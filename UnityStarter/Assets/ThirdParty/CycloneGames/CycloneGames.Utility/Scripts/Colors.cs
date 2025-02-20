using UnityEngine;
using System.Collections.Generic;

namespace CycloneGames.Utility.Runtime
{
	/// <summary>
	/// Color helpers
	/// </summary>
	public static class Colors
	{
		// via https://gist.github.com/LotteMakesStuff/f7ce43f11e545a151b95b5e87f76304c
		// NOTE: The follwing color names come from the CSS3 specification, Section 4.3 Extended Color Keywords
		// http://www.w3.org/TR/css3-color/#svg-color

		public static readonly Color AliceBlue = new Color32(240, 248, 255, 255);
		public static readonly Color AntiqueWhite = new Color32(250, 235, 215, 255);
		public static readonly Color Aqua = new Color32(0, 255, 255, 255);
		public static readonly Color Aquamarine = new Color32(127, 255, 212, 255);
		public static readonly Color Azure = new Color32(240, 255, 255, 255);
		public static readonly Color Beige = new Color32(245, 245, 220, 255);
		public static readonly Color Bisque = new Color32(255, 228, 196, 255);
		public static readonly Color Black = new Color32(0, 0, 0, 255);
		public static readonly Color BlanchedAlmond = new Color32(255, 235, 205, 255);
		public static readonly Color Blue = new Color32(0, 0, 255, 255);
		public static readonly Color BlueViolet = new Color32(138, 43, 226, 255);
		public static readonly Color Brown = new Color32(165, 42, 42, 255);
		public static readonly Color Burlywood = new Color32(222, 184, 135, 255);
		public static readonly Color CadetBlue = new Color32(95, 158, 160, 255);
		public static readonly Color Chartreuse = new Color32(127, 255, 0, 255);
		public static readonly Color Chocolate = new Color32(210, 105, 30, 255);
		public static readonly Color Coral = new Color32(255, 127, 80, 255);
		public static readonly Color CornflowerBlue = new Color32(100, 149, 237, 255);
		public static readonly Color Cornsilk = new Color32(255, 248, 220, 255);
		public static readonly Color Crimson = new Color32(220, 20, 60, 255);
		public static readonly Color Cyan = new Color32(0, 255, 255, 255);
		public static readonly Color DarkBlue = new Color32(0, 0, 139, 255);
		public static readonly Color DarkCyan = new Color32(0, 139, 139, 255);
		public static readonly Color DarkGoldenrod = new Color32(184, 134, 11, 255);
		public static readonly Color DarkGray = new Color32(169, 169, 169, 255);
		public static readonly Color DarkGreen = new Color32(0, 100, 0, 255);
		public static readonly Color DarkKhaki = new Color32(189, 183, 107, 255);
		public static readonly Color DarkMagenta = new Color32(139, 0, 139, 255);
		public static readonly Color DarkOliveGreen = new Color32(85, 107, 47, 255);
		public static readonly Color DarkOrange = new Color32(255, 140, 0, 255);
		public static readonly Color DarkOrchid = new Color32(153, 50, 204, 255);
		public static readonly Color DarkRed = new Color32(139, 0, 0, 255);
		public static readonly Color DarkSalmon = new Color32(233, 150, 122, 255);
		public static readonly Color DarkSeaGreen = new Color32(143, 188, 143, 255);
		public static readonly Color DarkSlateBlue = new Color32(72, 61, 139, 255);
		public static readonly Color DarkSlateGray = new Color32(47, 79, 79, 255);
		public static readonly Color DarkTurquoise = new Color32(0, 206, 209, 255);
		public static readonly Color DarkViolet = new Color32(148, 0, 211, 255);
		public static readonly Color DeepPink = new Color32(255, 20, 147, 255);
		public static readonly Color DeepSkyBlue = new Color32(0, 191, 255, 255);
		public static readonly Color DimGray = new Color32(105, 105, 105, 255);
		public static readonly Color DodgerBlue = new Color32(30, 144, 255, 255);
		public static readonly Color FireBrick = new Color32(178, 34, 34, 255);
		public static readonly Color FloralWhite = new Color32(255, 250, 240, 255);
		public static readonly Color ForestGreen = new Color32(34, 139, 34, 255);
		public static readonly Color Fuchsia = new Color32(255, 0, 255, 255);
		public static readonly Color Gainsboro = new Color32(220, 220, 220, 255);
		public static readonly Color GhostWhite = new Color32(248, 248, 255, 255);
		public static readonly Color Gold = new Color32(255, 215, 0, 255);
		public static readonly Color Goldenrod = new Color32(218, 165, 32, 255);
		public static readonly Color Gray = new Color32(128, 128, 128, 255);
		public static readonly Color Green = new Color32(0, 128, 0, 255);
		public static readonly Color GreenYellow = new Color32(173, 255, 47, 255);
		public static readonly Color Honeydew = new Color32(240, 255, 240, 255);
		public static readonly Color HotPink = new Color32(255, 105, 180, 255);
		public static readonly Color IndianRed = new Color32(205, 92, 92, 255);
		public static readonly Color Indigo = new Color32(75, 0, 130, 255);
		public static readonly Color Ivory = new Color32(255, 255, 240, 255);
		public static readonly Color Khaki = new Color32(240, 230, 140, 255);
		public static readonly Color Lavender = new Color32(230, 230, 250, 255);
		public static readonly Color Lavenderblush = new Color32(255, 240, 245, 255);
		public static readonly Color LawnGreen = new Color32(124, 252, 0, 255);
		public static readonly Color LemonChiffon = new Color32(255, 250, 205, 255);
		public static readonly Color LightBlue = new Color32(173, 216, 230, 255);
		public static readonly Color LightCoral = new Color32(240, 128, 128, 255);
		public static readonly Color LightCyan = new Color32(224, 255, 255, 255);
		public static readonly Color LightGoldenodYellow = new Color32(250, 250, 210, 255);
		public static readonly Color LightGray = new Color32(211, 211, 211, 255);
		public static readonly Color LightGreen = new Color32(144, 238, 144, 255);
		public static readonly Color LightPink = new Color32(255, 182, 193, 255);
		public static readonly Color LightSalmon = new Color32(255, 160, 122, 255);
		public static readonly Color LightSeaGreen = new Color32(32, 178, 170, 255);
		public static readonly Color LightSkyBlue = new Color32(135, 206, 250, 255);
		public static readonly Color LightSlateGray = new Color32(119, 136, 153, 255);
		public static readonly Color LightSteelBlue = new Color32(176, 196, 222, 255);
		public static readonly Color LightYellow = new Color32(255, 255, 224, 255);
		public static readonly Color Lime = new Color32(0, 255, 0, 255);
		public static readonly Color LimeGreen = new Color32(50, 205, 50, 255);
		public static readonly Color Linen = new Color32(250, 240, 230, 255);
		public static readonly Color Magenta = new Color32(255, 0, 255, 255);
		public static readonly Color Maroon = new Color32(128, 0, 0, 255);
		public static readonly Color MediumAquamarine = new Color32(102, 205, 170, 255);
		public static readonly Color MediumBlue = new Color32(0, 0, 205, 255);
		public static readonly Color MediumOrchid = new Color32(186, 85, 211, 255);
		public static readonly Color MediumPurple = new Color32(147, 112, 219, 255);
		public static readonly Color MediumSeaGreen = new Color32(60, 179, 113, 255);
		public static readonly Color MediumSlateBlue = new Color32(123, 104, 238, 255);
		public static readonly Color MediumSpringGreen = new Color32(0, 250, 154, 255);
		public static readonly Color MediumTurquoise = new Color32(72, 209, 204, 255);
		public static readonly Color MediumVioletRed = new Color32(199, 21, 133, 255);
		public static readonly Color MidnightBlue = new Color32(25, 25, 112, 255);
		public static readonly Color Mintcream = new Color32(245, 255, 250, 255);
		public static readonly Color MistyRose = new Color32(255, 228, 225, 255);
		public static readonly Color Moccasin = new Color32(255, 228, 181, 255);
		public static readonly Color NavajoWhite = new Color32(255, 222, 173, 255);
		public static readonly Color Navy = new Color32(0, 0, 128, 255);
		public static readonly Color OldLace = new Color32(253, 245, 230, 255);
		public static readonly Color Olive = new Color32(128, 128, 0, 255);
		public static readonly Color Olivedrab = new Color32(107, 142, 35, 255);
		public static readonly Color Orange = new Color32(255, 165, 0, 255);
		public static readonly Color Orangered = new Color32(255, 69, 0, 255);
		public static readonly Color Orchid = new Color32(218, 112, 214, 255);
		public static readonly Color PaleGoldenrod = new Color32(238, 232, 170, 255);
		public static readonly Color PaleGreen = new Color32(152, 251, 152, 255);
		public static readonly Color PaleTurquoise = new Color32(175, 238, 238, 255);
		public static readonly Color PaleVioletred = new Color32(219, 112, 147, 255);
		public static readonly Color PapayaWhip = new Color32(255, 239, 213, 255);
		public static readonly Color PeachPuff = new Color32(255, 218, 185, 255);
		public static readonly Color Peru = new Color32(205, 133, 63, 255);
		public static readonly Color Pink = new Color32(255, 192, 203, 255);
		public static readonly Color Plum = new Color32(221, 160, 221, 255);
		public static readonly Color PowderBlue = new Color32(176, 224, 230, 255);
		public static readonly Color Purple = new Color32(128, 0, 128, 255);
		public static readonly Color Red = new Color32(255, 0, 0, 255);
		public static readonly Color RosyBrown = new Color32(188, 143, 143, 255);
		public static readonly Color RoyalBlue = new Color32(65, 105, 225, 255);
		public static readonly Color SaddleBrown = new Color32(139, 69, 19, 255);
		public static readonly Color Salmon = new Color32(250, 128, 114, 255);
		public static readonly Color SandyBrown = new Color32(244, 164, 96, 255);
		public static readonly Color SeaGreen = new Color32(46, 139, 87, 255);
		public static readonly Color Seashell = new Color32(255, 245, 238, 255);
		public static readonly Color Sienna = new Color32(160, 82, 45, 255);
		public static readonly Color Silver = new Color32(192, 192, 192, 255);
		public static readonly Color SkyBlue = new Color32(135, 206, 235, 255);
		public static readonly Color SlateBlue = new Color32(106, 90, 205, 255);
		public static readonly Color SlateGray = new Color32(112, 128, 144, 255);
		public static readonly Color Snow = new Color32(255, 250, 250, 255);
		public static readonly Color SpringGreen = new Color32(0, 255, 127, 255);
		public static readonly Color SteelBlue = new Color32(70, 130, 180, 255);
		public static readonly Color Tan = new Color32(210, 180, 140, 255);
		public static readonly Color Teal = new Color32(0, 128, 128, 255);
		public static readonly Color Thistle = new Color32(216, 191, 216, 255);
		public static readonly Color Tomato = new Color32(255, 99, 71, 255);
		public static readonly Color Turquoise = new Color32(64, 224, 208, 255);
		public static readonly Color Violet = new Color32(238, 130, 238, 255);
		public static readonly Color Wheat = new Color32(245, 222, 179, 255);
		public static readonly Color White = new Color32(255, 255, 255, 255);
		public static readonly Color WhiteSmoke = new Color32(245, 245, 245, 255);
		public static readonly Color Yellow = new Color32(255, 255, 0, 255);
		public static readonly Color YellowGreen = new Color32(154, 205, 50, 255);

