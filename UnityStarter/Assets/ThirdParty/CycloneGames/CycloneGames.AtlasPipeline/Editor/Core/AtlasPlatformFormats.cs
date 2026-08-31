using System;
using System.Collections.Generic;
using UnityEditor;
using CycloneGames.AtlasPipeline.Pure;

namespace CycloneGames.AtlasPipeline
{
    /// <summary>
    /// Platform targets exposed to the CycloneGames atlas pipeline. Only Android, iPhone, and WebGL are
    /// edited directly; desktop players use the <see cref="AtlasPlatform.Standalone"/>
    /// preset automatically instead of adding a fourth editor field for every rule.
    /// </summary>
    public enum AtlasPlatform
    {
        Android = 0,
        Iphone = 1,
        Webgl = 2,
        Standalone = 3,
    }

    /// <summary>
    /// Central source of truth for which texture formats are valid on each platform and how
    /// those formats map to Unity importer/atlas settings. Keeping this separate from the UI means
    /// validation and authoring can never drift.
    /// </summary>
    public static class AtlasPlatformFormats
    {
        public const string AndroidPlatformName = "Android";
        public const string IphonePlatformName = "iPhone";
        public const string WebglPlatformName = "WebGL";
        public const string StandalonePlatformName = "Standalone";
        public const int DefaultCompressionQuality = 50;

        // ASTC is the modern default on both mobile platforms but is not universally available:
        // Android needs OpenGL ES 3.1 or Vulkan, iOS needs an A8 GPU or later. ETC2 and PVRTC are the
        // fallbacks for devices below those lines, and they matter far more than RGBA32 ever did —
        // an uncompressed 2048px atlas costs 16 MB of VRAM where ETC2 RGBA8 costs 0.5 MB.
        private static readonly AtlasTextureFormat[] AndroidFormats =
        {
            AtlasTextureFormat.Astc4x4,
            AtlasTextureFormat.Astc5x5,
            AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat.Astc8x8,
            AtlasTextureFormat.Etc2Rgba8,
            AtlasTextureFormat.Etc2Rgb4,
            AtlasTextureFormat.Rgba32,
        };

        private static readonly AtlasTextureFormat[] IphoneFormats =
        {
            AtlasTextureFormat.Astc4x4,
            AtlasTextureFormat.Astc5x5,
            AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat.Astc8x8,
            AtlasTextureFormat.PvrtcRgba4,
            AtlasTextureFormat.PvrtcRgb4,
            AtlasTextureFormat.Rgba32,
        };

        private static readonly AtlasTextureFormat[] WebglFormats =
        {
            AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat.Astc4x4,
            AtlasTextureFormat.Astc5x5,
            AtlasTextureFormat.Astc8x8,
            AtlasTextureFormat.Dxt5,
            AtlasTextureFormat.Dxt1,
            AtlasTextureFormat.Rgba32,
        };

        private static readonly AtlasTextureFormat[] StandaloneFormats =
        {
            AtlasTextureFormat.Bc7,
            AtlasTextureFormat.Dxt5,
            AtlasTextureFormat.Rgba32,
        };

        public static IReadOnlyList<AtlasTextureFormat> GetSupportedFormats(
            AtlasPlatform platform)
        {
            switch (platform)
            {
                case AtlasPlatform.Android:
                    return AndroidFormats;
                case AtlasPlatform.Iphone:
                    return IphoneFormats;
                case AtlasPlatform.Webgl:
                    return WebglFormats;
                case AtlasPlatform.Standalone:
                    return StandaloneFormats;
                default:
                    return new AtlasTextureFormat[0];
            }
        }

        public static AtlasTextureFormat GetDefaultFormat(
            AtlasPlatform platform)
        {
            switch (platform)
            {
                case AtlasPlatform.Android:
                    return AtlasTextureFormat.Astc6x6;
                case AtlasPlatform.Iphone:
                    return AtlasTextureFormat.Astc6x6;
                case AtlasPlatform.Webgl:
                    return AtlasTextureFormat.Astc6x6;
                case AtlasPlatform.Standalone:
                    return AtlasTextureFormat.Bc7;
                default:
                    return AtlasTextureFormat.Rgba32;
            }
        }

        public static string GetPlatformName(AtlasPlatform platform)
        {
            switch (platform)
            {
                case AtlasPlatform.Android:
                    return AndroidPlatformName;
                case AtlasPlatform.Iphone:
                    return IphonePlatformName;
                case AtlasPlatform.Webgl:
                    return WebglPlatformName;
                case AtlasPlatform.Standalone:
                    return StandalonePlatformName;
                default:
                    return "Unknown";
            }
        }

        public static bool TryGetPlatformByName(
            string platformName,
            out AtlasPlatform platform)
        {
            if (string.Equals(
                    platformName,
                    AndroidPlatformName,
                    StringComparison.OrdinalIgnoreCase))
            {
                platform = AtlasPlatform.Android;
                return true;
            }

            if (string.Equals(
                    platformName,
                    IphonePlatformName,
                    StringComparison.OrdinalIgnoreCase))
            {
                platform = AtlasPlatform.Iphone;
                return true;
            }

            if (string.Equals(
                    platformName,
                    WebglPlatformName,
                    StringComparison.OrdinalIgnoreCase))
            {
                platform = AtlasPlatform.Webgl;
                return true;
            }

            if (string.Equals(
                    platformName,
                    StandalonePlatformName,
                    StringComparison.OrdinalIgnoreCase))
            {
                platform = AtlasPlatform.Standalone;
                return true;
            }

            platform = AtlasPlatform.Android;
            return false;
        }

        public static AtlasTextureFormat GetSafeFormat(
            AtlasPlatform platform,
            AtlasTextureFormat format)
        {
            return IsFormatSupported(platform, format)
                ? format
                : GetDefaultFormat(platform);
        }

        public static bool IsFormatSupported(
            AtlasPlatform platform,
            AtlasTextureFormat format)
        {
            IReadOnlyList<AtlasTextureFormat> supportedFormats =
                GetSupportedFormats(platform);
            for (int i = 0; i < supportedFormats.Count; i++)
            {
                if (supportedFormats[i] == format)
                {
                    return true;
                }
            }

            return false;
        }

        public static int GetSupportedFormatIndex(
            AtlasPlatform platform,
            AtlasTextureFormat format)
        {
            IReadOnlyList<AtlasTextureFormat> supportedFormats =
                GetSupportedFormats(platform);
            for (int i = 0; i < supportedFormats.Count; i++)
            {
                if (supportedFormats[i] == format)
                {
                    return i;
                }
            }

            return -1;
        }

        public static TextureImporterFormat ToTextureImporterFormat(
            AtlasTextureFormat format)
        {
            switch (format)
            {
                case AtlasTextureFormat.Astc4x4:
                    return TextureImporterFormat.ASTC_4x4;
                case AtlasTextureFormat.Astc5x5:
                    return TextureImporterFormat.ASTC_5x5;
                case AtlasTextureFormat.Astc6x6:
                    return TextureImporterFormat.ASTC_6x6;
                case AtlasTextureFormat.Astc8x8:
                    return TextureImporterFormat.ASTC_8x8;
                case AtlasTextureFormat.Dxt1:
                    return TextureImporterFormat.DXT1;
                case AtlasTextureFormat.Dxt5:
                    return TextureImporterFormat.DXT5;
                case AtlasTextureFormat.Bc7:
                    return TextureImporterFormat.BC7;
                case AtlasTextureFormat.Etc2Rgba8:
                    return TextureImporterFormat.ETC2_RGBA8;
                case AtlasTextureFormat.Etc2Rgb4:
                    return TextureImporterFormat.ETC2_RGB4;
                case AtlasTextureFormat.PvrtcRgba4:
                    return TextureImporterFormat.PVRTC_RGBA4;
                case AtlasTextureFormat.PvrtcRgb4:
                    return TextureImporterFormat.PVRTC_RGB4;
                default:
                    return TextureImporterFormat.RGBA32;
            }
        }

        /// <summary>
        /// Compressed bytes per pixel, for memory and package budgeting. Block-compressed formats are
        /// quoted as total block bits divided by pixels per block.
        /// These are encoder-independent planning numbers: the real texture size also depends on the
        /// platform encoder, mip chains and atlas fill rate, so treat them as an order-of-magnitude
        /// budget rather than a promise.
        /// </summary>
        public static double GetBytesPerPixel(AtlasTextureFormat format)
        {
            switch (format)
            {
                case AtlasTextureFormat.Astc4x4:
                    return 128d / 16d / 8d;
                case AtlasTextureFormat.Astc5x5:
                    return 128d / 25d / 8d;
                case AtlasTextureFormat.Astc6x6:
                    return 128d / 36d / 8d;
                case AtlasTextureFormat.Astc8x8:
                    return 128d / 64d / 8d;
                case AtlasTextureFormat.Etc2Rgba8:
                    return 1d;
                case AtlasTextureFormat.Etc2Rgb4:
                    return 0.5d;
                case AtlasTextureFormat.PvrtcRgba4:
                    return 0.5d;
                case AtlasTextureFormat.PvrtcRgb4:
                    return 0.5d;
                case AtlasTextureFormat.Dxt1:
                    return 0.5d;
                case AtlasTextureFormat.Dxt5:
                    return 1d;
                case AtlasTextureFormat.Bc7:
                    return 1d;
                case AtlasTextureFormat.Rgba32:
                    return 4d;
                default:
                    return 4d;
            }
        }

        /// <summary>Estimated VRAM for one square atlas texture, in bytes.</summary>
        public static long EstimateAtlasBytes(int maxTextureSize, AtlasTextureFormat format)
        {
            if (maxTextureSize <= 0)
            {
                return 0L;
            }

            return AtlasCapacityPlanner.EstimateBytes(
                (long)maxTextureSize * maxTextureSize,
                GetBytesPerPixel(format));
        }

        /// <summary>
        /// Block-compressed formats require power-of-two dimensions. Unity silently clamps or fails to
        /// compress otherwise, which shows up as a blurry or oversized atlas rather than an error.
        /// </summary>
        public static bool IsPowerOfTwo(int value)
        {
            return value > 0 && (value & (value - 1)) == 0;
        }

        /// <summary>
        /// Atlas size at or above which the memory cost is worth naming in the validation warnings.
        /// 4096 is four times 2048 in both dimensions, so four times the memory.
        /// </summary>
        public const int LargeAtlasSizeThreshold = 4096;

        public static bool RequiresPowerOfTwo(AtlasTextureFormat format)
        {
            return format != AtlasTextureFormat.Rgba32;
        }

        public static TextureImporterCompression ToTextureImporterCompression(
            AtlasTextureFormat format)
        {
            return format == AtlasTextureFormat.Rgba32
                ? TextureImporterCompression.Uncompressed
                : TextureImporterCompression.Compressed;
        }

        public static string GetDisplayName(AtlasTextureFormat format)
        {
            switch (format)
            {
                case AtlasTextureFormat.Astc4x4:
                    return "ASTC 4x4";
                case AtlasTextureFormat.Astc5x5:
                    return "ASTC 5x5";
                case AtlasTextureFormat.Astc6x6:
                    return "ASTC 6x6";
                case AtlasTextureFormat.Astc8x8:
                    return "ASTC 8x8";
                case AtlasTextureFormat.Dxt1:
                    return "DXT1";
                case AtlasTextureFormat.Dxt5:
                    return "DXT5";
                case AtlasTextureFormat.Bc7:
                    return "BC7";
                case AtlasTextureFormat.Etc2Rgba8:
                    return "ETC2 RGBA8";
                case AtlasTextureFormat.Etc2Rgb4:
                    return "ETC2 RGB4";
                case AtlasTextureFormat.PvrtcRgba4:
                    return "PVRTC RGBA4";
                case AtlasTextureFormat.PvrtcRgb4:
                    return "PVRTC RGB4";
                default:
                    return "RGBA 32";
            }
        }

