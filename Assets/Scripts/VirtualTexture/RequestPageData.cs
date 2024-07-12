using System;
using System.Collections.Generic;

namespace VirtualTexture
{
	/// <summary>
	/// 渲染请求类.
	/// </summary>
	[Serializable]
	public struct RequestPageData : IEqualityComparer<RequestPageData>
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

        public bool Equals(RequestPageData lhs, RequestPageData rhs)
        {
			return lhs.pageX == rhs.pageX && lhs.pageY == rhs.pageY && lhs.mipLevel == rhs.mipLevel;
        }

        public int GetHashCode(RequestPageData obj)
        {
            int hashCode = 17;
            hashCode = hashCode * 23 + EqualityComparer<int>.Default.GetHashCode(this.pageX);
            hashCode = hashCode * 23 + EqualityComparer<int>.Default.GetHashCode(this.pageY);
            hashCode = hashCode * 23 + EqualityComparer<int>.Default.GetHashCode(this.mipLevel);
			return hashCode;
        }
    }
}