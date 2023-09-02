using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.Experimental.Rendering;
using Unity.VisualScripting;

namespace VirtualTexture
{
    [CustomEditor(typeof(VirtualShadowMaps))]
    public class VirtualShadowMapsEditor : Editor
    {
        private Camera m_Camera;
        private PageTable m_PageTable;
        private Material m_StaticShadowCasterMaterial;
        private List<MeshRenderer> m_Renderers;

        private VirtualShadowData m_VirtualShadowData;
        private VirtualShadowMaps m_VirtualShadowMaps { get { return target as VirtualShadowMaps; } }

        private void SaveRenderTexture(RenderTexture renderTexture, string filePath)
        {
            Graphics.SetRenderTarget(renderTexture);

            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, GraphicsFormat.R16_SFloat, TextureCreationFlags.None);
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0, false);
            texture.Apply();

            byte[] bytes = texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            File.WriteAllBytes(filePath, bytes);

            DestroyImmediate(texture);
        }

        private RenderTexture Render(RequestPageData request)
        {
            var x = request.pageX;
            var y = request.pageY;
            var mipScale = request.size;

            var regionRange = m_VirtualShadowMaps.regionRange;
            var lightTransform = m_VirtualShadowMaps.GetLightTransform();

            var cellWidth = regionRange.width / m_VirtualShadowMaps.pageSize * mipScale;
            var cellHeight = regionRange.height / m_VirtualShadowMaps.pageSize * mipScale;
            var cellRect = new Rect(regionRange.xMin + x * cellWidth, regionRange.yMin + y * cellHeight, cellWidth, cellHeight);
            var cellCenter = new Vector3(cellRect.center.x, 0, cellRect.center.y);

            var worldSize = new Vector3(regionRange.size.x, m_VirtualShadowData.bounds.size.y, regionRange.size.y);
            var worldBounds = new Bounds(m_VirtualShadowData.bounds.center, worldSize);
            var worldBoundsInLightSpace = VirtualShadowMapsUtilities.CalclateFitScene(worldBounds, m_VirtualShadowMaps.GetLightTransform().worldToLocalMatrix);

            var size = worldBounds.size;
            size.x /= m_VirtualShadowMaps.pageSize / mipScale;
            size.z /= m_VirtualShadowMaps.pageSize / mipScale;

            var bounds = new Bounds(worldBounds.center + cellCenter, size);
            var boundsInLightSpace = VirtualShadowMapsUtilities.CalclateFitScene(bounds, lightTransform.worldToLocalMatrix);
            var boundsInLightSpaceOrthographicSize = Mathf.Max(boundsInLightSpace.extents.x, boundsInLightSpace.extents.y);
            var boundsInLightSpaceLocalPosition = new Vector3(boundsInLightSpace.center.x, boundsInLightSpace.center.y, boundsInLightSpace.min.z - 0.05f);
            
            m_Camera.transform.localPosition = boundsInLightSpaceLocalPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(lightTransform.position);
            m_Camera.orthographicSize = boundsInLightSpaceOrthographicSize;
            m_Camera.nearClipPlane = 0.05f;
            m_Camera.farClipPlane = 0.05f + worldBoundsInLightSpace.size.z;
            m_Camera.ResetProjectionMatrix();

            var compactBounds = VirtualShadowMapsUtilities.CalculateBoundingBox(m_Renderers, m_Camera);
            compactBounds.min = new Vector3(bounds.min.x, compactBounds.min.y, bounds.min.z);
            compactBounds.max = new Vector3(bounds.max.x, compactBounds.max.y, bounds.max.z);

            var compactBoundsInLightSpace = VirtualShadowMapsUtilities.CalclateFitScene(compactBounds, lightTransform.worldToLocalMatrix);
            var compactBoundsInLightSpaceLocalPosition = new Vector3(compactBoundsInLightSpace.center.x, compactBoundsInLightSpace.center.y, compactBoundsInLightSpace.min.z - 0.05f);
            var compactPosition = new Vector3(compactBounds.center.x, compactBounds.max.y, compactBounds.center.z);
            var compactNormal = new Vector3(0.0f, 1.0f, 0.0f);

            m_Camera.transform.localPosition = compactBoundsInLightSpaceLocalPosition + lightTransform.worldToLocalMatrix.MultiplyPoint(lightTransform.position);
            m_Camera.orthographicSize = boundsInLightSpaceOrthographicSize;
            m_Camera.nearClipPlane = 0.05f;
            m_Camera.farClipPlane = 0.05f + compactBoundsInLightSpace.size.z;
            m_Camera.projectionMatrix = m_Camera.CalculateObliqueMatrix(VirtualShadowMapsUtilities.CameraSpacePlane(m_Camera, compactPosition, compactNormal, -1.0f));

            var cameraTexture = m_VirtualShadowMaps.GetCameraTexture();

            Graphics.SetRenderTarget(cameraTexture);
            GL.Clear(true, true, Color.black);
            GL.LoadIdentity();
            GL.LoadProjectionMatrix(m_Camera.projectionMatrix * m_Camera.worldToCameraMatrix);

            m_StaticShadowCasterMaterial.SetPass(0);

            var planes = GeometryUtility.CalculateFrustumPlanes(m_Camera);

            foreach (var it in m_Renderers)
            {
                if (GeometryUtility.TestPlanesAABB(planes, it.bounds))
                {
                    if (it.TryGetComponent<MeshFilter>(out var meshFilter))
                        Graphics.DrawMeshNow(meshFilter.sharedMesh, it.localToWorldMatrix);
                }
            }

            return cameraTexture;
        }

        public void GenerateShadowMaps()
        {
            m_VirtualShadowMaps.CreateCameraTexture(RenderTextureFormat.RGHalf);
            m_StaticShadowCasterMaterial = new Material(m_VirtualShadowMaps.castShader);

            var scene = SceneManager.GetActiveScene();
            var sceneName = Path.GetFileNameWithoutExtension(scene.path) + "_Shadow";
            var fileroot = Path.GetDirectoryName(scene.path);

            if (!AssetDatabase.IsValidFolder(Path.Join(fileroot, sceneName)))
                AssetDatabase.CreateFolder(fileroot, sceneName);

            m_Camera = m_VirtualShadowMaps.GetCamera();
            m_Renderers = m_VirtualShadowMaps.GetRenderers();

            m_VirtualShadowData = ScriptableObject.CreateInstance<VirtualShadowData>();
            m_VirtualShadowData.regionSize = m_VirtualShadowMaps.regionSize;
            m_VirtualShadowData.pageSize = m_VirtualShadowMaps.pageSize;
            m_VirtualShadowData.maxMipLevel = m_VirtualShadowMaps.maxMipLevel;
            m_VirtualShadowData.maxResolution = m_VirtualShadowMaps.maxResolution;
            m_VirtualShadowData.bounds = m_VirtualShadowMaps.CalculateBoundingBox();

            m_PageTable = new PageTable(m_VirtualShadowMaps.pageSize, m_VirtualShadowMaps.maxMipLevel);

            var requestPageJob = new RequestPageDataJob();

            for (int i = 0; i <= m_PageTable.maxMipLevel; i++)
            {
                foreach (var page in m_PageTable.pageLevelTable[i].pages)
                    requestPageJob.Request(page.x, page.y, page.mipLevel);
            }

            requestPageJob.Sort();

            var totalRequestCount = requestPageJob.requestCount;

            for (var i = 0; i < totalRequestCount; i++)
            {
                var request = requestPageJob.First();
                var cameraTexture = this.Render(request);

                var pageName = request.mipLevel + "-" + request.pageX + "-" + request.pageY;
                var outpath = Path.Join(fileroot, sceneName, "ShadowTexBytes-" + pageName + ".exr");

                SaveRenderTexture(cameraTexture, outpath);

                var projection = GL.GetGPUProjectionMatrix(m_Camera.projectionMatrix, false);
                var lightProjecionMatrix = VirtualShadowMapsUtilities.GetWorldToShadowMapSpaceMatrix(projection, m_Camera.worldToCameraMatrix);

                m_VirtualShadowData.SetMatrix(request, lightProjecionMatrix);
                m_VirtualShadowData.SetTexAsset(request, outpath);

                requestPageJob.Remove(request);

                if (EditorUtility.DisplayCancelableProgressBar("VirtualShadowMaps Baker", "Processing index:" + i + " total:" + totalRequestCount, i / (float)totalRequestCount))
                {
                    EditorUtility.ClearProgressBar();

                    return;
                }
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.Refresh();

            m_VirtualShadowData.SetupTextureImporter();
            m_VirtualShadowData.SaveAs(Path.Join(fileroot, sceneName));

            m_VirtualShadowMaps.shadowData = m_VirtualShadowData;

            AssetDatabase.Refresh();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Generate Shadow Maps"))
            {
                this.GenerateShadowMaps();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}