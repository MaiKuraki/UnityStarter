using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Deterministic counters observed after one peak / idle / second-peak pass.
    /// Every field is machine independent; only wall-clock timing varies across machines.
    /// </summary>
    internal readonly struct TrimWorkloadResult
    {
        public TrimWorkloadResult(
            int peakInactive,
            int trimmedInactive,
            int totalCreatedAfterWorkload,
            int totalCreatedDelta)
        {
            PeakInactive = peakInactive;
            TrimmedInactive = trimmedInactive;
            TotalCreatedAfterWorkload = totalCreatedAfterWorkload;
            TotalCreatedDelta = totalCreatedDelta;
        }

        /// <summary>Inactive item count observed right after the first working-set burst is returned.</summary>
        public int PeakInactive { get; }

        /// <summary>Inactive item count observed right after idle maintenance (<c>TrimInactive</c>).</summary>
        public int TrimmedInactive { get; }

        /// <summary>Absolute <c>Diagnostics.TotalCreated</c> after the whole workload.</summary>
        public int TotalCreatedAfterWorkload { get; }

        /// <summary>Items created after the constructor prewarm baseline.</summary>
        public int TotalCreatedDelta { get; }
    }

    /// <summary>
    /// Shared "peak -> idle -> second peak" workload used by both the report-only timing test and the
    /// deterministic characterization test. Keeping one implementation guarantees the reported timing
    /// and the asserted counters describe exactly the same work.
    /// </summary>
    internal static class PoolWorkload
    {
        public const int SoftCapacity = 256;
        public const int PeakActive = 1024;
        public const int TrimTarget = 256;

        // Deterministic expectations for the workload above. The pool is constructed with a SoftCapacity
        // prewarm (256 creations), the first peak borrows PeakActive items so 1024 - 256 are created, and
        // the second peak re-borrows PeakActive after trimming to TrimTarget so another 1024 - 256 are
        // created: 256 + 768 + 768 = 1792 creations, i.e. 1536 more than the constructor prewarm alone.
        public const int ExpectedTotalCreatedAfterWorkload = 1792;
        public const int ExpectedTotalCreatedDelta = 1536;

        public static TrimWorkloadResult ExecuteTrimWorkload()
        {
            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: SoftCapacity, hardCapacity: -1));

            int baselineCreated = factory.CreatedCount; // constructor prewarms SoftCapacity items

            var buffer = new PerfPoolable[PeakActive];

            // First peak: borrow the full working set (forces create-on-demand above the prewarm floor).
            for (int i = 0; i < PeakActive; i++)
            {
                buffer[i] = pool.Spawn(i);
            }

            for (int i = 0; i < PeakActive; i++)
            {
                pool.Despawn(buffer[i]);
            }

            int peakInactive = pool.CountInactive;

            // Idle maintenance: release every surplus inactive item.
            pool.TrimInactive(TrimTarget);
            int trimmedInactive = pool.CountInactive;

            // Second peak: borrow again to exercise create-on-demand after trimming.
            for (int i = 0; i < PeakActive; i++)
            {
                buffer[i] = pool.Spawn(i);
            }

            for (int i = 0; i < PeakActive; i++)
            {
                pool.Despawn(buffer[i]);
            }

            int totalCreatedAfterWorkload = (int)pool.Diagnostics.TotalCreated;
            return new TrimWorkloadResult(
                peakInactive,
                trimmedInactive,
                totalCreatedAfterWorkload,
                totalCreatedAfterWorkload - baselineCreated);
        }
    }
}
