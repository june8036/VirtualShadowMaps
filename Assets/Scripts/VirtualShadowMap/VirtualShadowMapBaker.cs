using System;
using System.Collections.Generic;
using UnityEngine;

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
        /// 计算MinMax的包围
        /// </summary>
        private ComputeBuffer m_DepthBuffer;

        /// <summary>
        /// 渲染单个ShadowMap需要的纹理
        /// </summary>
		private RenderTexture m_DepthTextureRaw;

        /// <summary>
        /// 渲染单个ShadowMap需要的纹理
        /// </summary>
		private RenderTexture m_DepthTexture;

        /// <summary>
        /// 渲染单个ShadowMap需要的投影矩阵
        /// </summary>
        public Matrix4x4 lightProjecionMatrix;

        public VirtualShadowMapBaker(VirtualShadowMaps virtualShadowMaps)
        {
            m_Camera = virtualShadowMaps.GetCamera();
            m_Renderers = virtualShadowMaps.GetRenderers();
            m_Material = new Material(virtualShadowMaps.castShader);
            m_WorldBounds = virtualShadowMaps.CalculateBoundingBox();
            m_VirtualShadowMaps = virtualShadowMaps;

            m_DepthBuffer = new ComputeBuffer(16 * 16, sizeof(float) * 2);

            m_DepthTextureRaw = new RenderTexture(virtualShadowMaps.maxResolution, virtualShadowMaps.maxResolution, 16, RenderTextureFormat.RGHalf);
            m_DepthTextureRaw.name = "DepthMapRaw";
            m_DepthTextureRaw.useMipMap = false;
            m_DepthTextureRaw.autoGenerateMips = false;
            m_DepthTextureRaw.filterMode = FilterMode.Point;
            m_DepthTextureRaw.wrapMode = TextureWrapMode.Clamp;

            m_DepthTexture = new RenderTexture(virtualShadowMaps.maxResolution, virtualShadowMaps.maxResolution, 16, RenderTextureFormat.RGHalf);
            m_DepthTexture.name = "DepthMap";
            m_DepthTexture.enableRandomWrite = true;
            m_DepthTexture.useMipMap = false;
            m_DepthTexture.autoGenerateMips = false;
            m_DepthTexture.filterMode = FilterMode.Point;
            m_DepthTexture.wrapMode = TextureWrapMode.Clamp;
        }

        ~VirtualShadowMapBaker()
        {
            this.Dispose();
        }

        public RenderTexture Render(RequestPageData request)
        {
            var x = request.pageX;
            var y = request.pageY;
            var mipScale = request.size;
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

            Graphics.SetRenderTarget(m_DepthTextureRaw);
            GL.Clear(true, true, Color.black);
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(m_Camera.projectionMatrix * m_Camera.worldToCameraMatrix);

            m_Material.SetPass(0);

            var planes = GeometryUtility.CalculateFrustumPlanes(m_Camera);

            foreach (var it in m_Renderers)
            {
                if (GeometryUtility.TestPlanesAABB(planes, it.bounds))
                {
                    m_Material.CopyPropertiesFromMaterial(it.sharedMaterial);

                    if (it.TryGetComponent<MeshFilter>(out var meshFilter))
                        Graphics.DrawMeshNow(meshFilter.sharedMesh, it.localToWorldMatrix);
                }
            }

            m_VirtualShadowMaps.minMaxDepthShader.SetTexture(0, "depthMap", m_DepthTexture);
            m_VirtualShadowMaps.minMaxDepthShader.SetTexture(0, "depthMapRaw", m_DepthTextureRaw);
            m_VirtualShadowMaps.minMaxDepthShader.SetBuffer(0, "minMaxDepthBuffer", m_DepthBuffer);
            m_VirtualShadowMaps.minMaxDepthShader.Dispatch(0, 1, 1, 1);

            var projection = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
            lightProjecionMatrix = VirtualShadowMapsUtilities.GetWorldToShadowMapSpaceMatrix(projection, m_Camera.worldToCameraMatrix);

            return m_DepthTextureRaw;
        }

        public void Dispose()
        {
            if (m_Camera != null)
                m_Camera.targetTexture = null;

            if (m_DepthTexture != null)
            {
                m_DepthTexture.Release();
                m_DepthTexture = null;
            }

            if (m_DepthTextureRaw != null)
            {
                m_DepthTextureRaw.Release();
                m_DepthTextureRaw = null;
            }
        }
    }
}