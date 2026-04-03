#if UNITY_EDITOR || DEVELOPMENT_BUILD
using System.Diagnostics;
using Cysharp.Threading.Tasks;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Development-only load profiler that detects slow asset loads.
    /// Automatically stripped from release builds via conditional compilation.
    /// Captures wall-clock time from load initiation to Task completion
    /// and logs a warning when it exceeds the configured threshold.
    /// </summary>
    public static class AssetLoadProfiler
    {
        /// <summary>
        /// Loads exceeding this threshold (in milliseconds) will emit a warning log.
        /// Default 100ms (~6 frames at 60fps). Adjust per project as needed.
        /// </summary>
        public static long SlowLoadThresholdMs = 100;

        /// <summary>
        /// Set to false to disable profiling without recompiling.
        /// </summary>
        public static bool Enabled = true;

        /// <summary>
        /// Attaches a fire-and-forget continuation to an async handle.
        /// Measures time from now until handle.Task completes.
        /// Zero allocation on the fast path (non-slow loads only log).
        /// </summary>
        public static void TrackAsync(IOperation handle, string location)
        {
            if (!Enabled || handle == null) return;
            if (handle.IsDone) return; // Already complete (cache hit), skip tracking.
            long startTicks = Stopwatch.GetTimestamp();
            AwaitAndReport(handle, location, startTicks).Forget();
        }

        /// <summary>
        /// For synchronous loads: call before the sync operation.
        /// </summary>
        public static long Begin() => Stopwatch.GetTimestamp();

        /// <summary>
        /// For synchronous loads: call after the sync operation completes.
        /// </summary>
        public static void EndSync(long startTicks, string location)
        {
            if (!Enabled) return;
            long elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000 / Stopwatch.Frequency;
            if (elapsedMs > SlowLoadThresholdMs)
            {
                CLogger.LogWarning($"[AssetLoadProfiler] Slow SYNC load ({elapsedMs}ms): {location}");
            }
        }

        private static async UniTaskVoid AwaitAndReport(IOperation handle, string location, long startTicks)
        {
            try
            {
                await handle.Task;
            }
            catch
            {
                // Swallow — error handling is the caller's responsibility.
                // We still want to report timing for failed loads.
            }

            long elapsedMs = (Stopwatch.GetTimestamp() - startTicks) * 1000 / Stopwatch.Frequency;
            if (elapsedMs > SlowLoadThresholdMs)
            {
                CLogger.LogWarning($"[AssetLoadProfiler] Slow ASYNC load ({elapsedMs}ms): {location}");
            }
        }
    }
}
#endif
