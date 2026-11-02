using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Deterministic zero-allocation gates. These are the only throughput-adjacent assertions allowed to
    /// run in CI: managed bytes allocated on the current thread is a hard, machine-independent signal,
    /// unlike wall-clock time.
    /// <para>
    /// Each scenario runs <see cref="ROUNDS"/> independent rounds and every round must report exactly
    /// zero. A single round can be polluted by one-off work happening on the main thread (editor tick,
    /// lazy static initialization, list growth in an unrelated subsystem); requiring all rounds to be
    /// clean converts a flaky signal into a hard one and keeps a failure diagnosable via the per-round
    /// output.
    /// </para>
    /// <para>
    /// The byte counter is verified by <see cref="AllocProbe"/> first. Hosts where the counter is dead
    /// (Unity 2022.3 Editor on Mono) cannot support a byte-level conclusion, so those runs assert the
    /// GC-independent invariant instead — <see cref="PoolBase{T}.TrackingCapacity"/> must not grow,
    /// which proves no tracking container reallocated — and then report as skipped rather than passed.
    /// </para>
    /// </summary>
    public sealed class PoolAllocationGateTests
    {
        private const int ROUNDS = 5;
        private const int ITERATIONS = 4096;

        [Test]
        public void ObjectPool_SteadyStateSpawnDespawn_AfterPrewarm_AllocatesNothing()
        {
            const int prewarm = 1024;

            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: prewarm, hardCapacity: -1));

            PerfPoolable warmup = pool.Spawn(0);
            pool.Despawn(warmup);

            RunSteadyState(pool, "ObjectPool steady state after prewarm");
        }

        [Test]
        public void ObjectPool_SteadyStateSpawnDespawn_AfterTrimInactive_AllocatesNothing()
        {
            const int prewarm = 2048;
            const int trimTarget = 512;

            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: prewarm, hardCapacity: -1));

            // Idle maintenance destroys the surplus inactive items; the remaining ones must still be
            // recycled without allocating on the hot path.
            pool.TrimInactive(trimTarget);
            Assert.That(pool.CountInactive, Is.EqualTo(trimTarget));

            PerfPoolable warmup = pool.Spawn(0);
            pool.Despawn(warmup);

            RunSteadyState(pool, "ObjectPool steady state after trim");
        }

        [Test]
        public void ObjectPool_BatchedBorrowReturn_SteadyState_AllocatesNothing()
        {
            const int prewarm = 256;
            const int batch = 256;
            const int cycles = 32;

            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: prewarm, hardCapacity: -1));

            var buffer = new PerfPoolable[batch];

            // Warm-up cycle so active-list and index capacities are already sized.
            for (int i = 0; i < batch; i++)
            {
                buffer[i] = pool.Spawn(i);
            }

            for (int i = 0; i < batch; i++)
            {
                pool.Despawn(buffer[i]);
            }

            int reserved = pool.TrackingCapacity;

            void Workload()
            {
                for (int cycle = 0; cycle < cycles; cycle++)
                {
                    for (int i = 0; i < batch; i++)
                    {
                        buffer[i] = pool.Spawn(i);
                    }

                    for (int i = 0; i < batch; i++)
                    {
                        pool.Despawn(buffer[i]);
                    }
                }
            }

            AssertZeroAllocation(pool, reserved, Workload, "Batched borrow/return steady state");
        }

        [Test]
        public void FastObjectPool_SteadyStateSpawnDespawn_AllocatesNothing()
        {
            const int prewarm = 1024;

            using var pool = new PerfFastObjectPool(
                new PoolCapacitySettings(softCapacity: prewarm, hardCapacity: -1));

            PerfFastPoolable warmup = pool.Spawn();
            pool.Despawn(warmup);

            RunSteadyState(pool, "FastObjectPool steady state");
        }

        /// <summary>
        /// Steady-state loop over a single prewarmed item: spawn and despawn repeatedly. Any growth of a
        /// tracking container shows up as a reserved-capacity change even on hosts where the byte counter
        /// is unusable.
        /// </summary>
        private static void RunSteadyState<T>(PoolBase<T> pool, string scenario) where T : class
        {
            int reserved = pool.TrackingCapacity;

            void Workload()
            {
                for (int i = 0; i < ITERATIONS; i++)
                {
                    T item = SpawnOne(pool, i);
                    pool.Despawn(item);
                }
            }

            AssertZeroAllocation(pool, reserved, Workload, scenario);
        }

        private static T SpawnOne<T>(PoolBase<T> pool, int i) where T : class
        {
            switch (pool)
            {
                case IMemoryPool<T> parameterless:
                    return parameterless.Spawn();
                case IMemoryPool<int, T> withParam:
                    return withParam.Spawn(i);
                default:
                    throw new InvalidOperationException("Unsupported pool shape for the allocation gate.");
            }
        }

        private static void AssertZeroAllocation<T>(PoolBase<T> pool, int reserved, Action workload, string scenario)
            where T : class
        {
            if (!AllocProbe.IsMeasurable())
            {
                workload();

                Assert.That(pool.TrackingCapacity, Is.EqualTo(reserved),
                    $"{scenario}: a tracking container grew, so the hot path allocated.");
                Assert.That(pool.CountActive, Is.Zero, $"{scenario}: the workload must end with no active lease.");

                AllocProbe.NoteUnmeasurable(scenario);
                AllocProbe.SkipUnmeasurable(scenario);
            }

            long[] rounds = new long[ROUNDS];
            for (int round = 0; round < ROUNDS; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                workload();
                rounds[round] = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine($"round {round}: {rounds[round]} bytes");
            }

            Assert.That(pool.TrackingCapacity, Is.EqualTo(reserved),
                $"{scenario}: a tracking container grew, so the hot path allocated.");

            for (int round = 0; round < rounds.Length; round++)
            {
                Assert.That(rounds[round], Is.EqualTo(0),
                    $"{scenario}: round {round} of {rounds.Length} allocated {rounds[round]} managed bytes.");
            }
        }
    }
}
