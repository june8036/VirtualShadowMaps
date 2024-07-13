#if UNITY_EDITOR
using UnityEngine;
using UnityEditor;

namespace VirtualTexture
{
    public class VirtualShadowPostprocessor : AssetPostprocessor
    {
        private static string[] kConsolePlatforms = { "Standalone", "Android", "iPhone", "GameCoreXboxOne", "GameCoreScarlett", "PS5", };

        private void OnPreprocessTexture()
        {
            if (this.assetPath.Contains("ShadowTexBytes-"))
            {
                TextureImporterSettings settings = new TextureImporterSettings();
                settings.textureType = TextureImporterType.SingleChannel;
                settings.textureShape = TextureImporterShape.Texture2D;
                settings.alphaSource = TextureImporterAlphaSource.None;
                settings.sRGBTexture = false;
                settings.ignorePngGamma = true;
                settings.mipmapEnabled = false;
                settings.filterMode = FilterMode.Point;
                settings.wrapMode = TextureWrapMode.Clamp;
                settings.streamingMipmaps = false;
                settings.singleChannelComponent = TextureImporterSingleChannelComponent.Red;

                TextureImporter textureImporter = (TextureImporter)assetImporter;
                textureImporter.SetTextureSettings(settings);

                var defaultSettings = textureImporter.GetDefaultPlatformTextureSettings();
                defaultSettings.format = TextureImporterFormat.R16;
                defaultSettings.textureCompression = TextureImporterCompression.Uncompressed;

                textureImporter.SetPlatformTextureSettings(defaultSettings);

                foreach (string platform in kConsolePlatforms)
                {
                    var standaloneSettings = textureImporter.GetPlatformTextureSettings(platform);
                    standaloneSettings.overridden = true;
                    standaloneSettings.format = TextureImporterFormat.R16;
                    standaloneSettings.textureCompression = TextureImporterCompression.Uncompressed;

                    textureImporter.SetPlatformTextureSettings(standaloneSettings);
                }
            }
        }
    }
}
#endif