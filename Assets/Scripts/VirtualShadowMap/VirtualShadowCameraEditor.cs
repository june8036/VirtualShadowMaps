#if UNITY_EDITOR
using UnityEditor;
using UnityEngine;

namespace VirtualTexture
{
    [CustomEditor(typeof(VirtualShadowCamera))]
    public class VirtualShadowCameraEditor : Editor
    {
        private VirtualShadowCamera m_VirtualShadowMaps { get { return target as VirtualShadowCamera; } }

        public override void OnInspectorGUI()
        {
            base.OnInspectorGUI();

            var tileTexture = m_VirtualShadowMaps.GetTileTexture();
            if (tileTexture)
            {
                GUILayout.Label("Tile Texture");

                Rect lastRect = GUILayoutUtility.GetAspectRect(1.0f);
                EditorGUI.DrawPreviewTexture(lastRect, tileTexture);

                this.Repaint();
            }

            var lookupTexture = m_VirtualShadowMaps.GetLookupTexture();
            if (lookupTexture)
            {
                GUILayout.Label("Lookup Texture");

                Rect lastRect = GUILayoutUtility.GetAspectRect(1.0f);
                EditorGUI.DrawPreviewTexture(lastRect, lookupTexture);

                this.Repaint();
            }

            if (GUILayout.Button("Refresh"))
            {
                m_VirtualShadowMaps.ResetShadowMaps();
            }

            if (GUI.changed)
            {
                EditorUtility.SetDirty(target);
            }
        }
    }
}
#endif