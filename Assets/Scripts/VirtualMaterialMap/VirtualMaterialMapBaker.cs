using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class VirtualMaterialMapBaker : IDisposable
    {
        /// <summary>
        /// 获取WorldPos着色器
        /// </summary>
        private Material m_Material;

        /// <summary>
        /// 渲染的静态物体
        /// </summary>
        private List<Renderer> m_Renderers;

        /// <summary>
        /// 烘焙需要的数据
        /// </summary>
        private VirtualMaterialMaps m_VirtualMaterialMaps;

        /// <summary>
        /// 渲染单个LightMap需要的纹理
        /// </summary>
        private RenderTexture m_BakedAlbedoMap;

        /// <summary>
        /// 渲染单个LightMap需要的纹理
        /// </summary>
        private RenderTexture m_BakedTiledMap;

        /// <summary>
        /// 渲染单个LightMap需要的纹理
        /// </summary>
        private RenderTexture m_BakedWorldPosMap;

        /// <summary>
        /// 渲染单个LightMap需要的投影矩阵
        /// </summary>
        public Vector4 lightProjecionMatrix;

        /// <summary>
        /// 单个tile的世界包围体
        /// </summary>
        public Bounds bounds;

        /// <summary>
        /// 渲染单个LightMap需要的纹理
        /// </summary>
		public RenderTexture worldPosMap { get => m_BakedWorldPosMap; }

        /// <summary>
        /// 渲染单个AlbedoMap需要的纹理
        /// </summary>
		public RenderTexture tileMap { get => m_BakedTiledMap; }

        public VirtualMaterialMapBaker(VirtualMaterialMaps virtualLightMaps)
        {
            m_Material = virtualLightMaps.feedbackMaterial;
            m_Renderers = virtualLightMaps.GetRenderers();
            m_VirtualMaterialMaps = virtualLightMaps;

            m_BakedAlbedoMap = new RenderTexture(8192, 8192, 0, RenderTextureFormat.ARGB32);
            m_BakedAlbedoMap.name = "AlbedoMap";
            m_BakedAlbedoMap.useMipMap = false;
            m_BakedAlbedoMap.autoGenerateMips = false;
            m_BakedAlbedoMap.filterMode = FilterMode.Point;
            m_BakedAlbedoMap.wrapMode = TextureWrapMode.Clamp;
            m_BakedAlbedoMap.useMipMap = false;
            m_BakedAlbedoMap.Create();

            m_BakedTiledMap = new RenderTexture(virtualLightMaps.maxResolution.ToInt(), virtualLightMaps.maxResolution.ToInt(), 0, RenderTextureFormat.ARGB32);
            m_BakedTiledMap.name = "TileMap";
            m_BakedTiledMap.useMipMap = false;
            m_BakedTiledMap.autoGenerateMips = false;
            m_BakedTiledMap.filterMode = FilterMode.Point;
            m_BakedTiledMap.wrapMode = TextureWrapMode.Clamp;
            m_BakedTiledMap.useMipMap = false;
            m_BakedTiledMap.Create();

            m_BakedWorldPosMap = new RenderTexture(virtualLightMaps.maxResolution.ToInt(), virtualLightMaps.maxResolution.ToInt(), 0, RenderTextureFormat.ARGBFloat);
            m_BakedWorldPosMap.name = "WorldPosMap";
            m_BakedWorldPosMap.useMipMap = false;
            m_BakedWorldPosMap.autoGenerateMips = false;
            m_BakedWorldPosMap.filterMode = FilterMode.Point;
            m_BakedWorldPosMap.wrapMode = TextureWrapMode.Clamp;
            m_BakedWorldPosMap.useMipMap = false;
            m_BakedWorldPosMap.Create();

            RenderTexture savedRT = RenderTexture.active;

            Graphics.SetRenderTarget(m_BakedAlbedoMap);
            GL.Clear(true, true, Color.black);
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(VirtualMaterialMapsUtilities.GetTextureScaleMatrix());
            GL.modelview = Matrix4x4.identity;

            foreach (var it in m_Renderers)
            {
                var meta = it.sharedMaterial.FindPass("Meta");

                Shader.DisableKeyword("EDITOR_VISUALIZATION");

                Shader.SetGlobalInt("unity_VisualizationMode", 0);
                Shader.SetGlobalFloat("unity_OneOverOutputBoost", 1);
                Shader.SetGlobalFloat("unity_MaxOutputValue", 1);

                Shader.SetGlobalVector("unity_LightmapST", it.lightmapScaleOffset);
                Shader.SetGlobalVector("unity_DynamicLightmapST", it.lightmapScaleOffset);
                Shader.SetGlobalVector("unity_MetaVertexControl", new Vector4(1, 0, 0, 0));
                Shader.SetGlobalVector("unity_MetaFragmentControl", new Vector4(1, 0, 0, 0));

                it.sharedMaterial.SetPass(meta);

                if (it.TryGetComponent<MeshFilter>(out var meshFilter))
                    Graphics.DrawMeshNow(meshFilter.sharedMesh, Matrix4x4.identity);
            }

            Graphics.SetRenderTarget(m_BakedWorldPosMap);
            GL.Clear(true, true, Color.black);
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(Matrix4x4.identity);
            GL.modelview = Matrix4x4.identity;

            foreach (var it in m_Renderers)
            {
                m_Material.SetVector("lightmapScaleOffset", it.lightmapScaleOffset);
                m_Material.SetPass(0);

                if (it.TryGetComponent<MeshFilter>(out var meshFilter))
                    Graphics.DrawMeshNow(meshFilter.sharedMesh, it.localToWorldMatrix);
            }

            Graphics.SetRenderTarget(savedRT);
        }

        ~VirtualMaterialMapBaker()
        {
            this.Dispose();
        }

        public RenderTexture Render(int x, int y, int level)
        {
            var mipScale = 1 << level;

            var tiledSize = 1.0f / m_VirtualMaterialMaps.pageSize * mipScale;

            var OffsetX = x * tiledSize;
            var OffsetY = y * tiledSize;
            var tilingX = tiledSize;
            var tilingY = tiledSize;

            RenderTexture savedRT = RenderTexture.active;

            Graphics.Blit(m_BakedAlbedoMap, m_BakedTiledMap, new Vector2(tilingX, tilingY), new Vector2(OffsetX, OffsetY));

            Graphics.SetRenderTarget(m_BakedWorldPosMap);

            var worldOffsetX = Mathf.FloorToInt(OffsetX * m_BakedTiledMap.width);
            var worldOffsetY = Mathf.FloorToInt(OffsetY * m_BakedTiledMap.height);
            var worldTilingX = Mathf.FloorToInt(tilingX * m_BakedTiledMap.width);
            var worldTilingY = Mathf.FloorToInt(tilingY * m_BakedTiledMap.height);

            Texture2D texture = new Texture2D(worldTilingX, worldTilingY, TextureFormat.RGBAFloat, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.ReadPixels(new Rect(worldOffsetX, worldOffsetY, worldTilingX, worldTilingY), 0, 0, false);
            texture.Apply();

            Graphics.SetRenderTarget(savedRT);

            var bounds = new Bounds();
            bounds.max = Vector3.negativeInfinity;
            bounds.min = Vector3.positiveInfinity;

            var worldPos = texture.GetPixelData<Vector4>(0);

            foreach (var it in worldPos)
            {
                if (it.w > 0.0f)
                    bounds.Encapsulate(it);
            }

            UnityEngine.Object.DestroyImmediate(texture);

            this.bounds = bounds;
            this.lightProjecionMatrix = new Vector4(OffsetX, OffsetY, tilingX, tilingY);

            return m_BakedTiledMap;
        }

        public bool SaveAsFile(string filePath)
        {
            RenderTexture savedRT = RenderTexture.active;

            Graphics.SetRenderTarget(m_BakedTiledMap);

            Texture2D texture = new Texture2D(m_BakedTiledMap.width, m_BakedTiledMap.height, TextureFormat.RGBA32, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.ReadPixels(new Rect(0, 0, m_BakedTiledMap.width, m_BakedTiledMap.height), 0, 0, false);
            texture.Apply();

            Graphics.SetRenderTarget(savedRT);

            byte[] bytes = texture.EncodeToTGA();
            File.WriteAllBytes(filePath, bytes);

            UnityEngine.Object.DestroyImmediate(texture);
            return true;
        }

        public void Dispose()
        {
            if (m_BakedTiledMap != null)
            {
                m_BakedTiledMap.Release();
                m_BakedTiledMap = null;
            }

            if (m_BakedWorldPosMap != null)
            {
                m_BakedWorldPosMap.Release();
                m_BakedWorldPosMap = null;
            }
        }
    }
}