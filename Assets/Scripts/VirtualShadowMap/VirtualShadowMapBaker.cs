using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;
using UnityEngine.Experimental.Rendering;

namespace VirtualTexture
{
    public sealed class VirtualShadowMapBaker : IDisposable
    {
        /// <summary>
        /// 光源朝向的相机
        /// </summary>
        private Camera m_Camera;

        /// <summary>
        /// 渲染的静态物体
        /// </summary>
        private List<MeshRenderer> m_Renderers;

        /// <summary>
        /// 投影着色器
        /// </summary>
        private Material m_Material;

        /// <summary>
        /// 世界包围体
        /// </summary>
        private Bounds m_WorldBounds;

        /// <summary>
        /// 烘焙需要的数据
        /// </summary>
        private VirtualShadowMaps m_VirtualShadowMaps;

        /// <summary>
        /// 渲染单个ShadowMap需要的纹理
        /// </summary>
		private RenderTexture m_StaticShadowMap;

        /// <summary>
        /// 渲染单个ShadowMap需要的投影矩阵
        /// </summary>
        public Matrix4x4 lightProjecionMatrix;

        /// <summary>
        /// 渲染单个ShadowMap需要的纹理
        /// </summary>
		public RenderTexture shadowMap { get => m_StaticShadowMap; }

        public VirtualShadowMapBaker(VirtualShadowMaps virtualShadowMaps)
        {
            m_Camera = virtualShadowMaps.GetCamera();
            m_Renderers = virtualShadowMaps.GetRenderers();
            m_Material = virtualShadowMaps.castMaterial;
            m_WorldBounds = virtualShadowMaps.CalculateBoundingBox();
            m_VirtualShadowMaps = virtualShadowMaps;

            m_StaticShadowMap = new RenderTexture(virtualShadowMaps.maxResolution.ToInt(), virtualShadowMaps.maxResolution.ToInt(), 16, RenderTextureFormat.RGHalf);
            m_StaticShadowMap.name = "StaticShadowMap";
            m_StaticShadowMap.useMipMap = false;
            m_StaticShadowMap.autoGenerateMips = false;
            m_StaticShadowMap.filterMode = FilterMode.Point;
            m_StaticShadowMap.wrapMode = TextureWrapMode.Clamp;
        }

        ~VirtualShadowMapBaker()
        {
            this.Dispose();
        }

