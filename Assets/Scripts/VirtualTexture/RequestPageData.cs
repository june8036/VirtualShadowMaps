using System;

namespace VirtualTexture
{
    /// <summary>
    /// 渲染请求类.
    /// </summary>
    [Serializable]
    public struct RequestPageData : IEquatable<RequestPageData>
    {
        /// <summary>
        /// 页表X坐标
        /// </summary>
        public int pageX;

        /// <summary>
        /// 页表Y坐标
        /// </summary>
        public int pageY;

        /// <summary>
        /// mipmap等级
        /// </summary>
        public int mipLevel;

        /// <summary>
        /// 页表大小
        /// </summary>
        public int size { get { return 1 << mipLevel; } }

        /// <summary>
        /// 构造函数
        /// </summary>
        public RequestPageData(int x, int y, int mip)
        {
            pageX = x;
            pageY = y;
            mipLevel = mip;
        }

        public override int GetHashCode()
        {
            return (pageY * short.MaxValue + pageX) * (1 + mipLevel);
        }

        public bool Equals(RequestPageData other)
        {
            return this.pageX == other.pageX && this.pageY == other.pageY && this.mipLevel == other.mipLevel;
        }
    }
}