#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualTexture
{
    [CustomEditor(typeof(VirtualMaterialMaps))]
    public sealed class VirtualMaterialMapsEditor : Editor
    {
        private VirtualMaterialMaps m_VirtualLightMaps { get { return target as VirtualMaterialMaps; } }

        public void GenerateTextureMaps()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneName = Path.GetFileNameWithoutExtension(scene.path) + "_Material";
            var fileroot = Path.GetDirectoryName(scene.path);

            if (!AssetDatabase.IsValidFolder(Path.Join(fileroot, sceneName)))
                AssetDatabase.CreateFolder(fileroot, sceneName);

            var virtualLightMapData = ScriptableObject.CreateInstance<VirtualMaterialMapData>();
            virtualLightMapData.pageSize = m_VirtualLightMaps.pageSize;
            virtualLightMapData.maxMipLevel = m_VirtualLightMaps.maxMipLevel;
            virtualLightMapData.maxResolution = m_VirtualLightMaps.maxResolution;

            var pageTable = new PageTable(m_VirtualLightMaps.pageSize, m_VirtualLightMaps.maxMipLevel);

            var requestPageJob = new RequestPageDataJob();

            for (int i = 0; i <= pageTable.maxMipLevel; i++)
            {
                foreach (var page in pageTable.pageLevelTable[i].pages)
                    requestPageJob.Request(page.x, page.y, page.mipLevel);
            }

            requestPageJob.Sort();

            var totalRequestCount = requestPageJob.requestCount;

            using (var baker = new VirtualMaterialMapBaker(m_VirtualLightMaps))
            {
                for (var i = 0; i < totalRequestCount; i++)
                {
                    var request = requestPageJob.First().Value;
                    var pageName = request.mipLevel + "-" + request.pageX + "-" + request.pageY;
                    var outpath = Path.Join(fileroot, sceneName, "AlbedoTexBytes-" + pageName + ".exr");

                    baker.Render(request.pageX, request.pageY, request.mipLevel);

                    if (baker.SaveAsFile(outpath))
                    {
                        virtualLightMapData.SetBounds(request, baker.bounds);
                        virtualLightMapData.SetMatrix(request, baker.lightProjecionMatrix);
                        virtualLightMapData.SetTexAsset(request, outpath);
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

            virtualLightMapData.SetupTextureImporter();
            virtualLightMapData.SaveAs(Path.Join(fileroot, sceneName));

            m_VirtualLightMaps.lightData = virtualLightMapData;
            m_VirtualLightMaps.Refresh();

            AssetDatabase.Refresh();
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GUILayout.Space(10);

            if (GUILayout.Button("Generate Material Maps"))
            {
                this.GenerateTextureMaps();
            }

            GUILayout.Space(10);

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif