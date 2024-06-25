using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualTexture
{
    public static class VirtualMaterialMapsUtilities
    {
        public static bool SaveAsFile(RenderTexture renderTexture, string filePath)
        {
            RenderTexture savedRT = RenderTexture.active;

            Graphics.SetRenderTarget(renderTexture);

            Texture2D texture = new Texture2D(renderTexture.width, renderTexture.height, TextureFormat.RGBAFloat, false);
            texture.hideFlags = HideFlags.HideAndDontSave;
            texture.ReadPixels(new Rect(0, 0, renderTexture.width, renderTexture.height), 0, 0, false);
            texture.Apply();

            Graphics.SetRenderTarget(savedRT);

            byte[] bytes = texture.EncodeToEXR(Texture2D.EXRFlags.CompressZIP);
            File.WriteAllBytes(filePath, bytes);

            if (Application.isEditor)
                UnityEngine.Object.DestroyImmediate(texture);
            else
                UnityEngine.Object.Destroy(texture);

            return true;
        }

        public static Matrix4x4 GetTextureScaleMatrix()
        {
            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 =  2.0f;
            textureScaleAndBias.m11 =  2.0f;
            textureScaleAndBias.m22 =  0.0f;
            textureScaleAndBias.m03 = -1.0f;
            textureScaleAndBias.m13 = -1.0f;
            textureScaleAndBias.m23 =  0.0f;

            return textureScaleAndBias;
        }
    }
}