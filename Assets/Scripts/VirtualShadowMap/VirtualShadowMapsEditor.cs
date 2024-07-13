#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualTexture
{
    [CustomEditor(typeof(VirtualShadowMaps))]
    public sealed class VirtualShadowMapsEditor : Editor
    {
        private VirtualShadowMaps m_VirtualShadowMaps { get { return target as VirtualShadowMaps; } }

        public void GenerateShadowMaps()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneName = Path.GetFileNameWithoutExtension(scene.path) + "_Shadow";
            var fileroot = Path.GetDirectoryName(scene.path);

            if (!AssetDatabase.IsValidFolder(Path.Join(fileroot, sceneName)))
                AssetDatabase.CreateFolder(fileroot, sceneName);

            var pageTable = new PageTable(m_VirtualShadowMaps.pageSize, m_VirtualShadowMaps.maxMipLevel);

            var requestPageJob = new RequestPageDataJob();

            for (int i = 0; i <= pageTable.maxMipLevel; i++)
            {
                foreach (var page in pageTable.pageLevelTable[i].pages)
                    requestPageJob.Request(page.x, page.y, page.mipLevel);
            }

            requestPageJob.Sort();

            var totalRequestCount = requestPageJob.requestCount;

            Dictionary<RequestPageData, string> texAssets = new Dictionary<RequestPageData, string>();
            Dictionary<RequestPageData, Matrix4x4> texLightProjecionMatrix = new Dictionary<RequestPageData, Matrix4x4>();

            using (var baker = new VirtualShadowMapBaker(m_VirtualShadowMaps))
            {
                for (var i = 0; i < totalRequestCount; i++)
                {
                    var request = requestPageJob.First().Value;
                    var pageName = request.mipLevel + "-" + request.pageX + "-" + request.pageY;
                    var outpath = Path.Join(fileroot, sceneName, "ShadowTexBytes-" + pageName + ".exr");

                    baker.Render(request.pageX, request.pageY, request.mipLevel);

                    if (baker.SaveRenderTexture(outpath))
                    {
                        texAssets[request] = outpath;
                        texLightProjecionMatrix[request] = baker.lightProjecionMatrix;
                    }

                    requestPageJob.Remove(request);

                    if (EditorUtility.DisplayCancelableProgressBar("VirtualShadowMaps Baker", "Processing index:" + i + " total:" + totalRequestCount, i / (float)totalRequestCount))
                    {
                        EditorUtility.ClearProgressBar();

                        return;
                    }
                }
            }

            EditorUtility.ClearProgressBar();

            AssetDatabase.Refresh();

            var m_VirtualShadowData = ScriptableObject.CreateInstance<VirtualShadowData>();
            m_VirtualShadowData.regionCenter = m_VirtualShadowMaps.regionCenter;
            m_VirtualShadowData.regionSize = m_VirtualShadowMaps.regionSize;
            m_VirtualShadowData.pageSize = m_VirtualShadowMaps.pageSize;
            m_VirtualShadowData.maxMipLevel = m_VirtualShadowMaps.maxMipLevel;
            m_VirtualShadowData.maxResolution = m_VirtualShadowMaps.maxResolution;
            m_VirtualShadowData.bounds = m_VirtualShadowMaps.CalculateBoundingBox();
            m_VirtualShadowData.worldToLocalMatrix = m_VirtualShadowMaps.GetLightTransform().worldToLocalMatrix;
            m_VirtualShadowData.localToWorldMatrix = m_VirtualShadowMaps.GetLightTransform().localToWorldMatrix;

            foreach (var it in texAssets)
                m_VirtualShadowData.SetTexAsset(it.Key, AssetDatabase.AssetPathToGUID(it.Value));

            foreach (var it in texLightProjecionMatrix)
                m_VirtualShadowData.SetMatrix(it.Key, it.Value);

            m_VirtualShadowData.SaveAs(Path.Join(fileroot, sceneName));

            m_VirtualShadowMaps.shadowData = m_VirtualShadowData;
            m_VirtualShadowMaps.Refresh();

            AssetDatabase.Refresh();
        }

        public void TurnOnShadows()
        {
            var renderers = m_VirtualShadowMaps.GetRenderers();

            foreach (var it in renderers)
            {
                if (it.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.Off)
                    it.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.On;
            }
        }

        public void TurnOffShadows()
        {
            var renderers = m_VirtualShadowMaps.GetRenderers();

            foreach (var it in renderers)
            {
                if (it.shadowCastingMode == UnityEngine.Rendering.ShadowCastingMode.On)
                    it.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            }
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            if (GUILayout.Button("Generate Shadow Maps"))
            {
                this.GenerateShadowMaps();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Calculate Region Box"))
            {
                m_VirtualShadowMaps.CalculateRegionBox();
            }

            GUILayout.Space(10);

            if (GUILayout.Button("Turn On Shadows (Ignore ShadowOnly)"))
            {
                this.TurnOnShadows();
            }

            if (GUILayout.Button("Turn Off Shadows (Ignore ShadowOnly)"))
            {
                this.TurnOffShadows();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif