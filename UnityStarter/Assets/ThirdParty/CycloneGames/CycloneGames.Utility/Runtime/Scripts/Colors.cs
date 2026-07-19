using System;
using System.Runtime.CompilerServices;
using UnityEngine;

namespace CycloneGames.Utility.Runtime
{
    /// <summary>
    /// A utility class providing a rich set of predefined colors and color-related helper methods.
    /// CSS3 specification color names are used for consistency.
    /// </summary>
    public static class Colors
    {
        private const string HexDigits = "0123456789ABCDEF";

        // CSS3 color definitions
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
        public static readonly Color LightGoldenrodYellow = new Color32(250, 250, 210, 255);
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

        private static readonly Color[] ColorArray =
        {
            AliceBlue, AntiqueWhite, Aqua, Aquamarine, Azure, Beige, Bisque, Black, BlanchedAlmond, Blue,
            BlueViolet, Brown, Burlywood, CadetBlue, Chartreuse, Chocolate, Coral, CornflowerBlue, Cornsilk, Crimson,
            Cyan, DarkBlue, DarkCyan, DarkGoldenrod, DarkGray, DarkGreen, DarkKhaki, DarkMagenta, DarkOliveGreen, DarkOrange,
            DarkOrchid, DarkRed, DarkSalmon, DarkSeaGreen, DarkSlateBlue, DarkSlateGray, DarkTurquoise, DarkViolet, DeepPink, DeepSkyBlue,
            DimGray, DodgerBlue, FireBrick, FloralWhite, ForestGreen, Fuchsia, Gainsboro, GhostWhite, Gold, Goldenrod,
            Gray, Green, GreenYellow, Honeydew, HotPink, IndianRed, Indigo, Ivory, Khaki, Lavender,
            Lavenderblush, LawnGreen, LemonChiffon, LightBlue, LightCoral, LightCyan, LightGoldenrodYellow, LightGray, LightGreen, LightPink,
            LightSalmon, LightSeaGreen, LightSkyBlue, LightSlateGray, LightSteelBlue, LightYellow, Lime, LimeGreen, Linen, Magenta,
            Maroon, MediumAquamarine, MediumBlue, MediumOrchid, MediumPurple, MediumSeaGreen, MediumSlateBlue, MediumSpringGreen, MediumTurquoise, MediumVioletRed,
            MidnightBlue, Mintcream, MistyRose, Moccasin, NavajoWhite, Navy, OldLace, Olive, Olivedrab, Orange,
            Orangered, Orchid, PaleGoldenrod, PaleGreen, PaleTurquoise, PaleVioletred, PapayaWhip, PeachPuff, Peru, Pink,
            Plum, PowderBlue, Purple, Red, RosyBrown, RoyalBlue, SaddleBrown, Salmon, SandyBrown, SeaGreen,
            Seashell, Sienna, Silver, SkyBlue, SlateBlue, SlateGray, Snow, SpringGreen, SteelBlue, Tan,
            Teal, Thistle, Tomato, Turquoise, Violet, Wheat, White, WhiteSmoke, Yellow, YellowGreen
        };

        public static int ColorCount => ColorArray.Length;

        /// <summary>
        /// Gets a color by index. Returns white if index is out of range.
        /// </summary>
        public static Color GetColorAt(int index)
        {
            if ((uint)index < (uint)ColorArray.Length)
            {
                return ColorArray[index];
            }
            return Color.white;
        }

        /// <summary>
        /// Attempts to get a palette color without using a fallback value.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetColorAt(int index, out Color color)
        {
            if ((uint)index < (uint)ColorArray.Length)
            {
                color = ColorArray[index];
                return true;
            }
            color = default;
            return false;
        }

        /// <summary>
        /// Returns a random color from the palette using Unity's global random state.
        /// Use the System.Random overload when the caller owns deterministic random state.
        /// </summary>
        public static Color RandomColor()
        {
            return ColorArray[UnityEngine.Random.Range(0, ColorArray.Length)];
        }

        /// <summary>
        /// Returns a random palette color using caller-owned random state.
        /// </summary>
        public static Color RandomColor(System.Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            return ColorArray[random.Next(ColorArray.Length)];
        }

        /// <summary>
        /// Returns a random color with each channel randomized between min and max.
        /// </summary>
        public static Color RandomColor(Color min, Color max)
        {
            ValidateRandomRange(min, max);
            return new Color
            {
                r = UnityEngine.Random.Range(min.r, max.r),
                g = UnityEngine.Random.Range(min.g, max.g),
                b = UnityEngine.Random.Range(min.b, max.b),
                a = UnityEngine.Random.Range(min.a, max.a)
            };
        }

