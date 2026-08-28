using System;
using System.Collections.Generic;
using UnityEditor;

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

        private static readonly AtlasTextureFormat[] AndroidFormats =
        {
            AtlasTextureFormat.Astc4x4,
            AtlasTextureFormat.Astc5x5,
            AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat.Astc8x8,
            AtlasTextureFormat.Rgba32,
        };

        private static readonly AtlasTextureFormat[] IphoneFormats =
        {
            AtlasTextureFormat.Astc4x4,
            AtlasTextureFormat.Astc5x5,
            AtlasTextureFormat.Astc6x6,
            AtlasTextureFormat.Astc8x8,
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
                default:
                    return TextureImporterFormat.RGBA32;
            }
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
                default:
                    return "RGBA 32";
            }
        }

        public static bool IsCompressionQualityValid(int quality)
        {
            return quality >= 0 && quality <= 100;
        }

        public static void ValidateRule(
            AtlasImportRule rule,
            ICollection<string> errors)
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
