namespace VirtualTexture
{
	public enum ScaleFactor
	{
		One,
        Half,
        Quarter,
        Eighth,
	}

	public static class ScaleModeExtensions
	{
		public static float ToFloat(this ScaleFactor mode)
		{
			switch(mode)
			{
			case ScaleFactor.Eighth:
				return 0.125f;
			case ScaleFactor.Quarter:
				return 0.25f;
			case ScaleFactor.Half:
				return 0.5f;
			}
			return 1;
		}
	}
}