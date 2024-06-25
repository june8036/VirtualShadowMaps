using System.IO;
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualTexture
{
    public sealed class VirtualLightMapData : ScriptableObject
    {
        /// <summary>
        /// 页表尺寸.
        /// </summary>
        public int pageSize = 16;

        /// <summary>
        /// 最大mipmap等级
        /// </summary>
        public int maxMipLevel = 4;

        /// <summary>
        /// 单个Tile的尺寸.
        /// </summary>
        public ShadowResolution maxResolution = ShadowResolution._1024;

        /// <summary>
        /// 纹理资源列表.
        /// </summary>
        [SerializeField]
        public SerializableDictionary<RequestPageData, string> texAssets = new SerializableDictionary<RequestPageData, string>();

        /// <summary>
        /// 纹理偏移列表.
        /// </summary>
        [SerializeField]
        public SerializableDictionary<RequestPageData, Vector4> texRect = new SerializableDictionary<RequestPageData, Vector4>();

        /// <summary>
        /// 每一个Tile覆盖的世界包围体.
        /// </summary>
        [SerializeField]
        public SerializableDictionary<RequestPageData, Bounds> tileBounds = new SerializableDictionary<RequestPageData, Bounds>();

        /// <summary>
        /// 资源数量.
        /// </summary>
        public int textureCount { get => texAssets.Count; }

        /// <summary>
        /// 添加纹理资源
        /// </summary>
        public void SetTexAsset(RequestPageData request, string key)
        {
            texAssets.Add(request, key);
        }

        /// <summary>
        /// 查找纹理资源
        /// </summary>
        public string GetTexAsset(RequestPageData request)
        {
            foreach (var pair in texAssets)
            {
                if (pair.Key.pageX == request.pageX &&
                    pair.Key.pageY == request.pageY &&
                    pair.Key.mipLevel == request.mipLevel)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 查找纹理资源
        /// </summary>
        public string GetTexAsset(int x, int y, int mipLevel)
        {
            foreach (var pair in texAssets)
            {
                if (pair.Key.pageX == x &&
                    pair.Key.pageY == y &&
                    pair.Key.mipLevel == mipLevel)
                {
                    return pair.Value;
                }
            }

            return null;
        }

        /// <summary>
        /// 设置纹理对应的投影矩阵
        /// </summary>
        public void SetMatrix(RequestPageData request, Vector4 rect)
        {
            texRect.Add(request, rect);
        }

        /// <summary>
        /// 获取纹理对应的投影矩阵
        /// </summary>
        public Vector4 GetMatrix(int x, int y, int mip)
        {
            foreach (var pair in texRect)
            {
                var req = pair.Key;
                if (req.pageX == x && req.pageY == y && req.mipLevel == mip)
                    return pair.Value;
            }

            return new Vector4(0, 0, 1, 1);
        }

        /// <summary>
        /// 设置纹理对应的投影矩阵
        /// </summary>
        public void SetBounds(RequestPageData request, Bounds bounds)
        {
            tileBounds.Add(request, bounds);
        }

        /// <summary>
        /// 获取纹理对应的投影矩阵
        /// </summary>
        public Bounds GetBounds(int x, int y, int mip)
        {
            foreach (var pair in tileBounds)
            {
                var req = pair.Key;
                if (req.pageX == x && req.pageY == y && req.mipLevel == mip)
                    return pair.Value;
            }

            return new Bounds();
        }

#if UNITY_EDITOR
        public void SetupTextureImporter()
        {
            foreach (var it in this.texAssets)
            {
                var textureImporter = TextureImporter.GetAtPath(it.Value) as TextureImporter;
                if (textureImporter != null)
                {
                    textureImporter.textureType = TextureImporterType.Lightmap;
                    textureImporter.textureShape = TextureImporterShape.Texture2D;
                    textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                    textureImporter.alphaSource = TextureImporterAlphaSource.None;
                    textureImporter.sRGBTexture = false;
                    textureImporter.ignorePngGamma = true;
                    textureImporter.mipmapEnabled = false;
                    textureImporter.filterMode = FilterMode.Bilinear;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;

                    textureImporter.SaveAndReimport();
                }
            }
        }

        public void SaveAs(string path, string name = "VirtualLightData.asset")
        {
            foreach (var key in this.texAssets.Keys)
            {
                var assetPath = texAssets[key];
                texAssets[key] = AssetDatabase.AssetPathToGUID(assetPath);
            }

            AssetDatabase.CreateAsset(this, Path.Join(path, name));
            AssetDatabase.SaveAssets();
        }
#endif
    }
}