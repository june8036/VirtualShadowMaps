#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VirtualTexture
{
    [CustomEditor(typeof(VirtualMaterialMapCamera))]
    public class VirtualLightMapCameraEditor : Editor
    {
        private VirtualMaterialMapCamera m_VirtualLightMaps { get { return target as VirtualMaterialMapCamera; } }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var tileTexture = m_VirtualLightMaps.GetTexture();
            if (tileTexture)
            {
                GUILayout.Label("Tile Texture");

                Rect lastRect = GUILayoutUtility.GetAspectRect(1.0f);
                EditorGUI.DrawPreviewTexture(lastRect, tileTexture);

                this.Repaint();
            }

            var lookupTexture = m_VirtualLightMaps.GetLookupTexture();
            if (lookupTexture)
            {
                GUILayout.Label("Lookup Texture");

                Rect lastRect = GUILayoutUtility.GetAspectRect(1.0f);
                EditorGUI.DrawPreviewTexture(lastRect, lookupTexture);

                this.Repaint();
            }

            if (GUILayout.Button("Rebuild"))
            {
                m_VirtualLightMaps.Rebuild();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif