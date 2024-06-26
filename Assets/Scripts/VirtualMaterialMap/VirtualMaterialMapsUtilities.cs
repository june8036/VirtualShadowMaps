using System.IO;
using UnityEngine;
using UnityEngine.Rendering;

namespace VirtualTexture
{
    public static class VirtualMaterialMapsUtilities
    {
		public static Texture2D BoxFilter(Texture2D texture2D)
		{
			var w = texture2D.width;
			var h = texture2D.height;
			var texture = new Texture2D(w, h, texture2D.format, false);

            for (int y = 0; y < texture2D.height; y++)
			{
				for (int x = 0; x < texture2D.width; x++)
				{
					Color color = Color.clear;

					int n = 0;
					for (int dy = -1; dy <= 1; dy++)
					{
						int cy = y + dy;
						for (int dx = -1; dx <= 1; dx++)
						{
							int cx = x + dx;
							if (cx >= 0 && cx < w && cy >= 0 && cy < h)
							{
								var cur = texture2D.GetPixel(cx, cy);
								if (cur != Color.clear)
								{
									color += cur;
									n++;
								}
							}
						}
					}

                    texture.SetPixel(x, y, color / n);
				}
			}

			return texture;
        }

        public static Matrix4x4 GetTextureScaleMatrix()
        {
            var textureScaleAndBias = Matrix4x4.identity;
            textureScaleAndBias.m00 =  2.0f;
            textureScaleAndBias.m11 =  2.0f;
            textureScaleAndBias.m22 =  0.0f;
            textureScaleAndBias.m03 = -1.0f;
            textureScaleAndBias.m13 = -1.0f;
            textureScaleAndBias.m23 =  0.0f;

            return textureScaleAndBias;
        }
    }
}