using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Result of one owner-thread idle-cache maintenance step.
    /// Work is measured in idle handles removed from cache ownership.
    /// </summary>
    public readonly struct AssetCacheTrimResult
    {
        public AssetCacheTrimResult(
            int workConsumed,
            int evictedCount,
            long releasedBytesApprox,
            int remainingIdleCount)
        {
            if (workConsumed < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(workConsumed));
            }

            if (evictedCount < 0 || evictedCount > workConsumed)
            {
                throw new ArgumentOutOfRangeException(nameof(evictedCount));
            }

            if (releasedBytesApprox < 0L)
            {
                throw new ArgumentOutOfRangeException(nameof(releasedBytesApprox));
            }

            if (remainingIdleCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(remainingIdleCount));
            }

            WorkConsumed = workConsumed;
            EvictedCount = evictedCount;
            ReleasedBytesApprox = releasedBytesApprox;
            RemainingIdleCount = remainingIdleCount;
        }

        public int WorkConsumed { get; }

        public int EvictedCount { get; }

        public long ReleasedBytesApprox { get; }

        public int RemainingIdleCount { get; }

        public bool HasMoreIdleEntries => RemainingIdleCount > 0;
    }

    /// <summary>
    /// Optional capability for an explicitly owned asset package that supports allocation-free diagnostics and
    /// bounded idle-cache maintenance. Implementations are main-thread-affine and never release active leases.
    /// </summary>
    public interface IAssetCacheMaintenanceOwner : IAssetRuntimeDiagnostics
    {
        AssetCacheTrimResult TrimIdleCacheStep(int maxWork);
    }

    /// <summary>
    /// Optional capability for driving bounded, best-effort retries of provider release failures that are parked
    /// in the owning package's cache. This exists so a periodic maintenance driver (for example
    /// <see cref="CacheRetention.AssetCacheRetentionScheduler"/>) can drain the retry queue even when no other
    /// cache operation runs. Implementations are main-thread-affine, bounded by <paramref name="maxWork"/>, and
    /// never throw for recoverable failures; fatal exceptions (out-of-memory, access violation) still propagate.
    /// </summary>
    public interface IAssetReleaseRetryDriver
    {
        /// <summary>
        /// Retries at most <paramref name="maxWork"/> parked provider-release failures.
        /// Returns the number of release failures still parked after this pass.
        /// </summary>
        int RetryPendingReleaseFailures(int maxWork);
    }
}
