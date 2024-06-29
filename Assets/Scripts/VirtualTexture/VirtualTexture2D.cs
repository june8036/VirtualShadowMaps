using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class VirtualTexture2D : IDisposable
    {
        /// <summary>
        /// RT Job对象
        /// </summary>
        private RequestPageDataJob m_RequestPageJob;

        /// <summary>
        /// 页表
        /// </summary>
        private PageTable m_PageTable;

        /// <summary>
        /// 平铺贴图对象
        /// </summary>
        private TiledTexture m_TileTexture;

        /// <summary>
        /// 导出的页表寻址贴图
        /// </summary>
        private RenderTexture m_LookupTexture;

        /// <summary>
        /// 缓存历史帧计数
        /// </summary>
        private int m_FrameCount = 0;

        /// <summary>
        /// 当前帧激活的Page列表
        /// </summary>
        private List<Page> m_Pages = new List<Page>();

        /// <summary>
        /// 当前帧激活的Tile列表
        /// </summary>
        private List<Page> m_ActiveTiles = new List<Page>();

        /// <summary>
        /// 当前帧激活的Tiled索引
        /// </summary>
        private Vector4[] m_TiledIndex;

        /// <summary>
        /// 当前帧激活的Tiled矩阵
        /// </summary>
        private Matrix4x4[] m_TiledMatrixs;

        /// <summary>
        /// 单个Tile的尺寸.
        /// </summary>
        public int tileSize { get { return m_TileTexture.tileSize; } }

        /// <summary>
        /// 区域尺寸.
        /// 区域尺寸表示横竖两个方向上Tile的数量.
        /// </summary>
        public int tilingCount { get { return m_TileTexture.tilingCount; } }
        
        /// <summary>
        /// Tile 纹理的宽度.
        /// </summary>
        public int textireSize { get { return m_TileTexture.textireSize; } }

        /// <summary>
        /// 页表大小.
        /// </summary>
        public PageTable pageTable { get { return m_PageTable; } }

        /// <summary>
        /// 页表大小.
        /// </summary>
        public int pageSize { get { return m_PageTable.pageSize; } }

        /// <summary>
        /// 页表
        /// </summary>
        public int maxPageLevel { get { return m_PageTable.maxMipLevel; } }

        /// <summary>
        /// 判断是否需要重绘
        /// </summary>
        public bool isTiledDirty { get { return m_Pages.Count > 0; } }

        /// <summary>
        /// 当前激活的Tiled数量
        /// </summary>
        public int tiledCount { get { return m_ActiveTiles.Count; } }

        /// <summary>
        /// 当前激活的Tiled索引
        /// </summary>
        public Vector4[] tiledIndex { get { return m_TiledIndex; } }

        /// <summary>
        /// 当前激活的Tiled矩阵
        /// </summary>
        public Matrix4x4[] tiledMatrixs { get { return m_TiledMatrixs; } }

        private struct DrawPageInfo
        {
            public Rect rect;
            public int x;
            public int y;
            public int mip;
        }

        public VirtualTexture2D(int tileSize, int tilingCount, VirtualTextureFormat[] formats, int pageSize, int maxLevel)
        {
            m_RequestPageJob = new RequestPageDataJob();

            m_PageTable = new PageTable(pageSize, maxLevel);
            m_TileTexture = new TiledTexture(tileSize, tilingCount, formats);

            m_TiledIndex = new Vector4[tilingCount * tilingCount];
            m_TiledMatrixs = new Matrix4x4[tilingCount * tilingCount];

            m_LookupTexture = new RenderTexture(pageSize, pageSize, 16, RenderTextureFormat.ARGBHalf);
            m_LookupTexture.name = "LookupTexture";
            m_LookupTexture.filterMode = FilterMode.Point;
            m_LookupTexture.wrapMode = TextureWrapMode.Clamp;
        }

        public RenderTexture GetTexture(int index)
        {
            return m_TileTexture.GetTexture(index);
        }

        public RenderTexture GetLookupTexture()
        {
            return m_LookupTexture;
        }

        public int RequestTile()
        {
            return m_TileTexture.RequestTile();
        }

        public Matrix4x4 GetMatrix(int tile)
        {
            return m_TileTexture.GetMatrix(tile);
        }

        /// <summary>
        /// 获取页表
        /// </summary>
        public Page GetPage(int x, int y, int mip)
        {
            return m_PageTable.GetPage(x, y, mip);
        }

        /// <summary>
        /// 加载页表
        /// </summary>
        public Page LoadPage(int x, int y, int mip)
        {
            var page = m_PageTable.FindPage(x, y, mip);
            if (page != null)
            {
                if (!page.payload.isReady)
                {
                    if (page.payload.loadRequest == null)
                        page.payload.loadRequest = m_RequestPageJob.Request(x, y, page.mipLevel);
                }
                else
                {
                    m_TileTexture.SetActive(page.payload.tileIndex);
                }

                return page;
            }

            return null;
        }

        /// <summary>
        /// 加载页表
        /// </summary>
        public void LoadPages(Color32[] pageData)
        {
            foreach (var data in pageData)
            {
                if (data.a == 0)
                    continue;

                LoadPage(data.r, data.g, data.b);
            }
        }

        /// <summary>
        /// 加载LOD下的所有页表
        /// </summary>
        public void LoadPageByLevel(int mip)
        {
            if (mip <= maxPageLevel)
            {
                var cellSize = 1 << mip;
                var cellCount = pageSize / cellSize;

                for (int i = 0; i < cellCount; i++)
                {
                    for (int j = 0; j < cellCount; j++)
                    {
                        this.LoadPage(i, j, mip);
                    }
                }
            }
        }

        /// <summary>
        /// 激活页表
        /// </summary>
        public void ActivatePage(int tile, Page page)
        {
            if (m_TileTexture.SetActive(tile))
            {
                if (page.payload.loadRequest != null)
                {
                    m_RequestPageJob.Remove(page.payload.loadRequest.Value);
                    page.payload.loadRequest = null;
                }

                m_PageTable.ActivatePage(tile, page);
            }
        }

        /// <summary>
        /// 移动页表
        /// </summary>
        public void MovePageTable(Vector2Int offset)
        {
            this.ClearRequest();

            m_PageTable.ChangeViewRect(offset);
        }

        public int GetRequestCount()
        {
            return m_RequestPageJob.requestCount;
        }

        public RequestPageData? FirstRequest()
        {
            return m_RequestPageJob.First();
        }

        public void SortRequest()
        {
            m_RequestPageJob.Sort();
        }

        public void SortRequest(Comparison<RequestPageData> comparison)
        {
            m_RequestPageJob.Sort(comparison);
        }

        public void RemoveRequest(RequestPageData req)
        {
            var page = m_PageTable.GetPage(req.pageX, req.pageY, req.mipLevel);
            if (page != null)
            {
                if (page.payload.loadRequest.Equals(req))
                {
                    m_RequestPageJob.Remove(req);
                    page.payload.loadRequest = null;
                }
            }
        }

        public void ClearRequest()
        {
            Debug.Assert(m_RequestPageJob != null);

            m_RequestPageJob.Clear((req) => 
            {
                var page = m_PageTable.GetPage(req.pageX, req.pageY, req.mipLevel);
                if (page != null)
                {
                    if (page.payload.loadRequest.Equals(req))
                        page.payload.loadRequest = null;
                }
            });
        }

        public bool UpdateLookup()
        {
            m_Pages.Clear();

            foreach (var kv in m_PageTable.activePages)
            {
                var page = kv.Value;
                if (page.payload.activeFrame < m_FrameCount)
                    continue;

                m_Pages.Add(page);
            }

            if (m_Pages.Count > 0)
            {
                m_ActiveTiles.Clear();

                foreach (var kv in m_PageTable.activePages)
                    m_ActiveTiles.Add(kv.Value);

                m_ActiveTiles.Sort((a, b) => { return -a.mipLevel.CompareTo(b.mipLevel); });

                for (int i = 0; i < m_ActiveTiles.Count; i++)
                {
                    var page = m_ActiveTiles[i];
                    var rect = page.GetRect();
                    var table = m_PageTable.pageLevelTable[page.mipLevel];
                    var offset = table.pageOffset * table.perCellSize;
                    var lb = rect.position - offset;

                    while (lb.x < 0) lb.x += pageSize;
                    while (lb.y < 0) lb.y += pageSize;

                    var tileRect = new Rect(lb.x, lb.y, rect.width, rect.height);
                    var tileIndex = m_TileTexture.IdToPos(page.payload.tileIndex);

                    var size = tileRect.width / pageSize;
                    var position = new Vector3(tileRect.x / pageSize, tileRect.y / pageSize);

                    m_TiledIndex[i] = new Vector4(tileIndex.x, tileIndex.y, page.mipLevel, 1 << page.mipLevel);
                    m_TiledMatrixs[i] = Matrix4x4.TRS(position, Quaternion.identity, new Vector3(size, size, size));
                }
            }

            m_FrameCount = Time.frameCount;

            return m_Pages.Count > 0;
        }

        public void Clear()
        {
            m_PageTable?.InvalidatePages();
            m_RequestPageJob?.Clear();
            m_TileTexture?.Clear();
        }

        public void Dispose()
        {
            m_TileTexture?.Dispose();
            m_TileTexture = null;

            m_LookupTexture?.Release();
            m_LookupTexture = null;

            m_RequestPageJob?.Clear();
            m_PageTable?.InvalidatePages();
        }
    }
}