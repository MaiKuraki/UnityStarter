using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Deterministic zero-allocation gates. These are the only throughput-adjacent assertions allowed
    /// to run in CI: managed bytes allocated on the current thread is a hard, machine-independent
    /// signal, unlike wall-clock time.
    /// <para>
    /// Each scenario runs <see cref="ROUNDS"/> independent rounds and every round must report exactly
    /// zero. A single round can be polluted by one-off work happening on the main thread (editor tick,
    /// lazy static initialization, list growth in an unrelated subsystem); requiring all rounds to be
    /// clean converts a flaky signal into a hard one and keeps a failure diagnosable via the per-round
    /// output. <see cref="GC.GetAllocatedBytesForCurrentThread"/> is thread-local, so the measured
    /// region must stay on the calling (main) thread and must not log.
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

            long[] rounds = new long[ROUNDS];
            for (int round = 0; round < ROUNDS; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < ITERATIONS; i++)
                {
                    PerfPoolable item = pool.Spawn(i);
                    pool.Despawn(item);
                }

                rounds[round] = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine($"round {round}: {rounds[round]} bytes");
            }

            AssertAllZero(rounds, "Steady-state spawn/despawn after prewarm");
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

            long[] rounds = new long[ROUNDS];
            for (int round = 0; round < ROUNDS; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < ITERATIONS; i++)
                {
                    PerfPoolable item = pool.Spawn(i);
                    pool.Despawn(item);
                }

                rounds[round] = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine($"round {round}: {rounds[round]} bytes");
            }

            AssertAllZero(rounds, "Steady-state spawn/despawn after TrimInactive");
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

            // Warm-up cycle so active-list and dictionary capacities are already sized.
            for (int i = 0; i < batch; i++)
            {
                buffer[i] = pool.Spawn(i);
            }

            for (int i = 0; i < batch; i++)
            {
                pool.Despawn(buffer[i]);
            }

            long[] rounds = new long[ROUNDS];
            for (int round = 0; round < ROUNDS; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
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

                rounds[round] = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine($"round {round}: {rounds[round]} bytes");
            }

            AssertAllZero(rounds, "Batched borrow/return steady state");
        }

        [Test]
        public void FastObjectPool_SteadyStateSpawnDespawn_AllocatesNothing()
        {
            const int prewarm = 1024;

            using var pool = new PerfFastObjectPool(
                new PoolCapacitySettings(softCapacity: prewarm, hardCapacity: -1));

            PerfFastPoolable warmup = pool.Spawn();
            pool.Despawn(warmup);

            long[] rounds = new long[ROUNDS];
            for (int round = 0; round < ROUNDS; round++)
            {
                long before = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < ITERATIONS; i++)
                {
                    PerfFastPoolable item = pool.Spawn();
                    pool.Despawn(item);
                }

                rounds[round] = GC.GetAllocatedBytesForCurrentThread() - before;
                TestContext.WriteLine($"round {round}: {rounds[round]} bytes");
            }

            AssertAllZero(rounds, "FastObjectPool steady-state spawn/despawn");
        }

        private static void AssertAllZero(long[] rounds, string scenario)
        {
            for (int round = 0; round < rounds.Length; round++)
            {
                Assert.That(rounds[round], Is.EqualTo(0),
                    $"{scenario}: round {round} of {rounds.Length} allocated {rounds[round]} managed bytes.");
            }
        }
    }
}
