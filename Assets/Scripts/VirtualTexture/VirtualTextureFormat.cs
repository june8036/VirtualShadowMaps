using UnityEngine;

namespace VirtualTexture
{
	public struct VirtualTextureFormat
	{
		public RenderTextureFormat format;
		public RenderTextureReadWrite readWrite;

        public VirtualTextureFormat(RenderTextureFormat format, RenderTextureReadWrite readWrite = RenderTextureReadWrite.Linear)
		{
			this.format = format;
            this.readWrite = readWrite;
        }
    }
}