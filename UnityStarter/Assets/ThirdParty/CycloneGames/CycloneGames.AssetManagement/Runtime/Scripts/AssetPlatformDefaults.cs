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
                // Single-threaded; keep IO parallelism minimal to avoid stalling the browser frame loop.
                return 4;
#elif (UNITY_ANDROID || UNITY_IOS) && !UNITY_EDITOR
                // Mobile storage is slow and memory is tight; bound concurrency to avoid spikes.
                return 8;
#else
                int cores = Math.Max(1, SystemInfo.processorCount);
                return Math.Clamp(cores * 2, 8, 32);
#endif
            }
        }
    }
}
