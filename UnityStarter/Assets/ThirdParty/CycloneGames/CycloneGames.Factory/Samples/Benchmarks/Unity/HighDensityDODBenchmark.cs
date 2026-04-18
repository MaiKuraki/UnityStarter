#if PRESENT_COLLECTIONS
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;
using UnityEngine;
using UnityEngine.Profiling;

namespace CycloneGames.Factory.Samples.Benchmarks.Unity
{
    /// <summary>
    /// Dedicated DOD benchmark split for high-density workloads.
    /// Compares contiguous NativePool iteration against handle-safe NativeDensePool churn.
    /// </summary>
    public sealed class HighDensityDODBenchmark : MonoBehaviour
    {
        private enum BenchmarkMode
        {
            NativePool = 0,
            NativeDensePool = 1,
            NativeDenseColumnPool2 = 2,
            NativeDenseColumnPool2Batch = 3,
        }

        [SerializeField] private FactoryHighLoadProfile profile;
        [SerializeField] private BenchmarkMode benchmarkMode = BenchmarkMode.NativeDensePool;
        [SerializeField] private bool runOnStart = true;

        private void Start()
        {
            if (runOnStart)
            {
                RunBenchmark();
            }
        }

        [ContextMenu("Run DOD High Density Benchmark")]
        public void RunBenchmark()
        {
            if (profile == null)
            {
                Debug.LogWarning("HighDensityDODBenchmark skipped: no FactoryHighLoadProfile assigned.", this);
                return;
            }

            switch (benchmarkMode)
            {
                case BenchmarkMode.NativePool:
                    RunNativePoolBenchmark();
                    break;
                case BenchmarkMode.NativeDenseColumnPool2:
                    RunNativeDenseColumnPoolBenchmark();
                    break;
                case BenchmarkMode.NativeDenseColumnPool2Batch:
                    RunNativeDenseColumnPoolBatchBenchmark();
                    break;
                default:
                    RunNativeDensePoolBenchmark();
                    break;
            }
        }

        private void RunNativePoolBenchmark()
        {
            using var pool = new NativePool<DenseBulletState>(profile.hardCapacity, Allocator.Temp);
            Profiler.BeginSample("Factory.NativePool.HighDensity");

            for (int i = 0; i < profile.sustainedActiveCount; i++)
            {
                pool.Spawn(new DenseBulletState
                {
                    Position = new Vector2(i, i * 0.5f),
                    Velocity = Vector2.right,
                    Lifetime = 8,
                });
            }

            var activeItems = pool.ActiveItems;
            for (int i = 0; i < activeItems.Length; i++)
            {
                var state = activeItems[i];
                state.Position += state.Velocity;
                state.Lifetime--;
                activeItems[i] = state;
            }

            Profiler.EndSample();
            Debug.Log($"[Factory][DOD][NativePool] Active={pool.ActiveCount} Capacity={pool.Capacity}", this);
        }

        private void RunNativeDensePoolBenchmark()
        {
            using var pool = new NativeDensePool<DenseBulletState>(profile.hardCapacity, Allocator.Temp);
            Profiler.BeginSample("Factory.NativeDensePool.HighDensity");

            for (int i = 0; i < profile.sustainedActiveCount; i++)
            {
                pool.TrySpawn(new DenseBulletState
                {
                    Position = new Vector2(i, i * 0.5f),
                    Velocity = Vector2.up,
                    Lifetime = 8,
                }, out _, out _);
            }

            var activeItems = pool.ActiveItems;
            for (int i = 0; i < activeItems.Length; i++)
            {
                var state = activeItems[i];
                state.Position += state.Velocity;
                state.Lifetime--;
                activeItems[i] = state;
            }

            int removals = Mathf.Min(profile.spawnBurstPerFrame, pool.CountActive);
            for (int i = 0; i < removals; i++)
            {
                var handle = pool.GetHandleAtDenseIndex(pool.CountActive - 1);
                pool.Despawn(handle);
            }

            Profiler.EndSample();
            Debug.Log($"[Factory][DOD][NativeDensePool] CountActive={pool.CountActive} CountInactive={pool.CountInactive} Capacity={pool.Capacity} PeakCountActive={pool.Diagnostics.PeakCountActive} Freed={removals}", this);
        }

