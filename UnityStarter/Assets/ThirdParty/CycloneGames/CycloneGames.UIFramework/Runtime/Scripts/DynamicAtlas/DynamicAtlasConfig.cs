using System;
using UnityEngine;

namespace CycloneGames.UIFramework.DynamicAtlas
{
    [Serializable]
    public sealed class DynamicAtlasConfig
    {
        public enum PlatformTier
        {
            DesktopHighEnd = 0,
            MobileHighEnd = 1,
            MobileLowEnd = 2,
            WebGL = 3,
        }

        public const int MinimumPageSize = 64;
        public const int MaximumPageSize = 8192;
        public const int MaximumPageCount = 64;
        public const int MaximumEntryCount = 65535;
        public const int MaximumKeyLength = 1024;

        [Min(MinimumPageSize)]
        [Tooltip("Power-of-two atlas page size in pixels.")]
        public int pageSize = 1024;

        [Range(1, MaximumPageCount)]
        [Tooltip("Hard page-count limit. Runtime allocation never exceeds this value or the memory budget.")]
        public int maxPages = 2;

        [Range(0, MaximumPageCount)]
        [Tooltip("Empty pages retained to absorb predictable UI churn without reallocating textures.")]
        public int minRetainedPages;

        [Min(1)]
        [Tooltip("Hard limit for all active and retained atlas entries.")]
        public int maxEntries = 512;

        [Min(1)]
        [Tooltip("Hard limit for entries stored on one page.")]
        public int maxEntriesPerPage = 384;

        [Min(1)]
        [Tooltip("Maximum accepted cache-key length.")]
        public int maxKeyLength = 256;

        [Min(1)]
        [Tooltip("Combined estimated CPU and GPU texture-memory budget in bytes.")]
        public long memoryBudgetBytes = 16L * 1024L * 1024L;

        [Range(0, 8)]
        [Tooltip("Transparent separation on every side of an entry, in pixels.")]
        public int padding = 2;

        [Tooltip("Copies a one-pixel edge gutter into padding to prevent bilinear sampling artifacts.")]
        public bool enableBleed = true;

        [Tooltip("Filtering used by generated atlas pages.")]
        public FilterMode filterMode = FilterMode.Bilinear;

        [Tooltip("Controls whether zero-reference entries are removed immediately or retained within hard limits.")]
        public DynamicAtlasRetentionPolicy retentionPolicy = DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure;

        [Tooltip("Controls whether oversized inputs are rejected or explicitly downscaled.")]
        public DynamicAtlasOversizePolicy oversizePolicy = DynamicAtlasOversizePolicy.Reject;

        [Tooltip("Permission boundary used after the preferred direct GPU copy path is unavailable. CPU raw copy still requires a readable, format-compatible source.")]
        public DynamicAtlasCopyFallback copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy;

        [Min(0.01f)]
        [Tooltip("Pixels-per-unit used for sprites created from textures or regions.")]
        public float defaultPixelsPerUnit = 100f;

        [NonSerialized]
        public Func<string, Texture2D> loadFunc;

        [NonSerialized]
        public Action<string, Texture2D> unloadFunc;

        public static DynamicAtlasConfig CreateForTier(
            PlatformTier tier,
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null)
        {
            var config = new DynamicAtlasConfig
            {
                loadFunc = loadFunc,
                unloadFunc = unloadFunc,
            };

            switch (tier)
            {
                case PlatformTier.DesktopHighEnd:
                    config.pageSize = 2048;
                    config.maxPages = 4;
                    config.minRetainedPages = 1;
                    config.maxEntries = 4096;
                    config.maxEntriesPerPage = 2048;
                    config.memoryBudgetBytes = 64L * 1024L * 1024L;
                    config.copyFallback = DynamicAtlasCopyFallback.GpuOnly;
                    break;

                case PlatformTier.MobileHighEnd:
                    config.pageSize = 2048;
                    config.maxPages = 2;
                    config.minRetainedPages = 1;
                    config.maxEntries = 2048;
                    config.maxEntriesPerPage = 1024;
                    config.memoryBudgetBytes = 32L * 1024L * 1024L;
                    config.copyFallback = DynamicAtlasCopyFallback.GpuOnly;
                    break;

                case PlatformTier.MobileLowEnd:
                    config.pageSize = 1024;
                    config.maxPages = 2;
                    config.minRetainedPages = 0;
                    config.maxEntries = 768;
                    config.maxEntriesPerPage = 512;
                    config.memoryBudgetBytes = 16L * 1024L * 1024L;
                    config.copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy;
                    break;

                case PlatformTier.WebGL:
                    config.pageSize = 1024;
                    config.maxPages = 2;
                    config.minRetainedPages = 0;
                    config.maxEntries = 512;
                    config.maxEntriesPerPage = 384;
                    config.memoryBudgetBytes = 20L * 1024L * 1024L;
                    config.copyFallback = DynamicAtlasCopyFallback.AllowCpuRawCopy;
                    break;

                default:
                    throw new ArgumentOutOfRangeException(nameof(tier), tier, "Unknown dynamic atlas platform tier.");
            }

            return config;
        }

        public static DynamicAtlasConfig CreateForCurrentPlatform(
            bool preferLowMemoryProfile = false,
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null)
        {
#if UNITY_WEBGL
            return CreateForTier(PlatformTier.WebGL, loadFunc, unloadFunc);
#elif UNITY_ANDROID || UNITY_IOS || UNITY_TVOS
            return CreateForTier(
                preferLowMemoryProfile ? PlatformTier.MobileLowEnd : PlatformTier.MobileHighEnd,
                loadFunc,
                unloadFunc);
#elif UNITY_STANDALONE
            return CreateForTier(
                preferLowMemoryProfile ? PlatformTier.MobileHighEnd : PlatformTier.DesktopHighEnd,
                loadFunc,
                unloadFunc);
#else
            // Unknown and future platforms start from the bounded compatibility baseline.
            // Products should replace it with an explicitly measured platform profile.
            return new DynamicAtlasConfig
            {
                loadFunc = loadFunc,
                unloadFunc = unloadFunc,
            };
#endif
        }

        public static DynamicAtlasConfig CreatePlatformOptimized(
            Func<string, Texture2D> loadFunc = null,
            Action<string, Texture2D> unloadFunc = null)
        {
            return CreateForCurrentPlatform(loadFunc: loadFunc, unloadFunc: unloadFunc);
        }

        public bool Validate(out string errorMessage)
        {
            if (pageSize < MinimumPageSize || pageSize > MaximumPageSize || !Mathf.IsPowerOfTwo(pageSize))
            {
                errorMessage = $"Page size must be a power of two in [{MinimumPageSize}, {MaximumPageSize}].";
                return false;
            }

            if (maxPages < 1 || maxPages > MaximumPageCount)
            {
                errorMessage = $"Max pages must be in [1, {MaximumPageCount}].";
                return false;
            }

            if (minRetainedPages < 0 || minRetainedPages > maxPages)
            {
                errorMessage = "Minimum retained pages must be in [0, maxPages].";
                return false;
            }

            if (maxEntries < 1 || maxEntries > MaximumEntryCount)
            {
                errorMessage = $"Max entries must be in [1, {MaximumEntryCount}].";
                return false;
            }

            if (maxEntriesPerPage < 1 || maxEntriesPerPage > maxEntries)
            {
                errorMessage = "Max entries per page must be in [1, maxEntries].";
                return false;
            }

            if (maxKeyLength < 1 || maxKeyLength > MaximumKeyLength)
            {
                errorMessage = $"Max key length must be in [1, {MaximumKeyLength}].";
                return false;
            }

            if (padding < 0 || padding > 8)
            {
                errorMessage = "Padding must be in [0, 8].";
                return false;
            }

            if (enableBleed && padding < 1)
            {
                errorMessage = "Edge bleed requires at least one pixel of padding.";
                return false;
            }

            if (filterMode != FilterMode.Point &&
                filterMode != FilterMode.Bilinear &&
                filterMode != FilterMode.Trilinear)
            {
                errorMessage = "Filter mode is not supported.";
                return false;
            }

            if (retentionPolicy != DynamicAtlasRetentionPolicy.RemoveWhenUnused &&
                retentionPolicy != DynamicAtlasRetentionPolicy.RetainUntilCapacityPressure)
            {
                errorMessage = "Retention policy is not supported.";
                return false;
            }

            if (oversizePolicy != DynamicAtlasOversizePolicy.Reject &&
                oversizePolicy != DynamicAtlasOversizePolicy.ScaleDown)
            {
                errorMessage = "Oversize policy is not supported.";
                return false;
            }

            if (copyFallback != DynamicAtlasCopyFallback.GpuOnly &&
                copyFallback != DynamicAtlasCopyFallback.AllowSynchronousReadback &&
                copyFallback != DynamicAtlasCopyFallback.AllowCpuRawCopy)
            {
                errorMessage = "Copy fallback is not supported.";
                return false;
            }

            if (float.IsNaN(defaultPixelsPerUnit) || float.IsInfinity(defaultPixelsPerUnit) || defaultPixelsPerUnit <= 0f)
            {
                errorMessage = "Default pixels-per-unit must be finite and greater than zero.";
                return false;
            }

            if (memoryBudgetBytes < TextureFormatHelper.EstimatePageBytes(pageSize, copyFallback))
            {
                errorMessage = "Memory budget must be large enough for at least one atlas page.";
                return false;
            }

            if (oversizePolicy == DynamicAtlasOversizePolicy.ScaleDown &&
                copyFallback != DynamicAtlasCopyFallback.AllowSynchronousReadback)
            {
                errorMessage = "ScaleDown requires the synchronous readback fallback.";
                return false;
            }

            if ((loadFunc == null) != (unloadFunc == null))
            {
                errorMessage = "A custom synchronous loader and unloader must be configured as an ownership pair.";
                return false;
            }

            errorMessage = null;
            return true;
        }

        internal DynamicAtlasConfig Copy()
        {
            return (DynamicAtlasConfig)MemberwiseClone();
        }

        internal bool IsEquivalentTo(DynamicAtlasConfig other)
        {
            return other != null &&
                   pageSize == other.pageSize &&
                   maxPages == other.maxPages &&
                   minRetainedPages == other.minRetainedPages &&
                   maxEntries == other.maxEntries &&
                   maxEntriesPerPage == other.maxEntriesPerPage &&
                   maxKeyLength == other.maxKeyLength &&
                   memoryBudgetBytes == other.memoryBudgetBytes &&
                   padding == other.padding &&
                   enableBleed == other.enableBleed &&
                   filterMode == other.filterMode &&
                   retentionPolicy == other.retentionPolicy &&
                   oversizePolicy == other.oversizePolicy &&
                   copyFallback == other.copyFallback &&
                   Mathf.Approximately(defaultPixelsPerUnit, other.defaultPixelsPerUnit) &&
                   loadFunc == other.loadFunc &&
                   unloadFunc == other.unloadFunc;
        }
    }
}
