using CycloneGames.Factory.Runtime;
using NUnit.Framework;
using Unity.PerformanceTesting;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Report-only steady-state throughput baselines for the spawn/despawn hot loop.
    /// These tests make no assertions: wall-clock median and spread are emitted so two builds can be
    /// compared on the same machine, and are never used as a gate.
    /// </summary>
    public sealed class PoolSpawnDespawnThroughputTests
    {
        private const int PREWARM_COUNT = 1024;
        private const int ITERATIONS_PER_MEASUREMENT = 4096;
        private const int WARMUP_COUNT = 5;
        private const int MEASUREMENT_COUNT = 15;

        private static int _intSink;
        private static bool _boolSink;

        [Test, Performance]
        public void ObjectPool_SpawnDespawn_SteadyState()
        {
            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: PREWARM_COUNT, hardCapacity: -1));

            // One activate/deactivate cycle before measuring: warms JIT and the list/dictionary capacity.
            PerfPoolable warmup = pool.Spawn(0);
            pool.Despawn(warmup);

            Measure.Method(() => ObjectPoolStep(pool))
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        [Test, Performance]
        public void FastObjectPool_SpawnDespawn_SteadyState()
        {
            using var pool = new PerfFastObjectPool(
                new PoolCapacitySettings(softCapacity: PREWARM_COUNT, hardCapacity: -1));

            PerfFastPoolable warmup = pool.Spawn();
            pool.Despawn(warmup);

            Measure.Method(() => FastPoolStep(pool))
                .WarmupCount(WARMUP_COUNT)
                .MeasurementCount(MEASUREMENT_COUNT)
                .IterationsPerMeasurement(ITERATIONS_PER_MEASUREMENT)
                .GC()
                .Run();
        }

        private static void ObjectPoolStep(ObjectPool<int, PerfPoolable> pool)
        {
            PerfPoolable item = pool.Spawn(1);
            _intSink ^= item.Id;
            pool.Despawn(item);
        }

        private static void FastPoolStep(PerfFastObjectPool pool)
        {
            PerfFastPoolable item = pool.Spawn();
            _boolSink = item.IsActive;
            pool.Despawn(item);
        }
    }
}
