using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VirtualTexture
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
	[RequireComponent(typeof(Camera))]
	public class VirtualShadowCamera : MonoBehaviour
	{
        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private readonly string m_VirtualShadowMapsKeyword = "_VIRTUAL_SHADOW_MAPS";

        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private readonly string m_VirtualShadowMapsPcssKeyword = "_VIRTUAL_SHADOW_MAPS_PCSS";

        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private GlobalKeyword m_VirtualShadowMapsKeywordFeature;

        /// <summary>
        /// 用于开启VSM功能的关键字
        /// </summary>
        private GlobalKeyword m_VirtualShadowMapsPcssKeywordFeature;

        /// <summary>
        /// 场景包围体
        /// </summary>
		private Bounds m_Bounds;

        /// <summary>
        /// 灯光空间场景包围体
        /// </summary>
        private Bounds[] m_BoundsInLightSpace;

        /// <summary>
        /// 当前场景的烘焙数据
        /// </summary>
        private VirtualShadowMaps m_VirtualShadowMaps;

        /// <summary>
        /// 当前场景的虚拟纹理
        /// </summary>
        private VirtualTexture2D m_VirtualTexture;

        /// <summary>
        /// 当前场景所有联级的投影矩阵（CPU）
        /// </summary>
		private Matrix4x4[] m_LightProjecionMatrixs;

        /// <summary>
        /// 当前场景所有联级的投影矩阵（GPU）
        /// </summary>
        private GraphicsBuffer m_LightProjecionMatrixBuffer;

        /// <summary>
        /// 渲染相机
        /// </summary>
        private Camera m_Camera;

        /// <summary>
        /// 绘制Tile用的CommandBuffer
        /// </summary>
        private CommandBuffer m_CommandBuffer;

        /// <summary>
        /// 绘制Tile用的CommandBuffer
        /// </summary>
        private CommandBuffer m_CameraCommandBuffer;

        /// <summary>
        /// 绘制Lookup的实例化
        /// </summary>
        private MaterialPropertyBlock m_PropertyBlock;

        /// <summary>
        /// 渲染相机
        /// </summary>
        private Transform m_CameraTransform;

        /// <summary>
        /// 绘制Tile用
        /// </summary>
        private static Mesh m_TileMesh = null;

        private static Mesh fullscreenMesh
        {
            get
            {
                if (m_TileMesh != null)
                    return m_TileMesh;

                m_TileMesh = new Mesh() { name = "Fullscreen Quad" };
                m_TileMesh.SetVertices(new List<Vector3>() {
                    new Vector3(0, 1, 0.0f),
                    new Vector3(0, 0, 0.0f),
                    new Vector3(1, 0, 0.0f),
                    new Vector3(1, 1, 0.0f)
                });

                m_TileMesh.SetUVs(0, new List<Vector2>()
                {
                    new Vector2(0, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 1),
                    new Vector2(1, 0)
                });

                m_TileMesh.SetIndices(new[] { 0, 1, 2, 2, 3, 0 }, MeshTopology.Triangles, 0, false);
                m_TileMesh.UploadMeshData(true);

                return m_TileMesh;
            }
        }

        /// <summary>
        /// 覆盖的区域.
        /// </summary>
        private ScaleFactor m_RegionChangeScale = ScaleFactor.Eighth;

        /// <summary>
        /// 覆盖的区域.
        /// </summary>
        public ScaleFactor regionChangeScale { get { return m_RegionChangeScale; } }

        /// <summary>
        /// 页表对应的世界刷新距离.
        /// </summary>
        public float regionChangeDistance { get => m_VirtualShadowMaps.regionSize * ScaleModeExtensions.ToFloat(m_RegionChangeScale); }

        /// <summary>
        /// Tile池.
        /// </summary>
        [SerializeField]
        [Min(1)]
        private int m_MaxTilePool = 64;

        /// <summary>
        /// 细分等级(数值越大加载的页表越多)
        /// </summary>
        [Space(10)]
        [Range(0, 10)]
        public float levelOfDetail = 1.0f;

        /// <summary>
        /// 一帧最多处理几个
        /// </summary>
        [Range(0, 10)]
        public int maxPageRequestLimit = 1;

        public void Awake()
        {
            m_Camera = GetComponent<Camera>();
            m_CameraTransform = m_Camera.transform;
        }

        public void OnEnable()
        {
            m_VirtualShadowMapsKeywordFeature = GlobalKeyword.Create(m_VirtualShadowMapsKeyword);
            m_VirtualShadowMapsPcssKeywordFeature = GlobalKeyword.Create(m_VirtualShadowMapsPcssKeyword);

            var tilingCount = Mathf.ClosestPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(m_MaxTilePool)));

            m_PropertyBlock = new MaterialPropertyBlock();

            m_LightProjecionMatrixs = VirtualShadowMaps.useStructuredBuffer ? new Matrix4x4[tilingCount * tilingCount] : new Matrix4x4[VirtualShadowMaps.maxUniformBufferSize];
            m_LightProjecionMatrixBuffer = VirtualShadowMaps.useStructuredBuffer ? new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LightProjecionMatrixs.Length, Marshal.SizeOf<Matrix4x4>()) : null;

            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "TileTexture.Render";

            m_CameraCommandBuffer = new CommandBuffer();
            m_CameraCommandBuffer.name = "VirtualShadowMaps.Setup";

            VirtualShadowManager.instance.RegisterCamera(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        public void OnDisable()
        {
            VirtualShadowManager.instance.UnregisterCamera(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            this.m_CommandBuffer?.Release();
            this.m_CommandBuffer = null;

            this.m_CameraCommandBuffer?.Release();
            this.m_CameraCommandBuffer = null;

            this.m_LightProjecionMatrixBuffer?.Release();
            this.m_LightProjecionMatrixBuffer = null;

            this.DestroyVirtualShadowMaps();
        }

        public void Reset()
        {
            this.m_MaxTilePool = 64;
            this.maxPageRequestLimit = 1;
            this.ResetShadowMaps();
        }

        public void ResetShadowMaps()
        {
            DestroyVirtualShadowMaps();
        }

        public void Update()
        {
            var virtualShadowMaps = VirtualShadowManager.instance.First();

            if (m_VirtualShadowMaps != virtualShadowMaps)
            {
                m_VirtualShadowMaps = virtualShadowMaps;

                if (m_VirtualShadowMaps != null)
                    this.CreateVirtualShadowMaps();
                else
                    this.DestroyVirtualShadowMaps();
            }

            if (m_VirtualShadowMaps != null && m_VirtualShadowMaps.shadowData != null)
            {
                this.UpdatePage();
                this.UpdateJob(maxPageRequestLimit);
                this.UpdateLookup();
            }
        }

        public Camera GetCamera()
        {
            if (m_Camera == null)
            {
                m_Camera = this.GetComponent<Camera>();
                m_CameraTransform = m_Camera.transform;
            }

            return m_Camera;
        }

        public Texture GetTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetTexture(0) : null;
        }

        public Texture GetLookupTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetLookupTexture() : null;
        }

        private void CreateVirtualShadowMaps()
        {
            var tilingCount = Mathf.ClosestPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(m_MaxTilePool)));
            var textureFormat = new VirtualTextureFormat[] { new VirtualTextureFormat(RenderTextureFormat.Shadowmap, FilterMode.Bilinear) };

            if (m_VirtualShadowMaps.shadowData != null)
            {
                var pageSize = m_VirtualShadowMaps.shadowData.pageSize;
                var maxResolution = m_VirtualShadowMaps.shadowData.maxResolution;
                var maxMipLevel = m_VirtualShadowMaps.shadowData.maxMipLevel;

                m_VirtualTexture = new VirtualTexture2D(maxResolution.ToInt(), tilingCount, textureFormat, pageSize, maxMipLevel);
            }
            else
            {
                m_VirtualTexture = new VirtualTexture2D(m_VirtualShadowMaps.maxResolution.ToInt(), tilingCount, textureFormat, m_VirtualShadowMaps.pageSize, m_VirtualShadowMaps.maxMipLevel);
            }

            this.UpdateBoundsInLightSpace();
            this.UpdatePage();
            this.UpdateJob(int.MaxValue, false);
            this.UpdateLookup();
        }

        private void DestroyVirtualShadowMaps()
        {
            m_VirtualShadowMaps = null;

            if (m_VirtualTexture != null)
            {
                m_VirtualTexture.Dispose();
                m_VirtualTexture = null;
            }
        }

        private void UpdateBoundsInLightSpace()
        {
            var worldToLocalMatrix = m_VirtualShadowMaps.shadowData ?
                m_VirtualShadowMaps.shadowData.worldToLocalMatrix :
                m_VirtualShadowMaps.GetLightTransform().worldToLocalMatrix;

            m_Bounds = m_VirtualShadowMaps.shadowData != null ? m_VirtualShadowMaps.shadowData.bounds : m_VirtualShadowMaps.CalculateBoundingBox();
            m_BoundsInLightSpace = new Bounds[m_VirtualTexture.maxPageLevel + 1];

            for (var level = 0; level <= m_VirtualTexture.maxPageLevel; level++)
            {
                var perSize = 1 << (m_VirtualTexture.maxPageLevel - level);

                var size = m_Bounds.size;
                size.x /= perSize;
                size.z /= perSize;

                var bounds = new Bounds(m_Bounds.center, size);
                m_BoundsInLightSpace[level] = bounds.CalclateFitScene(worldToLocalMatrix);
            }
        }

        private void UpdatePage()
        {
            var worldToLocalMatrix = m_VirtualShadowMaps.shadowData ?
                m_VirtualShadowMaps.shadowData.worldToLocalMatrix :
                m_VirtualShadowMaps.GetLightTransform().worldToLocalMatrix;

            var lightSpaceBounds = m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel];
            var lightSpaceMin = new Vector3(lightSpaceBounds.min.x, lightSpaceBounds.min.y, lightSpaceBounds.min.z);
            var lightSpaceRight = new Vector3(lightSpaceBounds.max.x, lightSpaceBounds.min.y, lightSpaceBounds.min.z);
            var lightSpaceBottom = new Vector3(lightSpaceBounds.min.x, lightSpaceBounds.max.y, lightSpaceBounds.min.z);
            var lightSpaceAxisX = Vector3.Normalize(lightSpaceRight - lightSpaceMin);
            var lightSpaceAxisY = Vector3.Normalize(lightSpaceBottom - lightSpaceMin);
            var lightSpaceWidth = (lightSpaceRight - lightSpaceMin).magnitude;
            var lightSpaceHeight = (lightSpaceBottom - lightSpaceMin).magnitude;

            var lightSpaceCameraPos = worldToLocalMatrix.MultiplyPoint(m_CameraTransform.position);
            lightSpaceCameraPos.z = lightSpaceBounds.min.z;

            var lightSpaceCameraVector = worldToLocalMatrix.MultiplyVector(m_CameraTransform.forward);
            lightSpaceCameraVector.z = 0;

            var minPageLevel = 0;
            var maxPageLevel = m_VirtualTexture.maxPageLevel + 1;

            for (int level = minPageLevel; level < maxPageLevel; level++)
            {
                var mipScale = 1 << level;
                var pageSize = m_VirtualTexture.pageSize / mipScale;

                var cellWidth = lightSpaceWidth / pageSize;
                var cellHeight = lightSpaceHeight / pageSize;
                var cellSize = Mathf.Max(cellWidth, cellHeight);
                var cellSize2 = 1.0f / (cellSize * cellSize);

                var lightSpaceCameraRect = new Rect(lightSpaceCameraPos.x, lightSpaceCameraPos.y, cellWidth * levelOfDetail, cellHeight * levelOfDetail);

                for (int y = 0; y < pageSize; y++)
                {
                    var posY = lightSpaceMin + lightSpaceAxisY * ((y + 0.5f) * cellHeight);

                    for (int x = 0; x < pageSize; x++)
                    {
                        var thisPos = lightSpaceAxisX * ((x + 0.5f) * cellWidth) + posY;
                        var estimate = Vector3.SqrMagnitude(thisPos - lightSpaceCameraPos) * cellSize2;
                        if (estimate < levelOfDetail)
                        {
                            var rect = new Rect(thisPos.x, thisPos.y, cellSize, cellSize);
                            if (rect.Overlaps(lightSpaceCameraRect))
                            {
                                m_VirtualTexture.LoadPage(x, y, level);
                            }
                            else
                            {
                                var angle = Vector3.Dot(lightSpaceCameraVector, (thisPos - lightSpaceCameraPos).normalized);
                                if (angle > 0.0f)
                                    m_VirtualTexture.LoadPage(x, y, level);
                            }
                        }
                    }
                }
            }
        }

        private void ProcessRequest(RequestPageData req, bool async = true)
        {
            if (m_VirtualShadowMaps.shadowData != null)
            {
                var key = m_VirtualShadowMaps.shadowData.GetTexAsset(req);
                if (key != null && key.Length > 0)
                {
                    if (async)
                    {
                        var handle = Addressables.LoadAssetAsync<Texture2D>(key);

                        handle.Completed += (AsyncOperationHandle<Texture2D> handle) =>
                        {
                            if (handle.Status == AsyncOperationStatus.Succeeded && handle.Result != null)
                            {
                                var page = m_VirtualTexture.GetPage(req.pageX, req.pageY, req.mipLevel);
                                if (page != null && page.payload.loadRequest.Equals(req))
                                {
                                    var tile = m_VirtualTexture.RequestTile();
                                    if (this.OnBeginTileLoading(req, tile, handle.Result))
                                    {
                                        m_VirtualTexture.ActivatePage(tile, page);
                                    }
                                }

                                Resources.UnloadAsset(handle.Result);
                            }
                            else
                            {
                                m_VirtualTexture.RemoveRequest(req);
                            }

                            Addressables.Release(handle);
                        };
                    }
                    else
                    {
                        var handle = Addressables.LoadAssetAsync<Texture2D>(key);
                        var texture = handle.WaitForCompletion();
                        if (texture != null)
                        {
                            var page = m_VirtualTexture.GetPage(req.pageX, req.pageY, req.mipLevel);
                            if (page != null && page.payload.loadRequest.Equals(req))
                            {
                                var tile = m_VirtualTexture.RequestTile();
                                if (this.OnBeginTileLoading(req, tile, texture))
                                {
                                    m_VirtualTexture.ActivatePage(tile, page);
                                }
                            }
                        }

                        Addressables.Release(handle);
                        Resources.UnloadAsset(texture);
                    }
                }
                else
                {
                    m_VirtualTexture.RemoveRequest(req);
                }
            }
            else if (Application.isPlaying)
            {
                var page = m_VirtualTexture.GetPage(req.pageX, req.pageY, req.mipLevel);
                if (page != null && page.payload.loadRequest.Equals(req))
                {
                    var tile = m_VirtualTexture.RequestTile();

                    if (this.OnBeginTileRendering(req, tile))
                    {
                        m_VirtualTexture.ActivatePage(tile, page);
                    }
                }
            }
        }

        private void UpdateJob(int num, bool async = true)
        {
            var requestCount = Mathf.Min(num, m_VirtualTexture.GetRequestCount());

            m_VirtualTexture.SortRequest();

            for (int i = 0; i < requestCount; i++)
            {
                var req = m_VirtualTexture.FirstRequest();
                if (req != null)
                    this.ProcessRequest(req.Value, async);
            }
        }

        private void UpdateLookup()
        {
            if (m_VirtualTexture.UpdateLookup())
            {
                m_PropertyBlock.Clear();
                m_PropertyBlock.SetVectorArray(ShaderConstants._TiledIndex, m_VirtualTexture.tiledIndex);
            }
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera camera)
        {
            if (m_Camera != camera)
                return;

            if (m_VirtualShadowMaps != null)
            {
                var lightSpaceBounds = m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel];
                var orthographicSize = Mathf.Max(m_BoundsInLightSpace[0].extents.x, m_BoundsInLightSpace[0].extents.y);
                var biasScale = VirtualShadowMapsUtilities.CalculateBiasScale(orthographicSize, m_VirtualTexture.tileSize);
                var distanceShadowMask = QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask ? true : false;
                var regionRange = new Rect(lightSpaceBounds.min.x, lightSpaceBounds.min.y, lightSpaceBounds.size.x, lightSpaceBounds.size.y);
                var worldToLocalMatrix = m_VirtualShadowMaps.shadowData ? m_VirtualShadowMaps.shadowData.worldToLocalMatrix : m_VirtualShadowMaps.GetLightTransform().worldToLocalMatrix;
                var softness = m_VirtualShadowMaps.softnesss / m_VirtualTexture.textireSize * (1 << m_VirtualTexture.maxPageLevel);

                m_CameraCommandBuffer.Clear();
                m_CameraCommandBuffer.SetGlobalMatrix(ShaderConstants._VirtualShadowLightMatrix, worldToLocalMatrix);
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualShadowBiasParams, new Vector4(m_VirtualShadowMaps.bias * biasScale, m_VirtualShadowMaps.normalBias * biasScale * 1.414f, distanceShadowMask ? 1 : 0, 0));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualShadowRegionParams, new Vector4(regionRange.x, regionRange.y, 1.0f / regionRange.width, 1.0f / regionRange.height));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualShadowPageParams, new Vector4(m_VirtualTexture.pageSize, 1.0f / m_VirtualTexture.pageSize, m_VirtualTexture.maxPageLevel, 0));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualShadowTileParams, new Vector4(m_VirtualTexture.tileSize, m_VirtualTexture.tilingCount, m_VirtualTexture.textireSize, 0));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualShadowFeedbackParams, new Vector4(m_VirtualTexture.pageSize, m_VirtualTexture.pageSize * m_VirtualTexture.tileSize * m_RegionChangeScale.ToFloat(), m_VirtualTexture.maxPageLevel, 0));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualShadowPcssParams, new Vector4(softness, m_VirtualShadowMaps.softnessNear, m_VirtualShadowMaps.softnessFar, 0));

                m_CameraCommandBuffer.SetGlobalTexture(ShaderConstants._VirtualShadowTileTexture, m_VirtualTexture.GetTexture(0));
                m_CameraCommandBuffer.SetGlobalTexture(ShaderConstants._VirtualShadowLookupTexture, m_VirtualTexture.GetLookupTexture());

                if (VirtualShadowMaps.useStructuredBuffer)
                    m_CameraCommandBuffer.SetGlobalBuffer(ShaderConstants._VirtualShadowMatrixs_SSBO, m_LightProjecionMatrixBuffer);
                else
                    m_CameraCommandBuffer.SetGlobalMatrixArray(ShaderConstants._VirtualShadowMatrixs, m_LightProjecionMatrixs);

                if (m_VirtualTexture.isTiledDirty)
                {
                    m_CameraCommandBuffer.SetRenderTarget(this.GetLookupTexture(), RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
                    m_CameraCommandBuffer.ClearRenderTarget(true, true, Color.clear);
                    m_CameraCommandBuffer.DrawMeshInstanced(
                        fullscreenMesh,
                        0,
                        m_VirtualShadowMaps.drawLookupMaterial,
                        0,
                        m_VirtualTexture.tiledMatrixs,
                        m_VirtualTexture.tiledCount,
                        m_PropertyBlock);
                }

                ctx.ExecuteCommandBuffer(m_CameraCommandBuffer);
            }
        }

        private bool OnBeginTileLoading(RequestPageData request, int tile, Texture2D texture)
        {
            m_LightProjecionMatrixs[tile] = m_VirtualShadowMaps.shadowData.GetMatrix(request.pageX, request.pageY, request.mipLevel);

            m_CommandBuffer.Clear();

            if (VirtualShadowMaps.useStructuredBuffer)
                m_CommandBuffer.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            else
                m_CommandBuffer.SetGlobalMatrixArray(ShaderConstants._VirtualShadowMatrixs, m_LightProjecionMatrixs);

            m_CommandBuffer.SetGlobalTexture(ShaderConstants._MainTex, texture);
            m_CommandBuffer.SetRenderTarget(m_VirtualTexture.GetTexture(0), RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            m_CommandBuffer.DrawMesh(fullscreenMesh, m_VirtualTexture.GetMatrix(tile), m_VirtualShadowMaps.drawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(m_CommandBuffer);

            return true;
        }

        private bool OnBeginTileRendering(RequestPageData request, int tile)
        {
            var x = request.pageX;
            var y = request.pageY;
            var mipScale = request.size;

            var lightSpaceBounds = m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel];
            var regionRange = new Rect(lightSpaceBounds.min.x, lightSpaceBounds.min.y, lightSpaceBounds.size.x, lightSpaceBounds.size.y);

            var cellWidth = regionRange.width / m_VirtualShadowMaps.pageSize * mipScale;
            var cellHeight = regionRange.height / m_VirtualShadowMaps.pageSize * mipScale;

            var realRect = new Rect(regionRange.xMin + x * cellWidth, regionRange.yMin + y * cellHeight, cellWidth, cellHeight);

            var lightTransform = m_VirtualShadowMaps.GetLightTransform();
            var wolrdPosition = lightTransform.position + new Vector3(realRect.center.x, 0, realRect.center.y);
            var localPosition = new Vector3(m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].center.x, m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].center.y, m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].min.z - 0.05f);
            var orthographicSize = Mathf.Max(m_BoundsInLightSpace[request.mipLevel].extents.x, m_BoundsInLightSpace[request.mipLevel].extents.y);

            m_VirtualShadowMaps.CreateCameraTexture(RenderTextureFormat.Shadowmap);
            m_VirtualShadowMaps.cameraTransform.localPosition = localPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(wolrdPosition);

            var shadowCamera = m_VirtualShadowMaps.GetCamera();
            shadowCamera.orthographicSize = orthographicSize;
            shadowCamera.nearClipPlane = 0.05f;
            shadowCamera.farClipPlane = 0.05f + m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].size.z;
            shadowCamera.Render();

            var projection = GL.GetGPUProjectionMatrix(shadowCamera.projectionMatrix, false);
            var lightProjecionMatrix = VirtualShadowMapsUtilities.GetWorldToShadowMapSpaceMatrix(projection, shadowCamera.worldToCameraMatrix);

            m_LightProjecionMatrixs[tile] = lightProjecionMatrix;

            m_CommandBuffer.Clear();

            if (VirtualShadowMaps.useStructuredBuffer)
                m_CommandBuffer.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            else
                m_CommandBuffer.SetGlobalMatrixArray(ShaderConstants._VirtualShadowMatrixs, m_LightProjecionMatrixs);

            m_CommandBuffer.SetGlobalTexture(ShaderConstants._MainTex, m_VirtualShadowMaps.GetCameraTexture());
            m_CommandBuffer.SetRenderTarget(m_VirtualTexture.GetTexture(0));
            m_CommandBuffer.DrawMesh(m_TileMesh, m_VirtualTexture.GetMatrix(tile), m_VirtualShadowMaps.drawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(m_CommandBuffer);

            return true;
        }

        static class ShaderConstants
        {
            public static readonly int _MainTex = Shader.PropertyToID("_MainTex");

            public static readonly int _TiledIndex = Shader.PropertyToID("_TiledIndex");

            public static readonly int _VirtualShadowMatrixs = Shader.PropertyToID("_VirtualShadowMatrixs");
            public static readonly int _VirtualShadowMatrixs_SSBO = Shader.PropertyToID("_VirtualShadowMatrixs_SSBO");
            public static readonly int _VirtualShadowLightMatrix = Shader.PropertyToID("_VirtualShadowLightMatrix");
            public static readonly int _VirtualShadowBiasParams = Shader.PropertyToID("_VirtualShadowBiasParams");
            public static readonly int _VirtualShadowPcssParams = Shader.PropertyToID("_VirtualShadowPcssParams");
            public static readonly int _VirtualShadowRegionParams = Shader.PropertyToID("_VirtualShadowRegionParams");
            public static readonly int _VirtualShadowPageParams = Shader.PropertyToID("_VirtualShadowPageParams");
            public static readonly int _VirtualShadowTileParams = Shader.PropertyToID("_VirtualShadowTileParams");
            public static readonly int _VirtualShadowFeedbackParams = Shader.PropertyToID("_VirtualShadowFeedbackParams");
            public static readonly int _VirtualShadowTileTexture = Shader.PropertyToID("_VirtualShadowTileTexture");
            public static readonly int _VirtualShadowLookupTexture = Shader.PropertyToID("_VirtualShadowLookupTexture");
        }
    }
}