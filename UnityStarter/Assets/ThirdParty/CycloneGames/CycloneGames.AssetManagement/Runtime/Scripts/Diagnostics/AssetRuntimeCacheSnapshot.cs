namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Allocation-free runtime cache counters for telemetry, stress HUDs, and memory governance.
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

        public AssetRuntimeCacheSnapshot(
            string packageName,
            string providerName,
            int activeCount,
            int idleCount,
            long idleBytesApprox,
            long idleBytesBudget)
        {
            PackageName = packageName;
            ProviderName = providerName;
            ActiveCount = activeCount;
            IdleCount = idleCount;
            IdleBytesApprox = idleBytesApprox;
            IdleBytesBudget = idleBytesBudget;
        }

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
