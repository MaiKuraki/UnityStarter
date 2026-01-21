using System;
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

        [Tooltip("Enable platform-specific optimizations (NativeArray, unsafe code, etc.)")]
        public bool enablePlatformOptimizations = true;

        [Tooltip("Custom texture loader (null = Resources.Load)")]
        public Func<string, Texture2D> loadFunc;

        [Tooltip("Custom texture unloader (null = Resources.UnloadAsset)")]
        public Action<string, Texture2D> unloadFunc;

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

            errorMessage = null;
            return true;
        }
    }
}