using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.Rendering;
using UnityEngine.Rendering.Universal;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace VirtualTexture
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
	[RequireComponent(typeof(Camera))]
	public class VirtualShadowCamera : MonoBehaviour
	{
        /// <summary>
        /// 场景包围体
        /// </summary>
		private Bounds m_WorldBounding;

        /// <summary>
        /// 灯光空间场景包围体
        /// </summary>
        private Bounds[] m_BoundsInLightSpace;

        /// <summary>
        /// 优先加载可见的Page
        /// </summary>
        private Plane[] m_CullingPlanes = new Plane[6];

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
                    new Vector3(0, 0, 0.0f),
                    new Vector3(0, 1, 0.0f),
                    new Vector3(1, 0, 0.0f),
                    new Vector3(1, 1, 0.0f)
                });

                m_TileMesh.SetUVs(0, new List<Vector2>()
                {
                    new Vector2(0, 0),
                    new Vector2(0, 1),
                    new Vector2(1, 0),
                    new Vector2(1, 1)
                });

                m_TileMesh.SetIndices(new[] { 0, 1, 2, 2, 1, 3 }, MeshTopology.Triangles, 0, false);
                m_TileMesh.UploadMeshData(true);

                return m_TileMesh;
            }
        }

        /// <summary>
        /// 绘制Tile材质
        /// </summary>
        private Material m_DrawTileMaterial;

        /// <summary>
        /// Lookup材质
        /// </summary>
        private Material m_DrawLookupMaterial;

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
        private int m_MaxTilePool = 32;

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
            m_LightProjecionMatrixs = new Matrix4x4[UniversalRenderPipeline.maxVisibleAdditionalLights];
            m_LightProjecionMatrixBuffer = VirtualShadowMaps.useStructuredBuffer ? new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LightProjecionMatrixs.Length, Marshal.SizeOf<Matrix4x4>()) : null;

            VirtualShadowManager.instance.RegisterCamera(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        public void OnDisable()
        {
            VirtualShadowManager.instance.UnregisterCamera(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            if (this.m_LightProjecionMatrixBuffer != null)
            {
                this.m_LightProjecionMatrixBuffer.Release();
                this.m_LightProjecionMatrixBuffer = null;
            }

            this.DestroyVirtualShadowMaps();
        }

        public void OnValidate()
        {
            this.m_MaxTilePool = Math.Min(this.m_MaxTilePool, UniversalRenderPipeline.maxVisibleAdditionalLights);
        }

        public void Reset()
        {
            this.m_MaxTilePool = UniversalRenderPipeline.maxVisibleAdditionalLights;
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

        public Texture GetLookupTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetLookupTexture() : null;
        }

        public Texture GetTileTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetTexture(0) : null;
        }

        private void CreateVirtualShadowMaps()
        {
            m_DrawTileMaterial = new Material(m_VirtualShadowMaps.drawDepthTileShader);
            m_DrawLookupMaterial = new Material(m_VirtualShadowMaps.drawLookupShader);
            m_DrawLookupMaterial.enableInstancing = true;

            var tilingCount = Mathf.CeilToInt(Mathf.Sqrt(m_MaxTilePool));
            var textureFormat = new VirtualTextureFormat[] { new VirtualTextureFormat(RenderTextureFormat.Shadowmap) };

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
            m_DrawTileMaterial = null;
            m_DrawLookupMaterial = null;
            m_VirtualShadowMaps = null;

            if (m_VirtualTexture != null)
            {
                m_VirtualTexture.Dispose();
                m_VirtualTexture = null;
            }
        }

        private void UpdateBoundsInLightSpace()
        {
            var cameraTransform = m_VirtualShadowMaps.GetCameraTransform();
            cameraTransform.position = Vector3.zero;

            m_WorldBounding = m_VirtualShadowMaps.shadowData != null ? m_VirtualShadowMaps.shadowData.bounds : m_VirtualShadowMaps.CalculateBoundingBox();
            m_BoundsInLightSpace = new Bounds[m_VirtualTexture.maxPageLevel + 1];

            for (var level = 0; level <= m_VirtualTexture.maxPageLevel; level++)
            {
                var perSize = 1 << (m_VirtualTexture.maxPageLevel - level);

                var size = m_WorldBounding.size;
                size.x /= perSize;
                size.z /= perSize;

                var bounds = new Bounds(m_WorldBounding.center, size);
                m_BoundsInLightSpace[level] = VirtualShadowMapsUtilities.CalclateFitScene(bounds, cameraTransform.worldToLocalMatrix);
            }
        }

        private void UpdatePage()
        {
            GeometryUtility.CalculateFrustumPlanes(GetCamera(), m_CullingPlanes);

            for (int level = 0; level <= m_VirtualTexture.maxPageLevel; level++)
            {
                var mipScale = 1 << level;
                var regionRange = m_VirtualShadowMaps.shadowData != null ? m_VirtualShadowMaps.shadowData.regionRange : m_VirtualShadowMaps.regionRange;
                var pageSize = m_VirtualTexture.pageSize / mipScale;
                var cellSize = m_VirtualShadowMaps.regionSize / m_VirtualTexture.pageSize * mipScale;
                var cellSize2 = cellSize * cellSize;

                for (int y = 0; y < pageSize; y++)
                {
                    var posY = regionRange.yMin + (y + 0.5f) * cellSize;

                    for (int x = 0; x < pageSize; x++)
                    {
                        var thisPos = new Vector3(regionRange.xMin + (x + 0.5f) * cellSize, 0, posY);
                        var estimate = Vector3.SqrMagnitude(thisPos - m_CameraTransform.position) / cellSize2;

                        if (estimate < levelOfDetail)
                        {
                            var bound = new Bounds(thisPos, new Vector3(cellSize, cellSize, cellSize));
                            if (GeometryUtility.TestPlanesAABB(m_CullingPlanes, bound))
                                m_VirtualTexture.LoadPage(x, y, level);
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
                                if (page != null && page.payload.loadRequest == req)
                                {
                                    var tile = m_VirtualTexture.RequestTile();
                                    if (this.OnBeginTileLoading(req, tile, handle.Result))
                                    {
                                        m_VirtualTexture.ActivatePage(tile, page);
                                    }
                                }
                            }

                            Addressables.Release(handle);
                            Resources.UnloadAsset(handle.Result);
                        };
                    }
                    else
                    {
                        var handle = Addressables.LoadAssetAsync<Texture2D>(key);
                        var texture = handle.WaitForCompletion();
                        if (texture != null)
                        {
                            var page = m_VirtualTexture.GetPage(req.pageX, req.pageY, req.mipLevel);
                            if (page != null && page.payload.loadRequest == req)
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
            }
            else if (Application.isPlaying)
            {
                var page = m_VirtualTexture.GetPage(req.pageX, req.pageY, req.mipLevel);
                if (page != null && page.payload.loadRequest == req)
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
            var requestCount = Math.Min(num, m_VirtualTexture.GetRequestCount());

            m_VirtualTexture.SortRequest();

            for (int i = 0; i < requestCount; i++)
            {
                var req = m_VirtualTexture.FirstRequest();
                if (req != null)
                    this.ProcessRequest(req, async);
            }
        }

        private void UpdateLookup()
        {
            if (m_DrawLookupMaterial != null)
                m_VirtualTexture.UpdateLookup(m_DrawLookupMaterial);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera camera)
        {
            if (m_Camera == camera && m_VirtualShadowMaps != null)
            {
                if (m_VirtualShadowMaps.shadowData != null)
                {
                    this.UpdateLookup();
                }

                var orthographicSize = Mathf.Max(m_BoundsInLightSpace[0].extents.x, m_BoundsInLightSpace[0].extents.y);
                var biasScale = VirtualShadowMapsUtilities.CalculateBiasScale(orthographicSize, m_VirtualTexture.tileSize);
                var distanceShadowMask = QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask ? true : false;
                var regionRange = m_VirtualShadowMaps.shadowData != null ? m_VirtualShadowMaps.shadowData.regionRange : m_VirtualShadowMaps.regionRange;

                var cmd = CommandBufferPool.Get();
                cmd.Clear();
                cmd.SetGlobalVector("_VirtualShadowBiasParams", new Vector4(m_VirtualShadowMaps.bias * biasScale, m_VirtualShadowMaps.normalBias * biasScale * 1.414f, distanceShadowMask ? 1 : 0, 0));
                cmd.SetGlobalVector("_VirtualShadowRegionParams", new Vector4(regionRange.x, regionRange.y, 1.0f / regionRange.width, 1.0f / regionRange.height));
                cmd.SetGlobalVector("_VirtualShadowPageParams", new Vector4(m_VirtualTexture.pageSize, 1.0f / m_VirtualTexture.pageSize, m_VirtualTexture.maxPageLevel, 0));
                cmd.SetGlobalVector("_VirtualShadowTileParams", new Vector4(m_VirtualTexture.tileSize, m_VirtualTexture.tilingCount, m_VirtualTexture.textireSize, 0));
                cmd.SetGlobalVector("_VirtualShadowFeedbackParams", new Vector4(m_VirtualTexture.pageSize, m_VirtualTexture.pageSize * m_VirtualTexture.tileSize * m_RegionChangeScale.ToFloat(), m_VirtualTexture.maxPageLevel, 0));

                cmd.SetGlobalTexture("_VirtualShadowTileTexture", m_VirtualTexture.GetTexture(0));
                cmd.SetGlobalTexture("_VirtualShadowLookupTexture", m_VirtualTexture.GetLookupTexture());

                if (VirtualShadowMaps.useStructuredBuffer)
                    cmd.SetGlobalBuffer("_VirtualShadowMatrixs_SSBO", m_LightProjecionMatrixBuffer);
                else
                    cmd.SetGlobalMatrixArray("_VirtualShadowMatrixs", m_LightProjecionMatrixs);

                ctx.ExecuteCommandBuffer(cmd);
                CommandBufferPool.Release(cmd);
            }
        }

        private bool OnBeginTileLoading(RequestPageData request, int tile, Texture2D texture)
        {
            m_LightProjecionMatrixs[tile] = m_VirtualShadowMaps.shadowData.GetMatrix(request.pageX, request.pageY, request.mipLevel);

            var cmd = CommandBufferPool.Get();
            cmd.Clear();

            if (VirtualShadowMaps.useStructuredBuffer)
                cmd.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            else
                cmd.SetGlobalMatrixArray("_VirtualShadowMatrixs", m_LightProjecionMatrixs);

            cmd.SetGlobalTexture("_MainTex", texture);
            cmd.SetRenderTarget(m_VirtualTexture.GetTexture(0));
            cmd.DrawMesh(fullscreenMesh, m_VirtualTexture.GetMatrix(tile), m_DrawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            return true;
        }

        private bool OnBeginTileRendering(RequestPageData request, int tile)
        {
            var x = request.pageX;
            var y = request.pageY;
            var mipScale = request.size;

            var regionRange = m_VirtualShadowMaps.shadowData != null ? m_VirtualShadowMaps.shadowData.regionRange : m_VirtualShadowMaps.regionRange;
            var cellWidth = regionRange.width / m_VirtualShadowMaps.pageSize * mipScale;
            var cellHeight = regionRange.height / m_VirtualShadowMaps.pageSize * mipScale;

            var realRect = new Rect(regionRange.xMin + x * cellWidth, regionRange.yMin + y * cellHeight, cellWidth, cellHeight);

            var lightTransform = m_VirtualShadowMaps.GetLightTransform();
            var wolrdPosition = lightTransform.position + new Vector3(realRect.center.x, 0, realRect.center.y);
            var localPosition = new Vector3(m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].center.x, m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].center.y, m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].min.z - 0.05f);
            var orthographicSize = Mathf.Max(m_BoundsInLightSpace[request.mipLevel].extents.x, m_BoundsInLightSpace[request.mipLevel].extents.y);

            m_VirtualShadowMaps.CreateCameraTexture(RenderTextureFormat.Shadowmap);
            m_VirtualShadowMaps.GetCameraTransform().localPosition = localPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(wolrdPosition);

            var shadowCamera = m_VirtualShadowMaps.GetCamera();
            shadowCamera.orthographicSize = orthographicSize;
            shadowCamera.nearClipPlane = 0.05f;
            shadowCamera.farClipPlane = 0.05f + m_BoundsInLightSpace[m_VirtualTexture.maxPageLevel].size.z;
            shadowCamera.Render();

            var projection = GL.GetGPUProjectionMatrix(shadowCamera.projectionMatrix, false) * shadowCamera.worldToCameraMatrix;
            var lightProjecionMatrix = VirtualShadowMapsUtilities.GetWorldToShadowMapSpaceMatrix(projection);

            m_LightProjecionMatrixs[tile] = lightProjecionMatrix;

            var cmd = CommandBufferPool.Get();
            cmd.Clear();

            if (VirtualShadowMaps.useStructuredBuffer)
                cmd.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            else
                cmd.SetGlobalMatrixArray("_VirtualShadowMatrixs", m_LightProjecionMatrixs);

            cmd.SetGlobalTexture("_MainTex", m_VirtualShadowMaps.GetCameraTexture());
            cmd.SetRenderTarget(m_VirtualTexture.GetTexture(0));
            cmd.DrawMesh(m_TileMesh, m_VirtualTexture.GetMatrix(tile), m_DrawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(cmd);
            CommandBufferPool.Release(cmd);

            return true;
        }
    }
}