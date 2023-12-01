namespace VirtualTexture
{
    public enum ShadowResolution
    {
        _256 = 256,
        _512 = 512,
        _1024 = 1024,
        _2048 = 2048,
        _4096 = 4096
    }

    public static class ShadowResolutionExtensions
    {
        public static int ToInt(this ShadowResolution mode)
        {
            switch (mode)
            {
                case ShadowResolution._256:
                    return 256;
                case ShadowResolution._512:
                    return 512;
                case ShadowResolution._1024:
                    return 1024;
                case ShadowResolution._2048:
                    return 2048;
                case ShadowResolution._4096:
                    return 2096;
            }
            return 1;
        }
    }
}