        public static bool IsCompressionQualityValid(int quality)
        {
            return quality >= 0 && quality <= 100;
        }

        /// <summary>
        /// Validates a rule's platform formats and atlas sizes.
        /// </summary>
        /// <param name="errors">Blocking problems: the build must not proceed.</param>
        /// <param name="warnings">
        /// Costly but legitimate choices. Kept separate because a rule that legitimately needs
        /// uncompressed output (pixel art) must not be blocked by a memory advisory.
        /// </param>
        public static void ValidateRule(
            AtlasImportRule rule,
            ICollection<string> errors,
            ICollection<string> warnings = null)
        {
            if (rule == null)
            {
                return;
            }

            if (!IsCompressionQualityValid(rule.CompressionQuality))
            {
                errors.Add(
                    $"Import rule '{rule.Name}' has invalid compression quality "
                    + $"'{rule.CompressionQuality}'. Expected a value between 0 and 100.");
            }

            // The recommended max is the one size in a rule that is a free integer, and it is copied
            // into every source importer's Max Size. A non-power-of-two value there is worse than on
            // the atlas: Unity resizes the source down to that cap and the RESULT is a
            // non-power-of-two texture, which PVRTC cannot compress at all and WebGL 1 refuses to
            // mip. Nothing errors at import time — the atlas simply ships uncompressed or blurry.
            int recommendedMax = rule.RecommendedMaxTextureSize;
            if (!IsPowerOfTwo(recommendedMax))
            {
                errors.Add(
                    $"Import rule '{rule.Name}' has a recommended max texture size of "
                    + $"'{recommendedMax}', which is not a power of two. It becomes the Max Size of "
                    + "every source importer the rule owns, so sources capped at a "
                    + "non-power-of-two size lose PVRTC compression on iOS and their mip chain on "
                    + "WebGL 1. Use 256, 512, 1024, 2048 or 4096.");
            }

            ValidatePlatformValue(
                rule,
                AtlasPlatform.Android,
                rule.AndroidFormat,
                errors);
            ValidatePlatformValue(
                rule,
                AtlasPlatform.Iphone,
                rule.IphoneFormat,
                errors);
            ValidatePlatformValue(
                rule,
                AtlasPlatform.Webgl,
                rule.WebglFormat,
                errors);
            ValidatePlatformValue(
                rule,
                AtlasPlatform.Standalone,
                rule.StandaloneFormat,
                errors);

            ValidateAtlasSize(
                rule,
                AtlasPlatform.Android,
                rule.AndroidFormat,
                errors,
                warnings);
            ValidateAtlasSize(
                rule,
                AtlasPlatform.Iphone,
                rule.IphoneFormat,
                errors,
                warnings);
            ValidateAtlasSize(
                rule,
                AtlasPlatform.Webgl,
                rule.WebglFormat,
                errors,
                warnings);
            ValidateAtlasSize(
                rule,
                AtlasPlatform.Standalone,
                rule.StandaloneFormat,
                errors,
                warnings);
        }

