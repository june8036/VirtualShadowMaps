using System;
using System.Collections.Generic;
using System.Linq;
using UnityEngine.Pool;

namespace VirtualTexture
{
    public sealed class RequestPageDataJob
    {
        /// <summary>
        /// 请求数量.
        /// </summary>
        public int requestCount { get => m_PendingRequests.Count; }

        /// <summary>
        /// 请求池.
        /// </summary>
        private ObjectPool<RequestPageData> m_PendingRequestPool = new ObjectPool<RequestPageData>(() => { return new RequestPageData(); });

        /// <summary>
        /// 等待处理的请求.
        /// </summary>
        private List<RequestPageData> m_PendingRequests = new List<RequestPageData>();

        /// <summary>
        /// 获取第一个加载请求
        /// </summary>
        public RequestPageData First()
        {
            return m_PendingRequests.Count > 0 ? m_PendingRequests.First() : null;
        }

        /// <summary>
        /// 搜索页面请求
        /// </summary>
        public RequestPageData Find(int x, int y, int mip)
        {
            foreach (var req in m_PendingRequests)
            {
                if (req.pageX == x && req.pageY == y && req.mipLevel == mip)
                    return req;
            }

            return null;
        }

        /// <summary>
        /// 移除页面请求
        /// </summary>
        public void Remove(RequestPageData req)
        {
            if (m_PendingRequests.Count == 0 && !m_PendingRequests.Contains(req))
            {
                throw new InvalidOperationException("Trying to release an object that has already been released to the pool.");
            }

            m_PendingRequestPool.Release(req);
            m_PendingRequests.Remove(req);
        }

        /// <summary>
        /// 新建页面请求
        /// </summary>
        public RequestPageData Request(int x, int y, int mip)
        {
            // 是否已经在请求队列中
            if (this.Find(x, y, mip) == null)
			{
                // 加入待处理列表
                var request = m_PendingRequestPool.Get();
                request.pageX = x;
                request.pageY = y;
                request.mipLevel = mip;

                m_PendingRequests.Add(request);

                return request;
            }

            return null;
        }

        /// <summary>
        /// 用于优先加载最大的mip
        /// </summary>
        public void Sort()
        {
            if (m_PendingRequests.Count > 0)
                m_PendingRequests.Sort((x, y) => { return y.mipLevel.CompareTo(x.mipLevel); });
        }

        /// <summary>
        /// 自定义排序
        /// </summary>
        public void Sort(Comparison<RequestPageData> comparison)
        {
            if (m_PendingRequests.Count > 0)
                m_PendingRequests.Sort(comparison);
        }

        /// <summary>
        /// 清除所有的页面请求
        /// </summary>
        public void Clear()
        {
            m_PendingRequests.Clear();
            m_PendingRequestPool.Clear();
        }

        /// <summary>
        /// 清除所有的页面请求
        /// </summary>
        public void Clear(Action<RequestPageData> cancelRequestPageJob)
        {
            if (cancelRequestPageJob != null)
			{
                foreach (var req in m_PendingRequests)
                    cancelRequestPageJob?.Invoke(req);
            }

            m_PendingRequests.Clear();
            m_PendingRequestPool.Clear();
        }
    }
}