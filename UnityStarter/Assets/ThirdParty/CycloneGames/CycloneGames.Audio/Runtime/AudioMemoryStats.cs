namespace CycloneGames.Audio.Runtime
{
    /// <summary>
    /// Additive, main-thread snapshot for external memory-governance adapters.
    /// Byte values cover decoded clip estimates owned by this module and do not include
    /// native decoder, platform mixer, or external asset-provider memory.
    /// </summary>
    public readonly struct AudioMemoryStats
    {
        public AudioMemoryStats(
            AudioRuntimeStats runtime,
            long externalClipCacheMemoryBytes,
            long externalClipCacheMemoryBudgetBytes,
            long activeBankClipLeaseMemoryBytes,
            long activeBankClipLeaseMemoryBudgetBytes,
            int preloadedBankClipLeaseCount,
            int pendingBankPreloadCount)
        {
            Runtime = runtime;
            ExternalClipCacheMemoryBytes = externalClipCacheMemoryBytes;
            ExternalClipCacheMemoryBudgetBytes = externalClipCacheMemoryBudgetBytes;
            ActiveBankClipLeaseMemoryBytes = activeBankClipLeaseMemoryBytes;
            ActiveBankClipLeaseMemoryBudgetBytes = activeBankClipLeaseMemoryBudgetBytes;
            PreloadedBankClipLeaseCount = preloadedBankClipLeaseCount;
            PendingBankPreloadCount = pendingBankPreloadCount;
        }

        public AudioRuntimeStats Runtime { get; }

        public long ExternalClipCacheMemoryBytes { get; }

        public long ExternalClipCacheMemoryBudgetBytes { get; }

        public long ActiveBankClipLeaseMemoryBytes { get; }

        public long ActiveBankClipLeaseMemoryBudgetBytes { get; }

        public int PreloadedBankClipLeaseCount { get; }

        public int PendingBankPreloadCount { get; }
    }

    /// <summary>Result of one bounded idle-only audio trim call.</summary>
    public readonly struct AudioIdleTrimResult
    {
        public AudioIdleTrimResult(int externalClipCount, int idleSourceCount)
            : this(externalClipCount, externalClipCount, idleSourceCount, idleSourceCount)
        {
        }

        public AudioIdleTrimResult(
            int externalClipScanCount,
            int externalClipCount,
            int idleSourceCount)
            : this(externalClipScanCount, externalClipCount, idleSourceCount, idleSourceCount)
        {
        }

        public AudioIdleTrimResult(
            int externalClipScanCount,
            int externalClipCount,
            int idleSourceScanCount,
            int idleSourceCount)
        {
            ExternalClipScanCount = externalClipScanCount;
            ExternalClipCount = externalClipCount;
            IdleSourceScanCount = idleSourceScanCount;
            IdleSourceCount = idleSourceCount;
        }

        public int ExternalClipScanCount { get; }

        public int ExternalClipCount { get; }

        public int IdleSourceScanCount { get; }

        public int IdleSourceCount { get; }

        public int TotalItemCount => ExternalClipCount + IdleSourceCount;

        public int TotalWorkCount => ExternalClipScanCount + IdleSourceScanCount;
    }
}