		public static Dictionary<int, Color> ColorDictionary;

		public static void InitializeDictionary()
		{
			ColorDictionary = new Dictionary<int, Color>
			{
				{ 0, AliceBlue },
				{ 1, AntiqueWhite },
				{ 2, Aqua },
				{ 3, Aquamarine },
				{ 4, Azure },
				{ 5, Beige },
				{ 6, Bisque },
				{ 7, Black },
				{ 8, BlanchedAlmond },
				{ 9, Blue },
				{ 10, BlueViolet },
				{ 11, Brown },
				{ 12, Burlywood },
				{ 13, CadetBlue },
				{ 14, Chartreuse },
				{ 15, Chocolate },
				{ 16, Coral },
				{ 17, CornflowerBlue },
				{ 18, Cornsilk },
				{ 19, Crimson },
				{ 20, Cyan },
				{ 21, DarkBlue },
				{ 22, DarkCyan },
				{ 23, DarkGoldenrod },
				{ 24, DarkGray },
				{ 25, DarkGreen },
				{ 26, DarkKhaki },
				{ 27, DarkMagenta },
				{ 28, DarkOliveGreen },
				{ 29, DarkOrange },
				{ 30, DarkOrchid },
				{ 31, DarkRed },
				{ 32, DarkSalmon },
				{ 33, DarkSeaGreen },
				{ 34, DarkSlateBlue },
				{ 35, DarkSlateGray },
				{ 36, DarkTurquoise },
				{ 37, DarkViolet },
				{ 38, DeepPink },
				{ 39, DeepSkyBlue },
				{ 40, DimGray },
				{ 41, DodgerBlue },
				{ 42, FireBrick },
				{ 43, FloralWhite },
				{ 44, ForestGreen },
				{ 45, Fuchsia },
				{ 46, Gainsboro },
				{ 47, GhostWhite },
				{ 48, Gold },
				{ 49, Goldenrod },
				{ 50, Gray },
				{ 51, Green },
				{ 52, GreenYellow },
				{ 53, Honeydew },
				{ 54, HotPink },
				{ 55, IndianRed },
				{ 56, Indigo },
				{ 57, Ivory },
				{ 58, Khaki },
				{ 59, Lavender },
				{ 60, Lavenderblush },
				{ 61, LawnGreen },
				{ 62, LemonChiffon },
				{ 63, LightBlue },
				{ 64, LightCoral },
				{ 65, LightCyan },
				{ 66, LightGoldenodYellow },
				{ 67, LightGray },
				{ 68, LightGreen },
				{ 69, LightPink },
				{ 70, LightSalmon },
				{ 71, LightSeaGreen },
				{ 72, LightSkyBlue },
				{ 73, LightSlateGray },
				{ 74, LightSteelBlue },
				{ 75, LightYellow },
				{ 76, Lime },
				{ 77, LimeGreen },
				{ 78, Linen },
				{ 79, Magenta },
				{ 80, Maroon },
				{ 81, MediumAquamarine },
				{ 82, MediumBlue },
				{ 83, MediumOrchid },
				{ 84, MediumPurple },
				{ 85, MediumSeaGreen },
				{ 86, MediumSlateBlue },
				{ 87, MediumSpringGreen },
				{ 88, MediumTurquoise },
				{ 89, MediumVioletRed },
				{ 90, MidnightBlue },
				{ 91, Mintcream },
				{ 92, MistyRose },
				{ 93, Moccasin },
				{ 94, NavajoWhite },
				{ 95, Navy },
				{ 96, OldLace },
				{ 97, Olive },
				{ 98, Olivedrab },
				{ 99, Orange },
				{ 100, Orangered },
				{ 101, Orchid },
				{ 102, PaleGoldenrod },
				{ 103, PaleGreen },
				{ 104, PaleTurquoise },
				{ 105, PaleVioletred },
				{ 106, PapayaWhip },
				{ 107, PeachPuff },
				{ 108, Peru },
				{ 109, Pink },
				{ 110, Plum },
				{ 111, PowderBlue },
				{ 112, Purple },
				{ 113, Red },
				{ 114, RosyBrown },
				{ 115, RoyalBlue },
				{ 116, SaddleBrown },
				{ 117, Salmon },
				{ 118, SandyBrown },
				{ 119, SeaGreen },
				{ 120, Seashell },
				{ 121, Sienna },
				{ 122, Silver },
				{ 123, SkyBlue },
				{ 124, SlateBlue },
				{ 125, SlateGray },
				{ 126, Snow },
				{ 127, SpringGreen },
				{ 128, SteelBlue },
				{ 129, Tan },
				{ 130, Teal },
				{ 131, Thistle },
				{ 132, Tomato },
				{ 133, Turquoise },
				{ 134, Violet },
				{ 135, Wheat },
				{ 136, White },
				{ 137, WhiteSmoke },
				{ 138, Yellow },
				{ 139, YellowGreen },
			};
		}

