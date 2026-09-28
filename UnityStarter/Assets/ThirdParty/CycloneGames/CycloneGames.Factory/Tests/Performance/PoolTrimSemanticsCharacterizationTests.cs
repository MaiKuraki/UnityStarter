using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Characterization ("lock the current behavior") tests for trim eviction ordering and the
    /// deterministic create counters of the peak/idle/second-peak workload.
    ///
    /// IMPORTANT: The trim tests below pin the CURRENT MRU eviction semantics. <c>_inactiveItems</c> is a
    /// stack: spawn, reuse and trim all pop the tail, and a returning item is pushed on the tail, so the
    /// MOST RECENTLY returned item is the first to be evicted (the OLDEST returned item is retained).
    /// When LRU eviction is adopted these assertions MUST be rewritten explicitly to keep the two most
    /// recently returned items instead of failing silently.
    /// </summary>
    public sealed class PoolTrimSemanticsCharacterizationTests
    {
        [Test]
        public void ManualTrim_EvictsMostRecentlyReturnedItems_KeepingOldestReturned()
        {
            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 64));

            PerfPoolable oldest = pool.Spawn(1);   // Id 0
            PerfPoolable second = pool.Spawn(2);   // Id 1
            PerfPoolable third = pool.Spawn(3);    // Id 2
            PerfPoolable newest = pool.Spawn(4);   // Id 3

            // Return in creation order: `oldest` is the first returned, `newest` the last returned.
            pool.Despawn(oldest);
            pool.Despawn(second);
            pool.Despawn(third);
            pool.Despawn(newest);

            Assert.That(pool.CountInactive, Is.EqualTo(4));

            // CHARACTERIZATION: current MRU eviction. Rewrite for LRU when that option is adopted.
            pool.TrimInactive(2);

            Assert.That(pool.CountInactive, Is.EqualTo(2));
            Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(2));

            // Observe which instances survived by spawning them back out (spawn also pops the tail).
            int[] survivors = new int[2];
            for (int i = 0; i < survivors.Length; i++)
            {
                PerfPoolable item = pool.Spawn(0);
                survivors[i] = item.Id;
            }

            Assert.That(survivors, Is.EquivalentTo(new[] { oldest.Id, second.Id }),
                "Current trim semantics retain the oldest returned items (MRU eviction).");
        }

        [Test]
        public void TrimOnDespawn_KeepsOldestReturnedItems()
        {
            // With TrimOnDespawn the just-returned (newest) item is destroyed as soon as the inactive
            // count reaches soft capacity, so the single retained item is the earliest return.
            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(
                    softCapacity: 1,
                    hardCapacity: 8,
                    trimPolicy: PoolTrimPolicy.TrimOnDespawn));

            Assert.That(pool.CountInactive, Is.EqualTo(1)); // constructor prewarms to soft capacity

            PerfPoolable firstReturned = pool.Spawn(1);   // takes the prewarmed item, Id 0
            PerfPoolable secondReturned = pool.Spawn(2);  // Id 1
            PerfPoolable thirdReturned = pool.Spawn(3);   // Id 2

            pool.Despawn(firstReturned);
            pool.Despawn(secondReturned);
            pool.Despawn(thirdReturned);

            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(2));

            // CHARACTERIZATION: the survivor is the first returned item, not the last.
            PerfPoolable survivor = pool.Spawn(0);
            Assert.That(survivor.Id, Is.EqualTo(firstReturned.Id));
        }

        [Test]
        public void PeakIdleSecondPeak_CreatedCounters_AreDeterministic()
        {
            // Deterministic create accounting for the shared workload. This is the baseline number to
            // diff after the trim change: with a fixed destroyed count the workload should not create
            // fewer/more items regardless of MRU vs LRU; ordering changes, the total does not.
            TrimWorkloadResult result = PoolWorkload.ExecuteTrimWorkload();

            Assert.That(result.PeakInactive, Is.EqualTo(PoolWorkload.PeakActive));
            Assert.That(result.TrimmedInactive, Is.EqualTo(PoolWorkload.TrimTarget));
            Assert.That(result.TotalCreatedAfterWorkload, Is.EqualTo(PoolWorkload.ExpectedTotalCreatedAfterWorkload));
            Assert.That(result.TotalCreatedDelta, Is.EqualTo(PoolWorkload.ExpectedTotalCreatedDelta));
        }
    }
}
