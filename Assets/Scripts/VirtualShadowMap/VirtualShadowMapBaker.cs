using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class VirtualShadowMapBaker : IDisposable
    {
        private Camera m_Camera;
        private List<MeshRenderer> m_Renderers;
        private Material m_Material;
        private Bounds m_WorldBounds;
        private VirtualShadowMaps m_VirtualShadowMaps;

        public Matrix4x4 lightProjecionMatrix;

        public VirtualShadowMapBaker(VirtualShadowMaps virtualShadowMaps)
        {
            var bounds = virtualShadowMaps.CalculateBoundingBox();
            var regionRange = virtualShadowMaps.regionRange;
            var worldSize = new Vector3(regionRange.size.x, bounds.size.y, regionRange.size.y);

            m_Camera = virtualShadowMaps.GetCamera();
            m_Renderers = virtualShadowMaps.GetRenderers();
            m_Material = new Material(virtualShadowMaps.castShader);
            m_WorldBounds = new Bounds(bounds.center, worldSize);
            m_VirtualShadowMaps = virtualShadowMaps;
        }

        ~VirtualShadowMapBaker()
        {
            this.Dispose();
        }

        public RenderTexture Render(RequestPageData request)
        {
            m_VirtualShadowMaps.CreateCameraTexture(RenderTextureFormat.RGHalf);

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
            m_Camera.orthographicSize = boundsInLightSpaceOrthographicSize;
            m_Camera.nearClipPlane = clipOffset;
            m_Camera.farClipPlane = clipOffset + boundsInLightSpace.size.z;
            m_Camera.ResetProjectionMatrix();

            var obliqueBounds = VirtualShadowMapsUtilities.CalculateBoundingBox(m_Renderers, m_Camera);
            obliqueBounds.min = new Vector3(bounds.min.x, obliqueBounds.min.y, bounds.min.z);
            obliqueBounds.max = new Vector3(bounds.max.x, obliqueBounds.max.y, bounds.max.z);

            var obliqueNormal = Vector3.up;
            var obliquePosition = new Vector3(obliqueBounds.center.x, obliqueBounds.max.y, obliqueBounds.center.z) + obliqueNormal * clipOffset;
            var obliqueBoundsInLightSpace = VirtualShadowMapsUtilities.CalclateFitScene(obliqueBounds, lightTransform.worldToLocalMatrix);
            var obliqueBoundsInLightSpaceLocalPosition = new Vector3(obliqueBoundsInLightSpace.center.x, obliqueBoundsInLightSpace.center.y, obliqueBoundsInLightSpace.min.z);

            m_Camera.transform.localPosition = obliqueBoundsInLightSpaceLocalPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(lightTransform.position);
            m_Camera.orthographicSize = boundsInLightSpaceOrthographicSize;
            m_Camera.nearClipPlane = clipOffset;
            m_Camera.farClipPlane = clipOffset + obliqueBounds.size.y * Mathf.Clamp01(Vector3.Dot(-lightTransform.forward, obliqueNormal));
            m_Camera.projectionMatrix = m_Camera.CalculateObliqueMatrix(VirtualShadowMapsUtilities.CameraSpacePlane(m_Camera, obliquePosition, obliqueNormal, -1.0f));

            var cameraTexture = m_VirtualShadowMaps.GetCameraTexture();

            Graphics.SetRenderTarget(cameraTexture);
            GL.Clear(true, true, Color.black);
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(m_Camera.projectionMatrix * m_Camera.worldToCameraMatrix);

            m_Material.SetPass(0);

            var planes = GeometryUtility.CalculateFrustumPlanes(m_Camera);

            foreach (var it in m_Renderers)
            {
                if (GeometryUtility.TestPlanesAABB(planes, it.bounds))
                {
                    if (it.TryGetComponent<MeshFilter>(out var meshFilter))
                        Graphics.DrawMeshNow(meshFilter.sharedMesh, it.localToWorldMatrix);
                }
            }

            var projection = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
            lightProjecionMatrix = VirtualShadowMapsUtilities.GetWorldToShadowMapSpaceMatrix(projection, m_Camera.worldToCameraMatrix);

            return cameraTexture;
        }

        public void Dispose()
        {
            m_VirtualShadowMaps?.DestroyCameraTexture();
        }
    }
}