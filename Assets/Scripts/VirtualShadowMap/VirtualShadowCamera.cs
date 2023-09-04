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
        private GlobalKeyword m_VirtualShadowMapsKeywordFeature;

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
        /// 绘制Tile用的CommandBuffer
        /// </summary>
        private CommandBuffer m_CommandBuffer;

        /// <summary>
        /// 渲染相机
        /// </summary>
        private Camera m_Camera;

        /// <summary>
        /// 绘制Tile用的CommandBuffer
        /// </summary>
        private CommandBuffer m_CameraCommandBuffer;

        /// <summary>
        /// 渲染相机
        /// </summary>
        private Transform m_CameraTransform;

        /// <summary>
        /// 纹理缓存
        /// </summary>
        private Dictionary<RequestPageData, Texture2D> m_CacheTextures = new Dictionary<RequestPageData, Texture2D>();

        /// <summary>
        /// 绘制Tile用
        /// </summary>
        private Mesh m_TileMesh;

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
        private Rect m_RegionRange = new Rect(-128, -128, 128, 128);

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
        [Range(0, 256)]
        private int m_TilePool = 64;

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
            InitializeQuadMesh();

            m_VirtualShadowMapsKeywordFeature = GlobalKeyword.Create(m_VirtualShadowMapsKeyword);

            var tilingCount = Mathf.ClosestPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(m_TilePool)));

            m_LightProjecionMatrixs = new Matrix4x4[tilingCount * tilingCount];
            m_LightProjecionMatrixBuffer = new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LightProjecionMatrixs.Length, Marshal.SizeOf<Matrix4x4>());

            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "TileTexture.Render";

            m_CameraCommandBuffer = new CommandBuffer();
            m_CameraCommandBuffer.name = "VirtualShadowMaps.Setup";

            m_Camera.AddCommandBuffer(CameraEvent.BeforeLighting, m_CameraCommandBuffer);
        }

        public void OnDisable()
        {
            if (m_CameraCommandBuffer != null)
                this.m_Camera.RemoveCommandBuffer(CameraEvent.BeforeLighting, m_CameraCommandBuffer);

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
            this.m_TilePool = 64;
            this.maxPageRequestLimit = 1;
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

            m_CameraCommandBuffer.Clear();

            if ((Application.isPlaying && m_VirtualShadowMaps) || !Application.isPlaying && m_VirtualShadowMaps && m_VirtualShadowMaps.shadowData != null)
            {
                m_CameraCommandBuffer.SetKeyword(m_VirtualShadowMapsKeywordFeature, true);

                var orthographicSize = Mathf.Max(m_BoundsInLightSpace[0].extents.x, m_BoundsInLightSpace[0].extents.y);
                var biasScale = VirtualShadowMapsUtilities.CalculateBiasScale(orthographicSize, m_VirtualTexture.tileSize);
                var distanceShadowMask = QualitySettings.shadowmaskMode == ShadowmaskMode.DistanceShadowmask ? true : false;

                m_CameraCommandBuffer.SetGlobalVector("_VirtualShadowBiasParams", new Vector4(m_VirtualShadowMaps.bias * biasScale, m_VirtualShadowMaps.normalBias * biasScale * 1.414f, distanceShadowMask? 1: 0, 0));
                m_CameraCommandBuffer.SetGlobalVector("_VirtualShadowRegionParams", new Vector4(m_RegionRange.x, m_RegionRange.y, 1.0f / m_RegionRange.width, 1.0f / m_RegionRange.height));
                m_CameraCommandBuffer.SetGlobalVector("_VirtualShadowPageParams", new Vector4(m_VirtualTexture.pageSize, 1.0f / m_VirtualTexture.pageSize, m_VirtualTexture.maxPageLevel, 0));
                m_CameraCommandBuffer.SetGlobalVector("_VirtualShadowTileParams", new Vector4(m_VirtualTexture.tileSize, m_VirtualTexture.tilingCount, m_VirtualTexture.textireSize, 0));
                m_CameraCommandBuffer.SetGlobalVector("_VirtualShadowFeedbackParams", new Vector4(m_VirtualTexture.pageSize, m_VirtualTexture.pageSize * m_VirtualTexture.tileSize * m_RegionChangeScale.ToFloat(), m_VirtualTexture.maxPageLevel, 0));

                m_CameraCommandBuffer.SetGlobalTexture("_VirtualShadowTileTexture", m_VirtualTexture.GetTexture(0));
                m_CameraCommandBuffer.SetGlobalTexture("_VirtualShadowLookupTexture", m_VirtualTexture.GetLookupTexture());

                m_CameraCommandBuffer.SetGlobalBuffer("_VirtualShadowMatrixs", m_LightProjecionMatrixBuffer);

                this.UpdatePage();

                this.UpdateJob();
                this.UpdateLookup();
            }
            else
            {
                m_CameraCommandBuffer.SetKeyword(m_VirtualShadowMapsKeywordFeature, false);
            }
        }

        public Texture GetLookupTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetLookupTexture() : null;
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

        public Texture GetTileTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetTexture(0) : null;
        }

        private void CreateVirtualShadowMaps()
        {
            m_DrawTileMaterial = new Material(m_VirtualShadowMaps.drawDepthTileShader);
            m_DrawLookupMaterial = new Material(m_VirtualShadowMaps.drawLookupShader);
            m_DrawLookupMaterial.enableInstancing = true;

            var tilingCount = Mathf.ClosestPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(m_TilePool)));
            var textureFormat = new VirtualTextureFormat[] { new VirtualTextureFormat(RenderTextureFormat.Shadowmap) };

            if (m_VirtualShadowMaps.shadowData != null)
            {
                var pageSize = m_VirtualShadowMaps.shadowData.pageSize;
                var maxResolution = m_VirtualShadowMaps.shadowData.maxResolution;
                var maxMipLevel = m_VirtualShadowMaps.shadowData.maxMipLevel;

                m_RegionRange = m_VirtualShadowMaps.shadowData.regionRange;
                m_VirtualTexture = new VirtualTexture2D(maxResolution, tilingCount, textureFormat, pageSize, maxMipLevel);
            }
            else
            {
                m_RegionRange = m_VirtualShadowMaps.regionRange;
                m_VirtualTexture = new VirtualTexture2D(m_VirtualShadowMaps.maxResolution, tilingCount, textureFormat, m_VirtualShadowMaps.pageSize, m_VirtualShadowMaps.maxMipLevel);
            }

            m_VirtualTexture.LoadPageByLevel(m_VirtualTexture.maxPageLevel);

            this.UpdateBoundsInLightSpace();

            var totalRequestCount = m_VirtualTexture.GetRequestCount();

            for (var i = 0; i < totalRequestCount; i++)
            {
                var req = m_VirtualTexture.FirstRequest();
                if (req != null)
                    this.ProcessRequest(req, false);
            }
        }

        private void DestroyVirtualShadowMaps()
        {
            m_DrawTileMaterial = null;
            m_DrawLookupMaterial = null;
            m_VirtualShadowMaps = null;

            m_VirtualTexture?.Dispose();
            m_VirtualTexture = null;
        }

        private void UpdateBoundsInLightSpace()
        {
            var worldToLocalMatrix = m_VirtualShadowMaps.GetLightTransform().worldToLocalMatrix;

            m_WorldBounding = m_VirtualShadowMaps.shadowData != null ? m_VirtualShadowMaps.shadowData.bounds : m_VirtualShadowMaps.CalculateBoundingBox();
            m_BoundsInLightSpace = new Bounds[m_VirtualTexture.maxPageLevel + 1];

            for (var level = 0; level <= m_VirtualTexture.maxPageLevel; level++)
            {
                var perSize = 1 << (m_VirtualTexture.maxPageLevel - level);

                var size = m_WorldBounding.size;
                size.x /= perSize;
                size.z /= perSize;

                var bounds = new Bounds(m_WorldBounding.center, size);
                m_BoundsInLightSpace[level] = VirtualShadowMapsUtilities.CalclateFitScene(bounds, worldToLocalMatrix);
            }
        }

        private void UpdatePage()
        {
            GeometryUtility.CalculateFrustumPlanes(GetCamera(), m_CullingPlanes);

            for (int level = 0; level <= m_VirtualTexture.maxPageLevel; level++)
            {
                var mipScale = 1 << level;
                var pageSize = m_VirtualTexture.pageSize / mipScale;
                var cellSize = m_VirtualShadowMaps.regionSize / m_VirtualTexture.pageSize * mipScale;

                for (int y = 0; y < pageSize; y++)
                {
                    for (int x = 0; x < pageSize; x++)
                    {
                        var thisPos = new Vector3(m_RegionRange.xMin + (x + 0.5f) * cellSize, 0, m_RegionRange.yMin + (y + 0.5f) * cellSize);
                        var bound = new Bounds(thisPos, new Vector3(cellSize, cellSize, cellSize));

                        if (GeometryUtility.TestPlanesAABB(m_CullingPlanes, bound))
                        {
                            var estimate = Vector3.SqrMagnitude(thisPos - m_CameraTransform.position) / (cellSize * cellSize);
                            if (estimate < levelOfDetail)
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
                            if (handle.Status == AsyncOperationStatus.Succeeded && m_VirtualTexture != null)
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
                        };
                    }
                    else
                    {
                        var texture = Addressables.LoadAssetAsync<Texture2D>(key).WaitForCompletion();

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

        private void UpdateJob()
        {
            m_VirtualTexture.SortRequest();

            for (int i = 0; i < maxPageRequestLimit; i++)
            {
                var req = m_VirtualTexture.FirstRequest();
                if (req != null)
                    this.ProcessRequest(req);
            }
        }

        private void UpdateLookup()
        {
            if (m_DrawLookupMaterial != null)
                m_VirtualTexture.UpdateLookup(m_DrawLookupMaterial);
        }

        private Vector2Int GetFixedCenter(Vector2Int pos)
        {
            return new Vector2Int((int)Mathf.Floor(pos.x / regionChangeDistance + 0.5f) * (int)regionChangeDistance,
                                  (int)Mathf.Floor(pos.y / regionChangeDistance + 0.5f) * (int)regionChangeDistance);
        }

        private Vector2Int GetFixedPos(Vector3 pos)
        {
            return new Vector2Int((int)(Mathf.Floor(pos.x / m_VirtualShadowMaps.cellSize + 0.5f) * m_VirtualShadowMaps.cellSize),
                                  (int)(Mathf.Floor(pos.z / m_VirtualShadowMaps.cellSize + 0.5f) * m_VirtualShadowMaps.cellSize));
        }

        private void SetRegion(Rect region)
        {
            if (!m_RegionRange.Equals(region))
            {
                m_RegionRange = region;
                m_VirtualTexture.Clear();
            }
        }

        public bool UpdateRegion(Vector3 position)
        {
            var fixedPos = GetFixedPos(position);
            var xDiff = fixedPos.x - m_RegionRange.center.x;
            var yDiff = fixedPos.y - m_RegionRange.center.y;

            if (Mathf.Abs(xDiff) > regionChangeDistance || Mathf.Abs(yDiff) > regionChangeDistance)
            {
                var fixedCenter = GetFixedCenter(fixedPos);
                if (fixedCenter != m_RegionRange.center)
                {
                    var oldCenter = new Vector2Int((int)m_RegionRange.center.x, (int)m_RegionRange.center.y);
                    m_RegionRange = new Rect(fixedCenter.x - m_VirtualShadowMaps.regionSize / 2, fixedCenter.y - m_VirtualShadowMaps.regionSize / 2, m_VirtualShadowMaps.regionSize, m_VirtualShadowMaps.regionSize);

                    Vector2Int offset = (fixedCenter - oldCenter) / (int)m_VirtualShadowMaps.cellSize;

                    m_VirtualTexture.MovePageTable(offset);

                    return true;
                }
            }

            return false;
        }

        private void InitializeQuadMesh()
        {
            List<Vector3> quadVertexList = new List<Vector3>();
            List<int> quadTriangleList = new List<int>();
            List<Vector2> quadUVList = new List<Vector2>();

            quadVertexList.Add(new Vector3(0, 1, 0.1f));
            quadVertexList.Add(new Vector3(0, 0, 0.1f));
            quadVertexList.Add(new Vector3(1, 0, 0.1f));
            quadVertexList.Add(new Vector3(1, 1, 0.1f));

            quadUVList.Add(new Vector2(0, 0));
            quadUVList.Add(new Vector2(0, 1));
            quadUVList.Add(new Vector2(1, 1));
            quadUVList.Add(new Vector2(1, 0));

            quadTriangleList.Add(0);
            quadTriangleList.Add(1);
            quadTriangleList.Add(2);

            quadTriangleList.Add(2);
            quadTriangleList.Add(3);
            quadTriangleList.Add(0);

            m_TileMesh = new Mesh();
            m_TileMesh.SetVertices(quadVertexList);
            m_TileMesh.SetUVs(0, quadUVList);
            m_TileMesh.SetTriangles(quadTriangleList, 0);
        }

        private bool OnBeginTileLoading(RequestPageData request, int tile, Texture2D texture)
        {
            m_LightProjecionMatrixs[tile] = m_VirtualShadowMaps.shadowData.GetMatrix(request.pageX, request.pageY, request.mipLevel);

            m_CommandBuffer.Clear();
            m_CommandBuffer.SetGlobalTexture("_MainTex", texture);
            m_CommandBuffer.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            m_CommandBuffer.SetRenderTarget(m_VirtualTexture.GetTexture(0));
            m_CommandBuffer.DrawMesh(m_TileMesh, m_VirtualTexture.GetMatrix(tile), m_DrawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(m_CommandBuffer);

            return true;
        }

        private bool OnBeginTileRendering(RequestPageData request, int tile)
        {
            var x = request.pageX;
            var y = request.pageY;
            var mipScale = request.size;

            var cellWidth = m_RegionRange.width / m_VirtualShadowMaps.pageSize * mipScale;
            var cellHeight = m_RegionRange.height / m_VirtualShadowMaps.pageSize * mipScale;

            var realRect = new Rect(m_RegionRange.xMin + x * cellWidth, m_RegionRange.yMin + y * cellHeight, cellWidth, cellHeight);

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
            m_CommandBuffer.SetGlobalTexture("_MainTex", m_VirtualShadowMaps.GetCameraTexture());
            m_CommandBuffer.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            m_CommandBuffer.SetRenderTarget(m_VirtualTexture.GetTexture(0));
            m_CommandBuffer.DrawMesh(m_TileMesh, m_VirtualTexture.GetMatrix(tile), m_DrawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(m_CommandBuffer);

            return true;
        }
    }
}