﻿using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Rendering;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using System.Runtime.InteropServices;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualTexture
{
    [ExecuteInEditMode]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(Camera))]
    public class VirtualMaterialMapCamera : MonoBehaviour
    {
        /// <summary>
        /// 用于开启VT功能的关键字
        /// </summary>
        private readonly string m_VirtualMaterialMapsKeyword = "_VIRTUAL_MATERIAL_MAPS";

        /// <summary>
        /// 用于开启VT功能的关键字
        /// </summary>
        private GlobalKeyword m_VirtualMaterialMapsKeywordFeature;

        /// <summary>
        /// 当前场景的烘焙数据
        /// </summary>
        private VirtualMaterialMaps m_VirtualMaterialMaps;

        /// <summary>
        /// 当前场景的虚拟纹理
        /// </summary>
        private VirtualTexture2D m_VirtualTexture;

        /// <summary>
        /// 优先加载可见的Page
        /// </summary>
        private Plane[] m_CullingPlanes = new Plane[6];

        /// <summary>
        /// 渲染相机
        /// </summary>
        private Camera m_Camera;

        /// <summary>
        /// 渲染相机
        /// </summary>
        private Transform m_CameraTransform;

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
        /// 当前场景所有联级的映射矩阵（CPU）
        /// </summary>
		private Vector4[] m_LightProjecionMatrixs;

        /// <summary>
        /// 当前场景所有联级的映射矩阵（GPU）
        /// </summary>
        private GraphicsBuffer m_LightProjecionMatrixBuffer;

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
            m_CommandBuffer = new CommandBuffer();
            m_CommandBuffer.name = "VirtualMaterialMaps.Render";

            m_CameraCommandBuffer = new CommandBuffer();
            m_CameraCommandBuffer.name = "VirtualMaterialMaps.Setup";

            var tilingCount = Mathf.CeilToInt(Mathf.Sqrt(m_MaxTilePool));

            m_LightProjecionMatrixs = VirtualMaterialMaps.useStructuredBuffer ? new Vector4[tilingCount * tilingCount] : new Vector4[VirtualMaterialMaps.maxUniformBufferSize];
            m_LightProjecionMatrixBuffer = VirtualMaterialMaps.useStructuredBuffer ? new GraphicsBuffer(GraphicsBuffer.Target.Structured, m_LightProjecionMatrixs.Length, Marshal.SizeOf<Matrix4x4>()) : null;

            m_PropertyBlock = new MaterialPropertyBlock();

            VirtualMaterialMapsManager.instance.RegisterCamera(this);
            RenderPipelineManager.beginCameraRendering += OnBeginCameraRendering;
        }

        public void OnDisable()
        {
            VirtualMaterialMapsManager.instance.UnregisterCamera(this);
            RenderPipelineManager.beginCameraRendering -= OnBeginCameraRendering;

            this.m_CommandBuffer?.Release();
            this.m_CommandBuffer = null;

            this.m_CameraCommandBuffer?.Release();
            this.m_CameraCommandBuffer = null;

            this.m_LightProjecionMatrixBuffer?.Release();
            this.m_LightProjecionMatrixBuffer = null;

            this.DestroyVirtualMaterialMaps();
        }

        public void Reset()
        {
            this.m_MaxTilePool = 64;
            this.maxPageRequestLimit = 1;
            this.Rebuild();
        }

        public void Rebuild()
        {
            DestroyVirtualMaterialMaps();
        }

        public void Update()
        {
            var virtualShadowMaps = VirtualMaterialMapsManager.instance.First();

            if (m_VirtualMaterialMaps != virtualShadowMaps)
            {
                m_VirtualMaterialMaps = virtualShadowMaps;

                if (m_VirtualMaterialMaps != null)
                    this.CreateVirtualMaterialMaps();
                else
                    this.DestroyVirtualMaterialMaps();
            }

            if (m_VirtualMaterialMaps != null && m_VirtualMaterialMaps.lightData != null)
            {
                this.UpdatePage();
                this.UpdateJob(maxPageRequestLimit);
                this.UpdateLookup();
            }
        }

        public RenderTexture GetTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetTexture(0) : null;
        }

        public RenderTexture GetLookupTexture()
        {
            return m_VirtualTexture != null ? m_VirtualTexture.GetLookupTexture() : null;
        }

        private void CreateVirtualMaterialMaps()
        {
            var tilingCount = Mathf.CeilToInt(Mathf.Sqrt(m_MaxTilePool));
            var textureFormat = new VirtualTextureFormat[] { new VirtualTextureFormat(RenderTextureFormat.ARGBHalf) };

            if (m_VirtualMaterialMaps.lightData != null)
            {
                var pageSize = m_VirtualMaterialMaps.lightData.pageSize;
                var maxResolution = m_VirtualMaterialMaps.lightData.maxResolution;
                var maxMipLevel = m_VirtualMaterialMaps.lightData.maxMipLevel;

                m_VirtualTexture = new VirtualTexture2D(maxResolution.ToInt(), tilingCount, textureFormat, pageSize, maxMipLevel);
            }
            else
            {
                m_VirtualTexture = new VirtualTexture2D(m_VirtualMaterialMaps.maxResolution.ToInt(), tilingCount, textureFormat, m_VirtualMaterialMaps.pageSize, m_VirtualMaterialMaps.maxMipLevel);
            }

            this.UpdatePage();
            this.UpdateJob(int.MaxValue, false);
            this.UpdateLookup();
        }

        private void DestroyVirtualMaterialMaps()
        {
            m_VirtualMaterialMaps = null;

            if (m_VirtualTexture != null)
            {
                m_VirtualTexture.Dispose();
                m_VirtualTexture = null;
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

        private void UpdatePage()
        {
            if (m_VirtualMaterialMaps.lightData == null)
                return;

            GeometryUtility.CalculateFrustumPlanes(GetCamera(), m_CullingPlanes);

            foreach (var it in m_VirtualMaterialMaps.lightData.tileBounds)
            {
                var page = it.Key;
                var bounds = it.Value;

                var cellSize = Mathf.Max(bounds.extents.x, bounds.extents.z);
                var cellSize2 = cellSize * cellSize;

                var estimate = Vector3.SqrMagnitude(bounds.center - m_CameraTransform.position) / cellSize2;
                if (estimate < levelOfDetail)
                {
                    if (GeometryUtility.TestPlanesAABB(m_CullingPlanes, bounds))
                        m_VirtualTexture.LoadPage(page.pageX, page.pageY, page.mipLevel);
                }
            }
        }

        private void ProcessRequest(RequestPageData req, bool async = true)
        {
            if (m_VirtualMaterialMaps.lightData != null)
            {
                var key = m_VirtualMaterialMaps.lightData.GetTexAsset(req);
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
            m_VirtualTexture.UpdateLookup();

            m_PropertyBlock.Clear();
            m_PropertyBlock.SetVectorArray(ShaderConstants._TiledIndex, m_VirtualTexture.tiledIndex);
        }

        private void OnBeginCameraRendering(ScriptableRenderContext ctx, Camera camera)
        {
            if (m_Camera != camera)
                return;

            if (m_VirtualMaterialMaps != null)
            {
                m_CameraCommandBuffer.Clear();

                m_CameraCommandBuffer.SetKeyword(m_VirtualMaterialMapsKeywordFeature, true);

                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualMaterialPageParams, new Vector4(m_VirtualTexture.pageSize, 1.0f / m_VirtualTexture.pageSize, m_VirtualTexture.maxPageLevel, 0));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualMaterialTileParams, new Vector4(m_VirtualTexture.tileSize, m_VirtualTexture.tilingCount, m_VirtualTexture.textireSize, 0));
                m_CameraCommandBuffer.SetGlobalVector(ShaderConstants._VirtualMaterialRegionParams, new Vector4(0.0f, 0.0f, 1.0f, 1.0f));

                m_CameraCommandBuffer.SetGlobalTexture(ShaderConstants._VirtualMaterialTileTexture, m_VirtualTexture.GetTexture(0));
                m_CameraCommandBuffer.SetGlobalTexture(ShaderConstants._VirtualMaterialLookupTexture, m_VirtualTexture.GetLookupTexture());

                if (VirtualShadowMaps.useStructuredBuffer)
                    m_CameraCommandBuffer.SetGlobalBuffer(ShaderConstants._VirtualMaterialMatrixs_SSBO, m_LightProjecionMatrixBuffer);
                else
                    m_CameraCommandBuffer.SetGlobalVectorArray(ShaderConstants._VirtualMaterialMatrixs, m_LightProjecionMatrixs);

                m_CameraCommandBuffer.SetRenderTarget(this.GetLookupTexture(), RenderBufferLoadAction.DontCare, RenderBufferStoreAction.Store);
                m_CameraCommandBuffer.ClearRenderTarget(true, true, Color.clear);

                m_CameraCommandBuffer.DrawMeshInstanced(
                    fullscreenMesh,
                    0,
                    m_VirtualMaterialMaps.drawLookupMaterial,
                    0,
                    m_VirtualTexture.tiledMatrixs,
                    m_VirtualTexture.tiledMatrixs.Length,
                    m_PropertyBlock);

                ctx.ExecuteCommandBuffer(m_CameraCommandBuffer);
            }
            else
            {
                m_CameraCommandBuffer.SetKeyword(m_VirtualMaterialMapsKeywordFeature, false);
            }
        }

        private bool OnBeginTileLoading(RequestPageData request, int tile, Texture2D texture)
        {
            m_LightProjecionMatrixs[tile] = m_VirtualMaterialMaps.lightData.GetMatrix(request.pageX, request.pageY, request.mipLevel);

            m_CommandBuffer.Clear();

            if (VirtualShadowMaps.useStructuredBuffer)
                m_CommandBuffer.SetBufferData(m_LightProjecionMatrixBuffer, m_LightProjecionMatrixs);
            else
                m_CommandBuffer.SetGlobalVectorArray(ShaderConstants._VirtualMaterialMatrixs, m_LightProjecionMatrixs);

            m_CommandBuffer.SetGlobalTexture(ShaderConstants._MainTex, texture);
            m_CommandBuffer.SetRenderTarget(m_VirtualTexture.GetTexture(0), RenderBufferLoadAction.Load, RenderBufferStoreAction.Store);
            m_CommandBuffer.DrawMesh(fullscreenMesh, m_VirtualTexture.GetMatrix(tile), m_VirtualMaterialMaps.drawTileMaterial, 0);

            Graphics.ExecuteCommandBuffer(m_CommandBuffer);

            return true;
        }

        static class ShaderConstants
        {
            public static readonly int _MainTex = Shader.PropertyToID("_MainTex");

            public static readonly int _TiledIndex = Shader.PropertyToID("_TiledIndex");

            public static readonly int _VirtualMaterialMatrixs = Shader.PropertyToID("_VirtualMaterialMatrixs");
            public static readonly int _VirtualMaterialMatrixs_SSBO = Shader.PropertyToID("_VirtualMaterialMatrixs_SSBO");

            public static readonly int _VirtualMaterialPageParams = Shader.PropertyToID("_VirtualMaterialPageParams");
            public static readonly int _VirtualMaterialTileParams = Shader.PropertyToID("_VirtualMaterialTileParams");
            public static readonly int _VirtualMaterialRegionParams = Shader.PropertyToID("_VirtualMaterialRegionParams");
            
            public static readonly int _VirtualMaterialTileTexture = Shader.PropertyToID("_VirtualMaterialTileTexture");
            public static readonly int _VirtualMaterialLookupTexture = Shader.PropertyToID("_VirtualMaterialLookupTexture");
        }
    }
}