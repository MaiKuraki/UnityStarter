using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    /// <summary>
    /// Configuration for Dynamic Atlas Service.
    /// Supports custom texture formats, block alignment, and platform-specific optimizations.
    /// </summary>
    [Serializable]
    public class DynamicAtlasConfig
    {
        public enum PlatformTier
        {
            DesktopHighEnd = 0,
            MobileHighEnd = 1,
            MobileLowEnd = 2,
            WebGL = 3,
        }

        [Tooltip("Page size in pixels (0 = auto-detect based on device capabilities)")]
        public int pageSize = 0;

        [Tooltip("Automatically scale textures that exceed page size")]
        public bool autoScaleLargeTextures = true;

        [Tooltip("Target texture format for atlas pages (RGBA32 = uncompressed, ASTC_4x4 = compressed)")]
        public TextureFormat targetFormat = TextureFormat.RGBA32;

        [Tooltip("Enable block alignment for compressed formats (required for ASTC/ETC/BC)")]
        public bool enableBlockAlignment = true;

        [Tooltip("Padding between sprites in pixels")]
        public int padding = 2;

        [Tooltip("Enable edge bleeding (gutter pixels) to prevent texture sampling artifacts at sprite boundaries")]
        public bool enableBleed = true;

        [Tooltip("Enable platform-specific optimizations (NativeArray, unsafe code, etc.)")]
        public bool enablePlatformOptimizations = true;

        [Tooltip("Maximum number of atlas pages (0 = unlimited)")]
        public int maxPages = 0;

        [Tooltip("Enable mipmap generation for atlas pages (needed for world-space UI or camera distance filtering)")]
        public bool enableMipmap = false;

        [Tooltip("Allow CPU fallback copy paths (ReadPixels / RenderTexture bridge) when GPU copy and raw buffer copy are unavailable.")]
        public bool allowCpuReadPixelsFallback = true;

        [Tooltip("Allow CPU-side bleed generation on fallback paths. Disable this on constrained platforms to avoid GetPixels/SetPixels overhead.")]
        public bool allowCpuBleedFallback = true;

        [Tooltip("Custom texture loader (null = Resources.Load)")]
        public Func<string, Texture2D> loadFunc;

        [Tooltip("Custom texture unloader (null = Resources.UnloadAsset)")]
        public Action<string, Texture2D> unloadFunc;

        [Tooltip("Async texture loader for non-blocking I/O (optional, used by GetSpriteAsync)")]
        public Func<string, UniTask<Texture2D>> loadFuncAsync;

        public DynamicAtlasConfig()
        {
        }

        public DynamicAtlasConfig(int pageSize, bool autoScaleLargeTextures = true)
        {
            this.pageSize = pageSize;
            this.autoScaleLargeTextures = autoScaleLargeTextures;
        }

        public DynamicAtlasConfig(
            Func<string, Texture2D> loadFunc,
            Action<string, Texture2D> unloadFunc,
            int pageSize = 0,
            bool autoScaleLargeTextures = true)
        {
            this.loadFunc = loadFunc;
            this.unloadFunc = unloadFunc;
            this.pageSize = pageSize;
            this.autoScaleLargeTextures = autoScaleLargeTextures;
        }

        /// <summary>
        /// Creates a configuration optimized for the current platform.
        /// </summary>
        public static DynamicAtlasConfig CreatePlatformOptimized(
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null,
            bool useCompression = false)
        {
            var config = new DynamicAtlasConfig
            {
                loadFunc = loadFunc,
                unloadFunc = unloadFunc,
                enablePlatformOptimizations = true,
                enableBlockAlignment = true
            };

            if (useCompression)
            {
                config.targetFormat = TextureFormatHelper.GetRecommendedCompressedFormat();
            }
            else
            {
                config.targetFormat = TextureFormatHelper.GetRecommendedUncompressedFormat();
            }

            // Auto-detect page size based on device
            config.pageSize = 0;
            config.autoScaleLargeTextures = true;

            return config;
        }

        /// <summary>
        /// Creates a configuration tuned for a specific runtime capability tier.
        /// </summary>
        public static DynamicAtlasConfig CreateForTier(
            PlatformTier tier,
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null,
            bool useCompression = false)
        {
            var config = new DynamicAtlasConfig
            {
                loadFunc = loadFunc,
                unloadFunc = unloadFunc,
                autoScaleLargeTextures = true,
                enablePlatformOptimizations = tier != PlatformTier.WebGL,
                enableBlockAlignment = useCompression,
                targetFormat = useCompression ? TextureFormatHelper.GetRecommendedCompressedFormat() : TextureFormatHelper.GetRecommendedUncompressedFormat()
            };

            switch (tier)
            {
                case PlatformTier.DesktopHighEnd:
                    config.pageSize = 4096;
                    config.maxPages = 0;
                    config.enableBleed = true;
                    config.enableMipmap = false;
                    config.allowCpuReadPixelsFallback = true;
                    config.allowCpuBleedFallback = true;
                    break;

                case PlatformTier.MobileHighEnd:
                    config.pageSize = 2048;
                    config.maxPages = 8;
                    config.enableBleed = true;
                    config.enableMipmap = false;
                    config.allowCpuReadPixelsFallback = true;
                    config.allowCpuBleedFallback = false;
                    break;

                case PlatformTier.MobileLowEnd:
                    config.pageSize = 1024;
                    config.maxPages = 4;
                    config.enableBleed = false;
                    config.enableMipmap = false;
                    config.targetFormat = TextureFormatHelper.GetRecommendedUncompressedFormat();
                    config.enableBlockAlignment = false;
                    config.allowCpuReadPixelsFallback = false;
                    config.allowCpuBleedFallback = false;
                    break;

                case PlatformTier.WebGL:
                    config.pageSize = 1024;
                    config.maxPages = 2;
                    config.enableBleed = false;
                    config.enableMipmap = false;
                    config.targetFormat = TextureFormat.RGBA32;
                    config.enablePlatformOptimizations = false;
                    config.enableBlockAlignment = false;
                    config.allowCpuReadPixelsFallback = false;
                    config.allowCpuBleedFallback = false;
                    break;
            }

            return config;
        }

        /// <summary>
        /// Creates a configuration based on the current runtime platform class.
        /// </summary>
        public static DynamicAtlasConfig CreateForCurrentPlatform(
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null,
            bool useCompression = false,
            bool preferLowMemoryProfile = false)
        {
#if UNITY_WEBGL
            return CreateForTier(PlatformTier.WebGL, loadFunc, unloadFunc, false);
#elif UNITY_ANDROID || UNITY_IOS || UNITY_TVOS
            return CreateForTier(preferLowMemoryProfile ? PlatformTier.MobileLowEnd : PlatformTier.MobileHighEnd, loadFunc, unloadFunc, useCompression);
#else
            return CreateForTier(preferLowMemoryProfile ? PlatformTier.MobileHighEnd : PlatformTier.DesktopHighEnd, loadFunc, unloadFunc, useCompression);
#endif
        }

        /// <summary>
        /// Gets the block size for the target format.
        /// </summary>
        public int GetBlockSize()
        {
            return TextureFormatHelper.GetBlockSize(targetFormat);
        }

        /// <summary>
        /// Checks if the target format requires block alignment.
        /// </summary>
        public bool RequiresBlockAlignment()
        {
            return enableBlockAlignment && TextureFormatHelper.RequiresBlockAlignment(targetFormat);
        }

        /// <summary>
        /// Validates the configuration for the current platform.
        /// </summary>
        public bool Validate(out string errorMessage)
        {
            if (!TextureFormatHelper.IsFormatSupported(targetFormat))
            {
                errorMessage = $"Texture format {targetFormat} is not supported on this platform.";
                return false;
            }

            if (pageSize > 0 && pageSize > SystemInfo.maxTextureSize)
            {
                errorMessage = $"Page size {pageSize} exceeds maximum texture size {SystemInfo.maxTextureSize}.";
                return false;
            }

            if (padding < 0 || padding > 16)
            {
                errorMessage = $"Padding {padding} is out of valid range (0-16).";
                return false;
            }

            if (enableBleed && padding < 2 && !TextureFormatHelper.RequiresBlockAlignment(targetFormat))
            {
                errorMessage = $"enableBleed requires padding >= 2 to avoid inter-sprite bleed overlap. Current padding: {padding}.";
                return false;
            }

            if (maxPages < 0)
            {
                errorMessage = $"maxPages {maxPages} cannot be negative.";
                return false;
            }

            if (!allowCpuReadPixelsFallback && allowCpuBleedFallback)
            {
                errorMessage = "allowCpuBleedFallback requires allowCpuReadPixelsFallback because CPU bleed depends on CPU-side atlas writes.";
                return false;
            }

            errorMessage = null;
            return true;
        }
    }
}