        private void RunNativeDenseColumnPoolBenchmark()
        {
            using var pool = new NativeDenseColumnPool2<Vector2, DenseAuxState>(profile.hardCapacity, Allocator.Temp);
            Profiler.BeginSample("Factory.NativeDenseColumnPool2.HighDensity");

            for (int i = 0; i < profile.sustainedActiveCount; i++)
            {
                pool.TrySpawn(
                    new Vector2(i, i * 0.5f),
                    new DenseAuxState
                    {
                        Velocity = Vector2.up,
                        Lifetime = 8,
                    },
                    out _,
                    out _);
            }

            var positions = pool.Stream0;
            var aux = pool.Stream1;
            for (int i = 0; i < pool.CountActive; i++)
            {
                positions[i] += aux[i].Velocity;
                var state = aux[i];
                state.Lifetime--;
                aux[i] = state;
            }

            int removals = Mathf.Min(profile.spawnBurstPerFrame, pool.CountActive);
            for (int i = 0; i < removals; i++)
            {
                var handle = pool.GetHandleAtDenseIndex(pool.CountActive - 1);
                pool.Despawn(handle);
            }

            Profiler.EndSample();
            Debug.Log($"[Factory][DOD][NativeDenseColumnPool2] CountActive={pool.CountActive} CountInactive={pool.CountInactive} Capacity={pool.Capacity} PeakCountActive={pool.Diagnostics.PeakCountActive} Freed={removals}", this);
        }

        private void RunNativeDenseColumnPoolBatchBenchmark()
        {
            using var pool = new NativeDenseColumnPool2<Vector2, DenseAuxState>(profile.hardCapacity, Allocator.Temp);
            var positions = new NativeArray<Vector2>(profile.spawnBurstPerFrame, Allocator.Temp);
            var auxStates = new NativeArray<DenseAuxState>(profile.spawnBurstPerFrame, Allocator.Temp);
            var handles = new NativeArray<NativePoolHandle>(profile.spawnBurstPerFrame, Allocator.Temp);
            try
            {
                Profiler.BeginSample("Factory.NativeDenseColumnPool2.BatchHighDensity");

                int spawnedTotal = 0;
                while (spawnedTotal < profile.sustainedActiveCount)
                {
                    int batchCount = Mathf.Min(profile.spawnBurstPerFrame, profile.sustainedActiveCount - spawnedTotal);
                    for (int i = 0; i < batchCount; i++)
                    {
                        positions[i] = new Vector2(spawnedTotal + i, (spawnedTotal + i) * 0.5f);
                        auxStates[i] = new DenseAuxState
                        {
                            Velocity = Vector2.right,
                            Lifetime = 16,
                        };
                    }

                    spawnedTotal += pool.SpawnBatch(positions, auxStates, batchCount, handles, allowPartial: true);
                    if (pool.CountActive >= pool.Capacity)
                    {
                        break;
                    }
                }

                var activePositions = pool.Stream0;
                var activeAux = pool.Stream1;
                for (int i = 0; i < pool.CountActive; i++)
                {
                    activePositions[i] += activeAux[i].Velocity;
                    var state = activeAux[i];
                    state.Lifetime--;
                    activeAux[i] = state;
                }

                int removals = Mathf.Min(profile.spawnBurstPerFrame, pool.CountActive);
                for (int i = 0; i < removals; i++)
                {
                    handles[i] = pool.GetHandleAtDenseIndex(pool.CountActive - 1 - i);
                }
                int despawned = pool.DespawnBatch(handles, removals);

                Profiler.EndSample();
                Debug.Log($"[Factory][DOD][NativeDenseColumnPool2.Batch] CountActive={pool.CountActive} CountInactive={pool.CountInactive} Capacity={pool.Capacity} PeakCountActive={pool.Diagnostics.PeakCountActive} Despawned={despawned}", this);
            }
            finally
            {
                if (handles.IsCreated) handles.Dispose();
                if (auxStates.IsCreated) auxStates.Dispose();
                if (positions.IsCreated) positions.Dispose();
            }
        }

        private struct DenseBulletState
        {
            public Vector2 Position;
            public Vector2 Velocity;
            public int Lifetime;
        }

        private struct DenseAuxState
        {
            public Vector2 Velocity;
            public int Lifetime;
        }
    }
}
#endif
