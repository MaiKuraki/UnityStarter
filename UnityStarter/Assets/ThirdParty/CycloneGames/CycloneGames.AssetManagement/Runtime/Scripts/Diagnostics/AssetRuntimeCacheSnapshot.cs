namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Allocation-free runtime cache counters for telemetry, stress HUDs, and memory governance.
    /// Activity, rejection, eviction, and peak values are cumulative for the lifetime of the owning cache.
    /// The snapshot intentionally exposes aggregate counters only; per-entry inspection remains an Editor diagnostic path.
    /// </summary>
    public readonly struct AssetRuntimeCacheSnapshot
    {
        public readonly string PackageName;
        public readonly string ProviderName;
        public readonly int ActiveCount;
        public readonly int IdleCount;
        public readonly long IdleBytesApprox;
        public readonly long IdleBytesBudget;

        public readonly long ActiveHitCount;
        public readonly long IdleHitCount;
        public readonly long CacheMissCount;
        public readonly long IdleAdmissionCount;
        public readonly long FailedOperationRejectionCount;
        public readonly long MetadataOverflowRejectionCount;
        public readonly long UnknownFootprintRejectionCount;
        public readonly long OversizeRejectionCount;
        public readonly long FootprintEstimationFailureCount;
        public readonly long EvictionCount;
        public readonly long CapacityEvictionCount;
        public readonly long MemoryBudgetEvictionCount;
        public readonly long RetentionEvictionCount;
        public readonly long ExplicitEvictionCount;
        public readonly long EvictedBytesApprox;
        public readonly long ProviderReleaseFailureCount;
        public readonly int PeakActiveCount;
        public readonly int PeakIdleCount;
        public readonly long PeakIdleBytesApprox;

        public AssetRuntimeCacheSnapshot(
            string packageName,
            string providerName,
            int activeCount,
            int idleCount,
            long idleBytesApprox,
            long idleBytesBudget)
        {
            this = default;
            PackageName = packageName;
            ProviderName = providerName;
            ActiveCount = activeCount;
            IdleCount = idleCount;
            IdleBytesApprox = idleBytesApprox;
            IdleBytesBudget = idleBytesBudget;
        }

        internal AssetRuntimeCacheSnapshot(
            string packageName,
            string providerName,
            int activeCount,
            int idleCount,
            long idleBytesApprox,
            long idleBytesBudget,
            long activeHitCount = 0L,
            long idleHitCount = 0L,
            long cacheMissCount = 0L,
            long idleAdmissionCount = 0L,
            long failedOperationRejectionCount = 0L,
            long metadataOverflowRejectionCount = 0L,
            long unknownFootprintRejectionCount = 0L,
            long oversizeRejectionCount = 0L,
            long footprintEstimationFailureCount = 0L,
            long evictionCount = 0L,
            long capacityEvictionCount = 0L,
            long memoryBudgetEvictionCount = 0L,
            long retentionEvictionCount = 0L,
            long explicitEvictionCount = 0L,
            long evictedBytesApprox = 0L,
            long providerReleaseFailureCount = 0L,
            int peakActiveCount = 0,
            int peakIdleCount = 0,
            long peakIdleBytesApprox = 0L)
            : this(
                packageName,
                providerName,
                activeCount,
                idleCount,
                idleBytesApprox,
                idleBytesBudget)
        {
            ActiveHitCount = activeHitCount;
            IdleHitCount = idleHitCount;
            CacheMissCount = cacheMissCount;
            IdleAdmissionCount = idleAdmissionCount;
            FailedOperationRejectionCount = failedOperationRejectionCount;
            MetadataOverflowRejectionCount = metadataOverflowRejectionCount;
            UnknownFootprintRejectionCount = unknownFootprintRejectionCount;
            OversizeRejectionCount = oversizeRejectionCount;
            FootprintEstimationFailureCount = footprintEstimationFailureCount;
            EvictionCount = evictionCount;
            CapacityEvictionCount = capacityEvictionCount;
            MemoryBudgetEvictionCount = memoryBudgetEvictionCount;
            RetentionEvictionCount = retentionEvictionCount;
            ExplicitEvictionCount = explicitEvictionCount;
            EvictedBytesApprox = evictedBytesApprox;
            ProviderReleaseFailureCount = providerReleaseFailureCount;
            PeakActiveCount = peakActiveCount;
            PeakIdleCount = peakIdleCount;
            PeakIdleBytesApprox = peakIdleBytesApprox;
        }

        public long CacheHitCount => ActiveHitCount + IdleHitCount;

        public long CacheLookupCount => CacheHitCount + CacheMissCount;

        public long AdmissionRejectionCount =>
            FailedOperationRejectionCount +
            MetadataOverflowRejectionCount +
            UnknownFootprintRejectionCount +
            OversizeRejectionCount;

        public double CacheHitRatio => CacheLookupCount <= 0L
            ? 0d
            : CacheHitCount / (double)CacheLookupCount;

        public float IdleBudgetUsage
        {
            get
            {
                return IdleBytesBudget <= 0L ? 0f : (float)IdleBytesApprox / IdleBytesBudget;
            }
        }

        public bool IsIdleBudgetExceeded => IdleBytesBudget > 0L && IdleBytesApprox > IdleBytesBudget;
    }
}