		public static Color GetColorAt(int index)
		{
			if (ColorDictionary == null)
			{
				InitializeDictionary();
			}

			if (index < ColorDictionary.Count)
			{
				return ColorDictionary[index];
			}
			else
			{
				return Color.white;
			}
		}

		public static Color RandomColor()
		{
			int random = Random.Range(0, 140);
			return GetColorAt(random);
		}

		/// <summary>
		/// Returns a random color between the two min/max specified
		/// </summary>
		/// <param name="color"></param>
		/// <param name="min"></param>
		/// <param name="max"></param>
		/// <returns></returns>
		public static Color RandomColor(this Color color, Color min, Color max)
		{
			Color c = new Color()
			{
				r = UnityEngine.Random.Range(min.r, max.r),
				g = UnityEngine.Random.Range(min.g, max.g),
				b = UnityEngine.Random.Range(min.b, max.b),
				a = UnityEngine.Random.Range(min.a, max.a)
			};

			return c;
		}

		/// <summary>
		/// Returns a uniform "flat" gradient from the specified color and alpha
		/// </summary>
		/// <param name="color">the color to use for both ends of the gradient</param>
		/// <param name="alpha">the alpha to use for both ends of the gradient</param>
		/// <returns></returns>
		public static Gradient FlatGradient(Color32 color, float alpha = 1f)
		{
			return new Gradient()
			{
				colorKeys = new GradientColorKey[2]
				{
					new GradientColorKey(color, 0), new GradientColorKey(color, 1f)
				},
				alphaKeys = new GradientAlphaKey[2]
				{
					new GradientAlphaKey(alpha, 0), new GradientAlphaKey(alpha, 1)
				}
			};
		}

