using System;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Bounded cache limits for one package. These limits govern idle entries only; active leases are never evicted.
    /// Byte accounting is an estimate and must not be interpreted as total managed, native, GPU, or bundle memory.
    /// </summary>
    public readonly struct AssetCacheTuning
    {
        private const int MAX_ENTRY_LIMIT = 131_072;
        private const long MINIMUM_BYTE_BUDGET = 1L * 1024 * 1024;

        private readonly byte _configured;

        public readonly int ProbationEntryLimit;
        public readonly int ProtectedEntryLimit;
        public readonly long IdleByteBudget;
        public readonly bool ClearIdleOnLowMemory;

        public AssetCacheTuning(
            int probationEntryLimit,
            int protectedEntryLimit,
            long idleByteBudget,
            bool clearIdleOnLowMemory = true)
        {
            if (probationEntryLimit <= 0 || probationEntryLimit > MAX_ENTRY_LIMIT)
            {
                throw new ArgumentOutOfRangeException(nameof(probationEntryLimit));
            }

            if (protectedEntryLimit <= 0 || protectedEntryLimit > MAX_ENTRY_LIMIT)
            {
                throw new ArgumentOutOfRangeException(nameof(protectedEntryLimit));
            }

            if (idleByteBudget < MINIMUM_BYTE_BUDGET)
            {
                throw new ArgumentOutOfRangeException(nameof(idleByteBudget));
            }

            _configured = 1;
            ProbationEntryLimit = probationEntryLimit;
            ProtectedEntryLimit = protectedEntryLimit;
            IdleByteBudget = idleByteBudget;
            ClearIdleOnLowMemory = clearIdleOnLowMemory;
        }

        public static AssetCacheTuning Automatic => AssetPlatformDefaults.CacheTuning;

        internal AssetCacheTuning Normalized()
        {
            return _configured == 0 ? Automatic : this;
        }
    }
}