        public RenderTexture Render(int x, int y, int level)
        {
            var mipScale = 1 << level;
            var clipOffset = 0.05f;

            var regionRange = m_VirtualShadowMaps.regionRange;
            var lightTransform = m_VirtualShadowMaps.GetLightTransform();

            var cellWidth = regionRange.width / m_VirtualShadowMaps.pageSize * mipScale;
            var cellHeight = regionRange.height / m_VirtualShadowMaps.pageSize * mipScale;
            var cellRect = new Rect(regionRange.xMin + x * cellWidth, regionRange.yMin + y * cellHeight, cellWidth, cellHeight);
            var cellCenter = new Vector3(cellRect.center.x, 0, cellRect.center.y);

            var size = m_WorldBounds.size;
            size.x /= m_VirtualShadowMaps.pageSize / mipScale;
            size.z /= m_VirtualShadowMaps.pageSize / mipScale;

            var bounds = new Bounds(m_WorldBounds.center + cellCenter, size);
            var boundsInLightSpace = VirtualShadowMapsUtilities.CalclateFitScene(bounds, lightTransform.worldToLocalMatrix);
            var boundsInLightSpaceOrthographicSize = Mathf.Max(boundsInLightSpace.extents.x, boundsInLightSpace.extents.y);
            var boundsInLightSpaceLocalPosition = new Vector3(boundsInLightSpace.center.x, boundsInLightSpace.center.y, boundsInLightSpace.min.z - clipOffset);

            m_Camera.transform.localPosition = boundsInLightSpaceLocalPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(lightTransform.position);
            m_Camera.aspect = 1.0f;
            m_Camera.orthographicSize = boundsInLightSpaceOrthographicSize;
            m_Camera.nearClipPlane = clipOffset;
            m_Camera.farClipPlane = clipOffset + boundsInLightSpace.size.z;
            m_Camera.ResetProjectionMatrix();

            var obliqueBounds = VirtualShadowMapsUtilities.CalculateBoundingBox(m_Renderers, m_Camera);
            obliqueBounds.min = new Vector3(bounds.min.x, obliqueBounds.min.y, bounds.min.z);
            obliqueBounds.max = new Vector3(bounds.max.x, obliqueBounds.max.y, bounds.max.z);

            var obliqueNormal = Vector3.up;
            var obliquePosition = new Vector3(obliqueBounds.center.x, obliqueBounds.max.y, obliqueBounds.center.z) + obliqueNormal * clipOffset;
            var obliqueSlope = 1 - Mathf.Clamp01(Vector3.Dot(-lightTransform.forward, obliqueNormal));
            var obliqueBoundsInLightSpace = VirtualShadowMapsUtilities.CalclateFitScene(obliqueBounds, lightTransform.worldToLocalMatrix);
            var obliqueBoundsInLightSpaceLocalPosition = new Vector3(obliqueBoundsInLightSpace.center.x, obliqueBoundsInLightSpace.center.y, obliqueBoundsInLightSpace.min.z);

            m_Camera.transform.localPosition = obliqueBoundsInLightSpaceLocalPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(lightTransform.position - lightTransform.forward * (obliqueBounds.size.y * obliqueSlope));
            m_Camera.orthographicSize = boundsInLightSpaceOrthographicSize;
            m_Camera.nearClipPlane = clipOffset;
            m_Camera.farClipPlane = clipOffset + obliqueBounds.size.y;
            m_Camera.projectionMatrix = m_Camera.CalculateObliqueMatrix(VirtualShadowMapsUtilities.CameraSpacePlane(m_Camera, obliquePosition, obliqueNormal, -1.0f));

            RenderTexture savedRT = RenderTexture.active;

            Graphics.SetRenderTarget(m_StaticShadowMap);
            GL.Clear(true, true, Color.black);
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(m_Camera.projectionMatrix * m_Camera.worldToCameraMatrix);

            var planes = GeometryUtility.CalculateFrustumPlanes(m_Camera);

            foreach (var it in m_Renderers)
            {
                if (GeometryUtility.TestPlanesAABB(planes, it.bounds))
                {
                    m_Material.CopyPropertiesFromMaterial(it.sharedMaterial);
                    m_Material.SetPass(0);

                    if (it.TryGetComponent<MeshFilter>(out var meshFilter))
                        Graphics.DrawMeshNow(meshFilter.sharedMesh, it.localToWorldMatrix);
                }
            }

            Graphics.SetRenderTarget(savedRT);

            var projection = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
            lightProjecionMatrix = VirtualShadowMapsUtilities.GetWorldToShadowMapSpaceMatrix(projection, m_Camera.worldToCameraMatrix);

            return m_StaticShadowMap;
        }

        public bool SaveRenderTexture(string filePath)
        {
            RenderTexture savedRT = RenderTexture.active;

            Graphics.SetRenderTarget(m_StaticShadowMap);

            Texture2D texture = new Texture2D(m_StaticShadowMap.width, m_StaticShadowMap.height, GraphicsFormat.R16_SFloat, TextureCreationFlags.None);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.ReadPixels(new Rect(0, 0, m_StaticShadowMap.width, m_StaticShadowMap.height), 0, 0, false);
            texture.Apply();

            Graphics.SetRenderTarget(savedRT);

            var tileNums = VirtualShadowMapsUtilities.CalculateTileNums(texture, VirtualShadowData.s_SplitBlockSize);
            if (tileNums > 0)
            {
                byte[] bytes = texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
                File.WriteAllBytes(filePath, bytes);
            }

            if (Application.isEditor)
                UnityEngine.Object.DestroyImmediate(texture);
            else
                UnityEngine.Object.Destroy(texture);

            return (tileNums > 0) ? true : false;
        }

        public void Dispose()
        {
            if (m_StaticShadowMap != null)
            {
                if (m_Camera != null)
                    m_Camera.targetTexture = null;

                m_StaticShadowMap.Release();
                m_StaticShadowMap = null;
            }
        }
    }
}