		/// <summary>
		/// Returns a simple gradient made of the two specified colors and alphas
		/// </summary>
		/// <param name="startColor">the color to use for the left side of the gradient</param>
		/// <param name="endColor">the color to use for the right side of the gradient</param>
		/// <param name="startAlpha">the alpha to use for the left side of the gradient</param>
		/// <param name="endAlpha">the alpha to use for the right side of the gradient</param>
		/// <returns></returns>
		public static Gradient SimpleGradient(Color32 startColor, Color32 endColor, float startAlpha = 1f,
			float endAlpha = 1f)
		{
			return new Gradient()
			{
				colorKeys = new GradientColorKey[2]
				{
					new GradientColorKey(startColor, 0), new GradientColorKey(endColor, 1f)
				},
				alphaKeys = new GradientAlphaKey[2]
				{
					new GradientAlphaKey(startAlpha, 0), new GradientAlphaKey(endAlpha, 1)
				}
			};
		}


		/// <summary>
		/// Tint : Uses HSV color conversions, keeps the original values, multiplies alpha
		/// Multiply : The whole color, including alpha, is multiplied over the original 
		/// Replace : completely replaces the original with the target color
		/// ReplaceKeepAlpha : color is replaced but the original alpha channel is ignored
		/// Add : target color gets added (including its alpha)
		/// </summary>
		public enum ColoringMode { Tint, Multiply, Replace, ReplaceKeepAlpha, Add }

		public static Color Colorize(this Color originalColor, Color targetColor, ColoringMode coloringMode, float lerpAmount = 1.0f)
		{
			Color resultColor = Color.white;
			switch (coloringMode)
			{
				case ColoringMode.Tint:
					{
						float s_h, s_s, s_v, t_h, t_s, t_v;
						Color.RGBToHSV(originalColor, out s_h, out s_s, out s_v);
						Color.RGBToHSV(targetColor, out t_h, out t_s, out t_v);
						resultColor = Color.HSVToRGB(t_h, t_s, s_v * t_v);
						resultColor.a = originalColor.a * targetColor.a;
					}
					break;
				case ColoringMode.Multiply:
					resultColor = originalColor * targetColor;
					break;
				case ColoringMode.Replace:
					resultColor = targetColor;
					break;
				case ColoringMode.ReplaceKeepAlpha:
					resultColor = targetColor;
					resultColor.a = originalColor.a;
					break;
				case ColoringMode.Add:
					resultColor = originalColor + targetColor;
					break;
				default:
					break;
			}
			return Color.Lerp(originalColor, resultColor, lerpAmount);
		}
	}
}