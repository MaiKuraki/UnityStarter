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
}