        /// <summary>
        /// Returns a random color within per-channel [min, max) bounds using caller-owned random state.
        /// </summary>
        public static Color RandomColor(Color min, Color max, System.Random random)
        {
            if (random == null) throw new ArgumentNullException(nameof(random));
            ValidateRandomRange(min, max);

            return new Color(
                NextFloat(random, min.r, max.r),
                NextFloat(random, min.g, max.g),
                NextFloat(random, min.b, max.b),
                NextFloat(random, min.a, max.a));
        }

        // --- Common Color Utilities ---

        /// <summary>
        /// Returns a copy of the color with the specified alpha.
        /// </summary>
        public static Color WithAlpha(this Color color, float alpha)
        {
            color.a = alpha;
            return color;
        }

        /// <summary>
        /// Converts a Color to a hex string (e.g. "#FF00AAFF").
        /// The conversion quantizes and clamps channels through Color32 and allocates only the result string.
        /// </summary>
        public static string ToHexString(this Color color)
        {
            Span<char> buffer = stackalloc char[9];
            TryFormatHex(color, buffer, out int charsWritten);
            return new string(buffer.Slice(0, charsWritten));
        }

        /// <summary>
        /// Attempts to write an uppercase hexadecimal color without intermediate allocations.
        /// </summary>
        public static bool TryFormatHex(
            this Color color,
            Span<char> destination,
            out int charsWritten,
            bool includeAlpha = true,
            bool includeHash = true)
        {
            int requiredLength = (includeHash ? 1 : 0) + (includeAlpha ? 8 : 6);
            if (destination.Length < requiredLength)
            {
                charsWritten = 0;
                return false;
            }

            Color32 value = color;
            int position = 0;
            if (includeHash)
            {
                destination[position++] = '#';
            }
            WriteHexByte(value.r, destination, ref position);
            WriteHexByte(value.g, destination, ref position);
            WriteHexByte(value.b, destination, ref position);
            if (includeAlpha)
            {
                WriteHexByte(value.a, destination, ref position);
            }

            charsWritten = position;
            return true;
        }

        /// <summary>
        /// Parses a hex string ("#RRGGBB" or "#RRGGBBAA", with or without '#') into a Color.
        /// Returns false if the format is invalid.
        /// </summary>
        public static bool TryParseHex(string hex, out Color color)
        {
            if (hex == null)
            {
                color = Color.white;
                return false;
            }
            return TryParseHex(hex.AsSpan(), out color);
        }

        /// <summary>
        /// Parses "#RRGGBB" or "#RRGGBBAA" from a character span without creating substrings.
        /// </summary>
        public static bool TryParseHex(ReadOnlySpan<char> hex, out Color color)
        {
            color = Color.white;
            if (!hex.IsEmpty && hex[0] == '#')
            {
                hex = hex.Slice(1);
            }
            if (hex.Length != 6 && hex.Length != 8)
            {
                return false;
            }

            if (!TryReadHexByte(hex, 0, out byte r) ||
                !TryReadHexByte(hex, 2, out byte g) ||
                !TryReadHexByte(hex, 4, out byte b))
            {
                return false;
            }

            byte a = 255;
            if (hex.Length == 8 && !TryReadHexByte(hex, 6, out a))
            {
                return false;
            }

            color = new Color32(r, g, b, a);
            return true;
        }

        /// <summary>
        /// Returns a simple BT.601 weighted luma value from the supplied RGB components.
        /// This is not the WCAG relative luminance used for accessibility contrast.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetLuminance(this Color color)
        {
            return 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
        }

        /// <summary>
        /// Returns WCAG relative luminance for gamma-encoded sRGB channels. Alpha is ignored.
        /// Input channels are clamped to [0, 1].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetRelativeLuminance(this Color color)
        {
            float r = LinearizeSrgb(Mathf.Clamp01(color.r));
            float g = LinearizeSrgb(Mathf.Clamp01(color.g));
            float b = LinearizeSrgb(Mathf.Clamp01(color.b));
            return 0.2126f * r + 0.7152f * g + 0.0722f * b;
        }

        /// <summary>
        /// Returns the WCAG contrast ratio between two opaque, gamma-encoded sRGB colors.
        /// Alpha is ignored. The result is in the inclusive range [1, 21].
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float GetContrastRatio(this Color first, Color second)
        {
            float firstLuminance = first.GetRelativeLuminance();
            float secondLuminance = second.GetRelativeLuminance();
            float lighter = Mathf.Max(firstLuminance, secondLuminance);
            float darker = Mathf.Min(firstLuminance, secondLuminance);
            return (lighter + 0.05f) / (darker + 0.05f);
        }

        /// <summary>
        /// Packs a Color into a single uint (RGBA, 8 bits per channel).
        /// The stable numeric layout is 0xRRGGBBAA. Color is quantized and clamped through Color32.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static uint ToUInt32(this Color color)
        {
            Color32 c = color;
            return ((uint)c.r << 24) | ((uint)c.g << 16) | ((uint)c.b << 8) | c.a;
        }

