#if UNITY_EDITOR
using System.IO;
using System.Linq;
using UnityEditor;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace VirtualTexture
{
    [CustomEditor(typeof(VirtualLightMaps))]
    public sealed class VirtualLightMapsEditor : Editor
    {
        private VirtualLightMaps m_VirtualLightMaps { get { return target as VirtualLightMaps; } }

        public void GenerateLightMaps()
        {
            var scene = SceneManager.GetActiveScene();
            var sceneName = Path.GetFileNameWithoutExtension(scene.path) + "_Light";
            var fileroot = Path.GetDirectoryName(scene.path);

            if (!AssetDatabase.IsValidFolder(Path.Join(fileroot, sceneName)))
                AssetDatabase.CreateFolder(fileroot, sceneName);

            var virtualLightMapData = ScriptableObject.CreateInstance<VirtualLightMapData>();
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

            using (var baker = new VirtualLightMapBaker(m_VirtualLightMaps))
            {
                for (var i = 0; i < totalRequestCount; i++)
                {
                    var request = requestPageJob.First().Value;
                    var pageName = request.mipLevel + "-" + request.pageX + "-" + request.pageY;
                    var outpath = Path.Join(fileroot, sceneName, "LightTexBytes-" + pageName + ".exr");

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

        public static void RefreshFull()
        {
            var activeScene = SceneManager.GetActiveScene();
            var sceneCount = SceneManager.sceneCount;

            for (int i = 0; i < sceneCount; i++)
            {
                var scene = SceneManager.GetSceneAt(i);
                if (!scene.isLoaded) continue;
                SceneManager.SetActiveScene(scene);
                LightmapSettings.lightmaps = new LightmapData[0];
            }

            SceneManager.SetActiveScene(activeScene);
        }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            GUILayout.Space(10);

            if (GUILayout.Button("Generate Light Maps"))
            {
                this.GenerateLightMaps();
            }

            if (GUILayout.Button("Clear Baked Data"))
            {
                var guid = m_VirtualLightMaps.lightData.GetTexAsset(0, 0, m_VirtualLightMaps.lightData.maxMipLevel - 1);
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(path);

                var existingLmaps = LightmapSettings.lightmaps.ToList();
                foreach (var it in existingLmaps)
                    it.lightmapColor = texture;

                LightmapSettings.lightmaps = existingLmaps.ToArray();
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