        /// <summary>
        /// Block-compressed formats need power-of-two dimensions. Unity does not error on a
        /// non-power-of-two atlas size; it silently fails to compress or clamps, which surfaces as a
        /// blurry or unexpectedly large atlas rather than as a build failure.
        /// </summary>
        private static void ValidateAtlasSize(
            AtlasImportRule rule,
            AtlasPlatform platform,
            AtlasTextureFormat format,
            ICollection<string> errors,
            ICollection<string> warnings)
        {
            int maxSize = rule.GetAtlasMaxTextureSize(platform);
            if (maxSize <= 0)
            {
                errors.Add(
                    $"Import rule '{rule.Name}' has an invalid {GetPlatformName(platform)} atlas "
                    + $"max size '{maxSize}'. Use a power of two such as 512, 1024 or 2048.");
                return;
            }

            if (RequiresPowerOfTwo(format) && !IsPowerOfTwo(maxSize))
            {
                errors.Add(
                    $"Import rule '{rule.Name}' uses {GetPlatformName(platform)} atlas max size "
                    + $"'{maxSize}' with '{GetDisplayName(format)}', which requires power-of-two "
                    + "dimensions. Unity will not compress it correctly. Use a power of two, or "
                    + "switch the platform to RGBA 32 if the atlas genuinely must stay uncompressed.");
            }

            // RGBA32 on a 2048px atlas is 16 MB of VRAM against 0.5 MB for ETC2 RGBA8. Now that ETC2
            // and PVRTC are available there is almost always a better choice — but pixel-art rules
            // legitimately need uncompressed output, so this reports the cost instead of blocking.
            if (format == AtlasTextureFormat.Rgba32
                && (platform == AtlasPlatform.Android || platform == AtlasPlatform.Iphone)
                && warnings != null)
            {
                long uncompressed = EstimateAtlasBytes(maxSize, AtlasTextureFormat.Rgba32);
                AtlasTextureFormat fallback = platform == AtlasPlatform.Android
                    ? AtlasTextureFormat.Etc2Rgba8
                    : AtlasTextureFormat.PvrtcRgba4;
                long compressed = EstimateAtlasBytes(maxSize, fallback);

                warnings.Add(
                    $"Import rule '{rule.Name}' uses RGBA 32 on {GetPlatformName(platform)} at "
                    + $"{maxSize}px, about {uncompressed / (1024 * 1024)} MB of VRAM per atlas. "
                    + $"'{GetDisplayName(fallback)}' would cost about "
                    + $"{compressed / (1024 * 1024)} MB. Keep RGBA 32 only if the atlas must stay "
                    + "uncompressed (pixel art, or hardware below the ASTC/ETC2 line).");
            }

            // 4096 is a legitimate choice — fewer, larger atlas files — but it costs four times the
            // memory of 2048 and sits at the limit of what low-end mobile GPUs accept. Reported, not
            // blocked: a project that only targets modern hardware may well want it.
            if (maxSize >= LargeAtlasSizeThreshold
                && (platform == AtlasPlatform.Android || platform == AtlasPlatform.Iphone)
                && warnings != null
                && format != AtlasTextureFormat.Rgba32)
            {
                long atlasBytes = EstimateAtlasBytes(maxSize, format);
                long halvedBytes = EstimateAtlasBytes(maxSize / 2, format);
                warnings.Add(
                    $"Import rule '{rule.Name}' uses a {maxSize}px atlas on "
                    + $"{GetPlatformName(platform)} with '{GetDisplayName(format)}', about "
                    + $"{atlasBytes / (1024 * 1024)} MB of VRAM per atlas. Halving to "
                    + $"{maxSize / 2}px would cost about {halvedBytes / (1024 * 1024)} MB. "
                    + "Large atlases reduce the atlas count, but check the target hardware's "
                    + "maximum texture size first.");
            }
        }

        private static void ValidatePlatformValue(
            AtlasImportRule rule,
            AtlasPlatform platform,
            AtlasTextureFormat format,
            ICollection<string> errors)
        {
            if (IsFormatSupported(platform, format))
            {
                return;
            }

            IReadOnlyList<AtlasTextureFormat> supportedFormats =
                GetSupportedFormats(platform);
            string[] supportedNames = new string[supportedFormats.Count];
            for (int i = 0; i < supportedFormats.Count; i++)
            {
                supportedNames[i] = GetDisplayName(supportedFormats[i]);
            }

            string supportedList = string.Join(", ", supportedNames);
            errors.Add(
                $"Import rule '{rule.Name}' uses unsupported {GetPlatformName(platform)} "
                + $"format '{GetDisplayName(format)}'. "
                + $"Supported {GetPlatformName(platform)} formats: {supportedList}.");
        }
    }
}
