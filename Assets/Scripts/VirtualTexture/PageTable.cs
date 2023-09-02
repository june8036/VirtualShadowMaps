using System;
using System.Collections.Generic;
using UnityEngine;

namespace VirtualTexture
{
    /// <summary>
    /// 页表
    /// </summary>
    public sealed class PageTable
    {
        /// <summary>
        /// 页表尺寸.
        /// </summary>
        private int m_PageSize;

        /// <summary>
        /// 页表尺寸.
        /// </summary>
        private int m_MaxMipLevel;

        /// <summary>
        /// 页表层级结构
        /// </summary>
        private PageLevelTable[] m_PageLevelTable;

        /// <summary>
        /// 当前活跃的页表
        /// </summary>
        private Dictionary<int, Page> m_ActivePages = new Dictionary<int, Page>();

        /// <summary>
        /// 页表尺寸.
        /// </summary>
        public int pageSize { get => m_PageSize; }

        /// <summary>
        /// 最大mipmap等级
        /// </summary>
        public int maxMipLevel { get => m_MaxMipLevel; }

        /// <summary>
        /// 页表层级结构
        /// </summary>
        public PageLevelTable[] pageLevelTable { get => m_PageLevelTable; }

        /// <summary>
        /// 当前活跃的页表
        /// </summary>
        public Dictionary<int, Page> activePages { get => m_ActivePages; }

        public PageTable(int pageSize = 256, int maxLevel = 8)
        {
            m_PageSize = pageSize;
            m_MaxMipLevel = Math.Min((int)Mathf.Log(pageSize, 2), maxLevel);

            m_PageLevelTable = new PageLevelTable[m_MaxMipLevel + 1];

            for (int i = 0; i <= m_MaxMipLevel; i++)
                m_PageLevelTable[i] = new PageLevelTable(pageSize, i);
        }

        public Page GetPage(int x, int y, int mip)
        {
            Debug.Assert(x >= 0 && y >= 0 && mip >= 0);
            Debug.Assert(x < pageSize && y < pageSize && mip <= m_MaxMipLevel);

            return m_PageLevelTable[mip].Get(x, y);
        }

        public Page GetNearestPage(int x, int y, int mip)
        {
            Debug.Assert(x >= 0 && y >= 0 && mip >= 0);
            Debug.Assert(x < pageSize && y < pageSize && mip <= m_MaxMipLevel);

            return m_PageLevelTable[mip].Nearest(x, y);
        }

        public Page FindPage(int x, int y, int mip)
		{
            if (mip > m_MaxMipLevel || mip < 0 || x < 0 || y < 0 || x >= pageSize || y >= pageSize)
                return null;

            return m_PageLevelTable[mip].Get(x, y);
        }

        public void ChangeViewRect(Vector2Int offset)
        {
            for (int i = 0; i <= m_MaxMipLevel; i++)
                m_PageLevelTable[i].ChangeViewRect(offset, InvalidatePage);

            foreach (var kv in m_ActivePages)
                m_ActivePages[kv.Key].payload.activeFrame = Time.frameCount;
        }

        /// <summary>
        /// 将页表置为活跃状态
        /// </summary>
        public void ActivatePage(int tile, Page page)
        {
            if (m_ActivePages.TryGetValue(tile, out var node))
            {
                if (node != page)
                {
                    node.payload.ResetTileIndex();
                    m_ActivePages.Remove(tile);
                }
            }

            page.payload.tileIndex = tile;
            page.payload.loadRequest = null;
            page.payload.activeFrame = Time.frameCount;

            m_ActivePages[tile] = page;
        }

        /// <summary>
        /// 将页表置为非活跃状态
        /// </summary>
        public void InvalidatePage(int tile)
        {
            if (m_ActivePages.TryGetValue(tile, out var node))
            {
                node.payload.ResetTileIndex();
                m_ActivePages.Remove(tile);
            }
        }

        public void InvalidatePages()
        {
            foreach (var it  in m_ActivePages)
                it.Value.payload.ResetTileIndex();

            m_ActivePages?.Clear();
        }

        public void ResetPageOffset()
        {
            for (int i = 0; i <= m_MaxMipLevel; i++)
                m_PageLevelTable[i].ResetPageOffset();
        }
    }
}