namespace VirtualTexture
{
    /// <summary>
    /// 页表数据
    /// </summary>
    public sealed class PagePayload
    {
        private static int s_InvalidTileIndex = -1;

        /// <summary>
        /// 对应平铺贴图中的id
        /// </summary>
		public int tileIndex = s_InvalidTileIndex;

        /// <summary>
        /// 激活的帧序号
        /// </summary>
        public int activeFrame;

        /// <summary>
        /// 渲染请求
        /// </summary>
		public RequestPageData loadRequest;

        /// <summary>
        /// 是否处于可用状态
        /// </summary>
		public bool isReady { get { return tileIndex != s_InvalidTileIndex; } }

        /// <summary>
        /// 重置页表数据
        /// </summary>
        public void ResetTileIndex()
        {
            tileIndex = s_InvalidTileIndex;
        }
    }
}