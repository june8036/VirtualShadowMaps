using System;
using UnityEngine;

namespace VirtualTexture
{
    public sealed class PageLevelTable
    {
        private Page[,] m_Pages;

        private Vector2Int m_PageOffset;

        public Page[,] pages { get => m_Pages; }

        public Vector2Int pageOffset { get => m_PageOffset; }

        public int mipLevel { get; }
        public int nodeCellCount { get; }
        public int perCellSize { get; }

        public PageLevelTable(int pageSize, int mip)
        {
            mipLevel = mip;
            perCellSize = 1 << mipLevel;
            nodeCellCount = pageSize / perCellSize;

            m_PageOffset = Vector2Int.zero;
            m_Pages = new Page[nodeCellCount, nodeCellCount];

            for (int i = 0; i < nodeCellCount; i++)
            {
                for(int j = 0; j < nodeCellCount; j++)
                {
                    m_Pages[i, j] = new Page(i, j, mipLevel);
                }
            }
        }

        public void ResetPageOffset()
        {
            m_PageOffset = Vector2Int.zero;
        }

        public void ChangeViewRect(Vector2Int offset, Action<int> InvalidatePage)
        {
            if (Mathf.Abs(offset.x) >= nodeCellCount || Mathf.Abs(offset.y) > nodeCellCount || offset.x % perCellSize != 0 || offset.y % perCellSize != 0)
            {
                for (int i = 0; i < nodeCellCount; i++)
				{
                    for (int j = 0; j < nodeCellCount; j++)
                    {
                        var transXY = GetTransXY(i, j);
                        ref var page = ref m_Pages[transXY.x, transXY.y];
                        page.payload.loadRequest = null;

                        if (page.payload.isReady)
                        {
                            InvalidatePage(page.payload.tileIndex);
                        }
                    }
                }

                m_PageOffset = Vector2Int.zero;
                return;
            }

            offset.x /= perCellSize;
            offset.y /= perCellSize;

            #region clip map
            if (offset.x > 0)
            {
                for (int i = 0;i < offset.x; i++)
                {
                    for (int j = 0;j < nodeCellCount;j++)
                    {
                        var transXY = GetTransXY(i, j);
                        m_Pages[transXY.x, transXY.y].payload.loadRequest = null;
                        InvalidatePage(m_Pages[transXY.x, transXY.y].payload.tileIndex);
                    }
                }
            }
            else if (offset.x < 0)
            {
                for (int i = 1; i <= -offset.x; i++)
                {
                    for (int j = 0; j < nodeCellCount; j++)
                    {
                        var transXY = GetTransXY(nodeCellCount - i, j);
                        ref var page = ref m_Pages[transXY.x, transXY.y];
                        page.payload.loadRequest = null;
                        
                        if (page.payload.isReady)
						{
                            InvalidatePage(page.payload.tileIndex);
                        }
                    }
                }
            }
            if (offset.y > 0)
            {
                for (int i = 0; i < offset.y; i++)
                {
                    for (int j = 0; j < nodeCellCount; j++)
                    {
                        var transXY = GetTransXY(j, i);
                        m_Pages[transXY.x, transXY.y].payload.loadRequest = null;
                        InvalidatePage(m_Pages[transXY.x, transXY.y].payload.tileIndex);
                    }
                }
            }
            else if (offset.y < 0)
            {
                for (int i = 1; i <= -offset.y; i++)
                {
                    for (int j = 0; j < nodeCellCount; j++)
                    {
                        var transXY = GetTransXY(j, nodeCellCount - i);
                        m_Pages[transXY.x, transXY.y].payload.loadRequest = null;
                        InvalidatePage(m_Pages[transXY.x, transXY.y].payload.tileIndex);
                    }
                }
            }
            #endregion

            m_PageOffset += offset;
            
            while(m_PageOffset.x < 0) m_PageOffset.x += nodeCellCount;
            while (m_PageOffset.y < 0) m_PageOffset.y += nodeCellCount;

            m_PageOffset.x %= nodeCellCount;
            m_PageOffset.y %= nodeCellCount;
        }

        // 取x/y/mip完全一致的node，没有就返回null
        public Page Get(int x, int y)
        {
            if (x < 0 || y < 0 || x >= nodeCellCount || y >= nodeCellCount)
                return null;

            return m_Pages[x, y];
        }

        public Page Nearest(int x, int y)
        {
            if (x < 0 || y < 0 || x >= nodeCellCount || y >= nodeCellCount)
            {
                x /= perCellSize;
                y /= perCellSize;

                x = (x + m_PageOffset.x) % nodeCellCount;
                y = (y + m_PageOffset.y) % nodeCellCount;
            }

            return Get(x, y);
        }        

        public RectInt GetInverRect(RectInt rect)
        {
            return new RectInt( rect.xMin - m_PageOffset.x,
                                rect.yMin - m_PageOffset.y,
                                rect.width,
                                rect.height);
        }

        private Vector2Int GetTransXY(int x, int y)
        {
            return new Vector2Int((x + m_PageOffset.x) % nodeCellCount,
                                  (y + m_PageOffset.y) % nodeCellCount);
        }
    }
}