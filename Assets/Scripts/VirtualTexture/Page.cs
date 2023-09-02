using UnityEngine;

namespace VirtualTexture
{
    public sealed class Page
    {
        public int x { get; }

        public int y { get; }

        public int mipLevel { get; }

        public int size { get { return 1 << mipLevel; } }

        public PagePayload payload { get; }

        public Page(int x, int y, int mip)
        {
            this.x = x;
            this.y = y;
            this.mipLevel = mip;
            this.payload = new PagePayload();
        }

        public RectInt GetRect()
        {
            var cellSize = 1 << mipLevel;
            return new RectInt(x * cellSize, y * cellSize, cellSize, cellSize);
        }
    }
}