        /// <summary>
        /// Unpacks a stable 0xRRGGBBAA value back into a Color.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color FromUInt32(uint packed)
        {
            return new Color32(
                (byte)(packed >> 24),
                (byte)(packed >> 16),
                (byte)(packed >> 8),
                (byte)packed);
        }

        /// <summary>
        /// Returns the inverted color (1-r, 1-g, 1-b), preserving alpha.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color Invert(this Color color)
        {
            return new Color(1f - color.r, 1f - color.g, 1f - color.b, color.a);
        }

        /// <summary>
        /// Adjusts brightness in HSV space by multiplying the V channel.
        /// More visually natural than multiplying RGB directly.
        /// factor > 1 = brighter, factor between 0 and 1 = darker.
        /// </summary>
        public static Color AdjustBrightness(this Color color, float factor)
        {
            if (!IsFinite(factor) || factor < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(factor), factor, "Brightness factor must be finite and non-negative.");
            }
            Color.RGBToHSV(color, out float h, out float s, out float v);
            v = Mathf.Clamp01(v * factor);
            Color result = Color.HSVToRGB(h, s, v);
            result.a = color.a;
            return result;
        }

        /// <summary>
        /// Desaturates a color by the given amount (0 = original, 1 = full grayscale).
        /// The amount is clamped to [0, 1].
        /// </summary>
        public static Color Desaturate(this Color color, float amount)
        {
            amount = Mathf.Clamp01(amount);
            float gray = 0.299f * color.r + 0.587f * color.g + 0.114f * color.b;
            return new Color(
                color.r + (gray - color.r) * amount,
                color.g + (gray - color.g) * amount,
                color.b + (gray - color.b) * amount,
                color.a);
        }

        /// <summary>
        /// Linearly interpolates between two colors without clamping t.
        /// Allows overshoot for elastic/spring animations.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static Color LerpUnclamped(Color a, Color b, float t)
        {
            return new Color(
                a.r + (b.r - a.r) * t,
                a.g + (b.g - a.g) * t,
                a.b + (b.b - a.b) * t,
                a.a + (b.a - a.a) * t);
        }

        /// <summary>
        /// Returns a uniform "flat" gradient from the specified color and alpha.
        /// </summary>
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
        /// Returns a simple gradient made of the two specified colors and alphas.
        /// </summary>
        public static Gradient SimpleGradient(Color32 startColor, Color32 endColor, float startAlpha = 1f, float endAlpha = 1f)
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

        public enum ColoringMode
        {
            Tint = 0,
            Multiply = 1,
            Replace = 2,
            ReplaceKeepAlpha = 3,
            Add = 4
        }

        public static Color Colorize(this Color originalColor, Color targetColor, ColoringMode coloringMode, float lerpAmount = 1.0f)
        {
            Color resultColor = Color.white;
            switch (coloringMode)
            {
                case ColoringMode.Tint:
                    {
                        Color.RGBToHSV(originalColor, out _, out _, out float s_v);
                        Color.RGBToHSV(targetColor, out float t_h, out float t_s, out float t_v);
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
                    throw new ArgumentOutOfRangeException(nameof(coloringMode), coloringMode, "Unknown coloring mode.");
            }
            return Color.Lerp(originalColor, resultColor, lerpAmount);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteHexByte(byte value, Span<char> destination, ref int position)
        {
            destination[position++] = HexDigits[value >> 4];
            destination[position++] = HexDigits[value & 0x0F];
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool TryReadHexByte(ReadOnlySpan<char> value, int index, out byte result)
        {
            int high = HexValue(value[index]);
            int low = HexValue(value[index + 1]);
            if ((high | low) < 0)
            {
                result = 0;
                return false;
            }
            result = (byte)((high << 4) | low);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int HexValue(char value)
        {
            if ((uint)(value - '0') <= 9u) return value - '0';
            if ((uint)(value - 'A') <= 5u) return value - 'A' + 10;
            if ((uint)(value - 'a') <= 5u) return value - 'a' + 10;
            return -1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float LinearizeSrgb(float channel)
        {
            return channel <= 0.04045f
                ? channel / 12.92f
                : Mathf.Pow((channel + 0.055f) / 1.055f, 2.4f);
        }

        private static void ValidateRandomRange(Color min, Color max)
        {
            if (!IsFinite(min.r) || !IsFinite(min.g) || !IsFinite(min.b) || !IsFinite(min.a) ||
                !IsFinite(max.r) || !IsFinite(max.g) || !IsFinite(max.b) || !IsFinite(max.a) ||
                min.r > max.r || min.g > max.g || min.b > max.b || min.a > max.a)
            {
                throw new ArgumentException("Color random bounds must be finite and ordered per channel.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static float NextFloat(System.Random random, float min, float max)
        {
            return (float)(min + ((double)max - min) * random.NextDouble());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
