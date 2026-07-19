using System;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Platform-aware default tuning values for the asset system. Centralizes the trade-off between
    /// throughput (desktop/console) and stability (mobile/WebGL), so leaving knobs unset still yields
    /// safe behavior on constrained devices instead of unbounded concurrency.
    /// </summary>
    public static class AssetPlatformDefaults
    {
        /// <summary>
        /// Recommended maximum number of bundles loaded concurrently. Unbounded concurrency on mobile
        /// causes IO thrash and memory spikes; this caps it to a sane, device-appropriate value.
        /// </summary>
        public static int BundleLoadingMaxConcurrency
        {
            get
            {
#if UNITY_WEBGL && !UNITY_EDITOR
                return 2;
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                return SystemInfo.systemMemorySize >= 6144 ? 6 : 4;
#else
                int cores = Math.Max(1, SystemInfo.processorCount);
                return Math.Clamp(cores, 4, 16);
#endif
            }
        }

        /// <summary>
        /// Conservative idle-cache fallback selected from platform and reported physical memory. Product profiles
        /// should override this value when measured content traces or platform SDK budgets are available.
        /// </summary>
        public static AssetCacheTuning CacheTuning
        {
            get
            {
                int memoryMb = Math.Max(0, SystemInfo.systemMemorySize);

#if UNITY_WEBGL && !UNITY_EDITOR
                return new AssetCacheTuning(16, 96, 64L * 1024 * 1024);
#elif UNITY_SERVER && !UNITY_EDITOR
                return memoryMb >= 8192
                    ? new AssetCacheTuning(32, 256, 256L * 1024 * 1024)
                    : new AssetCacheTuning(16, 128, 96L * 1024 * 1024);
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                if (memoryMb >= 6144)
                {
                    return new AssetCacheTuning(48, 384, 256L * 1024 * 1024);
                }

                return memoryMb >= 3072
                    ? new AssetCacheTuning(32, 256, 128L * 1024 * 1024)
                    : new AssetCacheTuning(16, 128, 64L * 1024 * 1024);
#else
                if (memoryMb >= 16384)
                {
                    return new AssetCacheTuning(96, 768, 768L * 1024 * 1024);
                }

                return memoryMb >= 8192
                    ? new AssetCacheTuning(64, 512, 512L * 1024 * 1024)
                    : new AssetCacheTuning(32, 256, 192L * 1024 * 1024);
#endif
            }
        }
    }
}
