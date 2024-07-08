using System;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualTexture
{
    public sealed class TiledTexture : IDisposable
    {
        /// <summary>
        /// 单个Tile的尺寸.
        /// </summary>
        private int m_TileSize = 512;

        /// <summary>
        /// 平铺次数.
        /// </summary>
        private int m_TilingCount = 8;

        /// <summary>
        /// Tile缓存池.
        /// </summary>
        private LruCache m_TilePool;

        /// <summary>
        /// Tile纹理格式.
        /// </summary>
        private VirtualTextureFormat[] m_TileFormat;

        /// <summary>
        /// Tile Target
        /// </summary>
        private RenderTexture[] m_TileTextures;

        /// <summary>
        /// 区域尺寸.
        /// 区域尺寸表示横竖两个方向上Tile的数量.
        /// </summary>
        public int tilingCount { get { return m_TilingCount; } }

        /// <summary>
        /// 单个Tile的尺寸.
        /// Tile是宽高相等的正方形.
        /// </summary>
        public int tileSize { get { return m_TileSize; } }

        /// <summary>
        /// Tile 纹理的宽度.
        /// </summary>
        public int textireSize { get { return m_TilingCount * m_TileSize; } }

        public TiledTexture(int size, int count, VirtualTextureFormat[] format)
        {
            m_TileSize = size;
            m_TilingCount = count;
            m_TileFormat = format;
            m_TilePool = new LruCache(m_TilingCount * m_TilingCount);

            m_TileTextures = new RenderTexture[m_TileFormat.Length];

            for (int i = 0; i < m_TileFormat.Length; i++)
            {
                var texture = RenderTexture.GetTemporary(this.textireSize, this.textireSize, 16, m_TileFormat[i].format, m_TileFormat[i].readWrite);
                texture.name = "TileTexture" + i;
                texture.useMipMap = false;
                texture.autoGenerateMips = false;
                texture.filterMode = FilterMode.Bilinear;
                texture.wrapMode = TextureWrapMode.Clamp;

                m_TileTextures[i] = texture;
            }
        }

        ~TiledTexture()
        {
            this.Dispose();
        }

        public Vector2Int IdToPos(int id)
        {
            return new Vector2Int(id % tilingCount, id / tilingCount);
        }

        public int PosToId(Vector2Int tile)
        {
            return tile.y * tilingCount + tile.x;
        }

        public int RequestTile()
        {
            return m_TilePool.first;
        }

        public bool SetActive(int tile)
        {
            return m_TilePool.SetActive(tile);
        }

        public RectInt TileToRect(Vector2Int tile)
		{
            var size = m_TileSize;
            return new RectInt(tile.x * size, tile.y * size, size, size);
		}

        public RenderTexture GetTexture(int index)
        {
            return m_TileTextures[index];
        }

        public Matrix4x4 GetMatrix(int id)
        {
            return GetMatrix(IdToPos(id));
        }

        public Matrix4x4 GetMatrix(Vector2Int tile)
		{
            var tileRect = TileToRect(tile);

            var tileX = tileRect.x / (float)textireSize * 2 - 1;
            var tileY = 1 - (tileRect.y + tileRect.height) / (float)textireSize * 2;
            var tileWidth = tileRect.width * 2 / (float)textireSize;
            var tileHeight = tileRect.height * 2 / (float)textireSize;

            if (SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLCore ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES2 ||
                SystemInfo.graphicsDeviceType == GraphicsDeviceType.OpenGLES3)
            {
                tileY = tileRect.y / (float)textireSize * 2 - 1;
            }

            return Matrix4x4.TRS(new Vector3(tileX, tileY, 0), Quaternion.identity, new Vector3(tileWidth, tileHeight, 0));
        }

        public void Clear()
		{
            m_TilePool.Clear();
        }

        public void Dispose()
        {
            for (int i = 0; i < m_TileTextures.Length; i++)
            {
                if (m_TileTextures[i] != null)
                {
                    RenderTexture.ReleaseTemporary(m_TileTextures[i]);
                    m_TileTextures[i] = null;
                }
            }
        }
    }
}