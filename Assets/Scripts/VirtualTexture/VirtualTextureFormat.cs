using UnityEngine;

namespace VirtualTexture
{
	public struct VirtualTextureFormat
	{
		public RenderTextureFormat format;
		public RenderTextureReadWrite readWrite;
        public FilterMode filterMode;

        public VirtualTextureFormat(RenderTextureFormat format, FilterMode filterMode = FilterMode.Bilinear, RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear)
		{
			this.format = format;
			this.filterMode = filterMode;
            this.readWrite = readWrite;
        }
    }
}