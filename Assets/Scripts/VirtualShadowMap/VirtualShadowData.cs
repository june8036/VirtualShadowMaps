using System.IO;
using UnityEngine;
using System.Collections.Generic;

#if UNITY_EDITOR
using UnityEditor;
#endif

namespace VirtualTexture
{
    public sealed class VirtualShadowData : ScriptableObject
    {
        public static int s_SplitBlockSize = 64;

        /// <summary>
        /// 覆盖区域中心.
        /// </summary>
        public Vector3 regionCenter = Vector3.zero;

        /// <summary>
        /// 覆盖区域大小.
        /// </summary>
        public int regionSize = 1024;

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
        /// 世界包围体.
        /// </summary>
        [SerializeField]
        public Bounds bounds;

        /// <summary>
        /// 阴影投影矩阵列表.
        /// </summary>
        [SerializeField]
        public SerializableDictionary<RequestPageData, Matrix4x4> lightProjections = new SerializableDictionary<RequestPageData, Matrix4x4>();

        /// <summary>
        /// 纹理资源列表.
        /// </summary>
        [SerializeField]
        public SerializableDictionary<RequestPageData, string> texAssets = new SerializableDictionary<RequestPageData, string>();

        /// <summary>
        /// 资源数量.
        /// </summary>
        public int textureCount { get => texAssets.Count; }

        /// <summary>
        /// 页表对应的世界区域.
        /// </summary>
        public Rect regionRange
        {
            get
            {
                return new Rect(-regionSize / 2, -regionSize / 2, regionSize, regionSize);
            }
        }

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
        /// 设置纹理对应的投影矩阵
        /// </summary>
        public void SetMatrix(RequestPageData request, Matrix4x4 matrix)
        {
            lightProjections.Add(request, matrix);
        }

        /// <summary>
        /// 获取纹理对应的投影矩阵
        /// </summary>
        public Matrix4x4 GetMatrix(int x, int y, int mip)
        {
            foreach (var pair in lightProjections)
            {
                var req = pair.Key;
                if (req.pageX == x && req.pageY == y && req.mipLevel == mip)
                    return pair.Value;
            }

            return Matrix4x4.identity;
        }

        public KeyValuePair<Texture2D, int[]> Compress(Texture2D source)
        {
            var blockSize = s_SplitBlockSize;
            var blockNum = source.width / blockSize;

            var indices = new int[blockNum * blockNum];
            var colors = source.GetPixels();

            int tileIndex = 0;

            for (int i = 0; i < source.height / blockSize; i++)
            {
                for (int j = 0; j < source.width / blockSize; j++)
                {
                    bool isSkip = canSkipTile(colors, j, i, blockSize, source.width);
                    indices[j + i * blockNum] = isSkip ? -1 : tileIndex;
                    if (isSkip == false) tileIndex++;
                }
            }

            var atlasC = Mathf.ClosestPowerOfTwo(Mathf.CeilToInt(Mathf.Sqrt(tileIndex)) * blockSize) / blockSize;
            var mainTexture = new Texture2D(blockSize * atlasC, blockSize * atlasC, source.format, false, true);

            for (int i = 0; i < source.height / blockSize; i++)
            {
                for (int j = 0; j < source.width / blockSize; j++)
                {
                    if (indices[j + i * blockNum] == -1) continue;
                    int x = indices[j + i * blockNum] % atlasC;
                    int y = indices[j + i * blockNum] / atlasC;

                    Graphics.CopyTexture(source, 0, 0, j * blockSize, i * blockSize, blockSize, blockSize, mainTexture, 0, 0, x * blockSize, y * blockSize);
                }
            }

            return new KeyValuePair<Texture2D, int[]>(mainTexture, indices);
        }

        public Texture2D Uncompress(Texture2D mainTexture, int[] indics)
        {
            var resolution = this.maxResolution.ToInt();
            var blockSize = s_SplitBlockSize;
            var blockNum = resolution / blockSize;
            
            var atlasC = mainTexture.width / blockSize;
            var sourceTex = new Texture2D(resolution, resolution, mainTexture.format, false, true);

            for (int i = 0; i < resolution / blockSize; i++)
            {
                for (int j = 0; j < resolution / blockSize; j++)
                {
                    if (indics[j + i * blockNum] == -1)
                        continue;

                    int x = indics[j + i * blockNum] % atlasC;
                    int y = indics[j + i * blockNum] / atlasC;

                    Graphics.CopyTexture(mainTexture, 0, 0, x * blockSize, y * blockSize, blockSize, blockSize, sourceTex, 0, 0, j * blockSize, i * blockSize);
                }
            }

            return sourceTex;
        }

        private bool canSkipTile(Color[] colors, int x, int y, int count, int texWidth)
        {
            int emptyCount = 0;

            for (int i = 0; i < count; i++)
            {
                for (int j = 0; j < count; j++)
                {
                    if (colors[(y * count + i) * texWidth + x * count + j].r == 0) emptyCount++;
                }
            }

            return emptyCount * 100.0f / count / count > 99;
        }

#if UNITY_EDITOR
        public void SetupTextureImporter()
        {
            foreach (var it in this.texAssets)
            {
                var textureImporter = TextureImporter.GetAtPath(it.Value) as TextureImporter;
                if (textureImporter)
                {
                    textureImporter.textureType = TextureImporterType.SingleChannel;
                    textureImporter.textureShape = TextureImporterShape.Texture2D;
                    textureImporter.textureCompression = TextureImporterCompression.CompressedHQ;
                    textureImporter.alphaSource = TextureImporterAlphaSource.None;
                    textureImporter.sRGBTexture = false;
                    textureImporter.ignorePngGamma = true;
                    textureImporter.mipmapEnabled = false;
                    textureImporter.filterMode = FilterMode.Point;
                    textureImporter.wrapMode = TextureWrapMode.Clamp;

                    var defaultSettings = textureImporter.GetDefaultPlatformTextureSettings();
                    defaultSettings.format = TextureImporterFormat.R16;
                    defaultSettings.textureCompression = TextureImporterCompression.CompressedHQ;

                    textureImporter.SetPlatformTextureSettings(defaultSettings);

                    textureImporter.SaveAndReimport();
                }
            }
        }

        public void SaveAs(string path, string name = "VirtualShadowData.asset")
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