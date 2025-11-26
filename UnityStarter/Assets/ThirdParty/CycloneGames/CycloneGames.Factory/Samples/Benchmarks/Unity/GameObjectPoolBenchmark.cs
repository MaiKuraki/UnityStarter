using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.Profiling;
using CycloneGames.Factory.Runtime;
using Cysharp.Threading.Tasks;

namespace CycloneGames.Factory.Samples.Benchmarks.Unity
{
    /// <summary>
    /// A fully UniTask-based benchmark suite for GameObject pooling.
    /// Provides high-precision performance and memory analysis for comparing object pooling with direct instantiation.
    /// </summary>
    public class GameObjectPoolBenchmark : MonoBehaviour
    {
        [Header("Benchmark Configuration")]
        [Tooltip("The prefab to be used in instantiation and pooling tests.")]
        [SerializeField] private BenchmarkBullet bulletPrefab;
        [Tooltip("The parent transform for spawned GameObjects.")]
        [SerializeField] private Transform spawnParent;
        [Tooltip("Number of iterations to warm up the JIT and caches before measurement.")]
        [SerializeField] private int warmupIterations = 200;
        [Tooltip("Number of iterations to measure for performance benchmarks.")]
        [SerializeField] private int measurementIterations = 1000;
        [Tooltip("Automatically run all benchmarks when the scene starts.")]
        [SerializeField] private bool runOnStart = true;
        [Tooltip("Enables detailed Unity Profiler samples for granular performance analysis.")]
        [SerializeField] private bool enableDetailedProfiling = true;
        
        [Header("High-Precision Mode")]
        [Tooltip("Use multiple trials for more statistically accurate results.")]
        [SerializeField] private bool useHighPrecisionMode = true;
        [Tooltip("Number of trials to run for statistical accuracy (3-5 recommended).")]
        [SerializeField] private int benchmarkTrials = 3;

        [Header("Stress Test Configuration")]
        [Tooltip("The maximum number of concurrent objects to spawn during the stress test.")]
        [SerializeField] private int maxConcurrentObjects = 5000;
        [Tooltip("How many objects to spawn per frame during the stress test.")]
        [SerializeField] private int spawnBatchSize = 100;
        [Tooltip("The duration of the stress test in seconds.")]
        [SerializeField] private float stressTestDuration = 10f;

		[Header("Scenarios")]
		[Tooltip("Run pairwise benchmarks (create+destroy per-iteration)")]
		[SerializeField] private bool includePairwiseBenchmarks = true;
		[Tooltip("Run cold start scenarios (pool capacity insufficient; expand during test)")]
		[SerializeField] private bool includeColdStart = true;
		[Tooltip("Run prewarmed scenarios (pool capacity >= demand)")]
		[SerializeField] private bool includePrewarmed = true;
		[Tooltip("Prewarm size for prewarmed scenarios")]
		[SerializeField] private int prewarmSize = 1000;
		[Tooltip("Burst size per operation for cold/prewarmed scenarios (spawn N, then despawn N)")]
		[SerializeField] private int scenarioBurstSize = 256;

        private UnityBenchmarkRunner _runner;
        private ObjectPool<BulletSpawnData, BenchmarkBullet> _bulletPool;
        private IFactory<BenchmarkBullet> _bulletFactory;
        private List<GameObject> _directInstantiatedObjects;
        private CancellationTokenSource _cancellationTokenSource;

        private void Awake()
        {
            // Dependency validation for safety and stability
            if (bulletPrefab == null)
            {
                Debug.LogError("Benchmark disabled: 'Bullet Prefab' is not assigned.", this);
                enabled = false;
                return;
            }
            if (spawnParent == null)
            {
                Debug.LogError("Benchmark disabled: 'Spawn Parent' is not assigned.", this);
                enabled = false;
                return;
            }

            // Initialize
            _runner = gameObject.AddComponent<UnityBenchmarkRunner>();
            _directInstantiatedObjects = new List<GameObject>(measurementIterations);
            _cancellationTokenSource = new CancellationTokenSource();
            
            SetupDependencies();
        }

        private void Start()
        {
            if (runOnStart)
            {
                RunAllBenchmarksAsync().Forget();
            }
        }

        private void OnDestroy()
        {
            _cancellationTokenSource?.Cancel();
            _cancellationTokenSource?.Dispose();
            _bulletPool?.Dispose();

            // Final cleanup of any remaining objects
            if (_directInstantiatedObjects != null)
            {
                foreach (var obj in _directInstantiatedObjects)
                {
                    if (obj != null)
                    {
                        // Use Destroy instead of DestroyImmediate in OnDestroy
                        Destroy(obj);
                    }
                }
                _directInstantiatedObjects.Clear();
            }
        }

        private void SetupDependencies()
        {
            var spawner = new DefaultUnityObjectSpawner();
            _bulletFactory = new MonoPrefabFactory<BenchmarkBullet>(spawner, bulletPrefab, spawnParent);
            _bulletPool = new ObjectPool<BulletSpawnData, BenchmarkBullet>(_bulletFactory, 100);
        }

        [ContextMenu("Run All Benchmarks (UniTask)")]
        public void RunAllBenchmarks()
        {
            RunAllBenchmarksAsync().Forget();
        }

        private async UniTaskVoid RunAllBenchmarksAsync()
        {
            var token = _cancellationTokenSource.Token;
            try
            {
                Debug.Log("=== Starting Unity GameObject Pool Benchmarks (UniTask Mode) ===");

                // Isolate this session's results
                _runner.ClearResults();

                // Baseline batch measurements
                await BenchmarkDirectInstantiationAsync(token);
                await BenchmarkFactoryInstantiationAsync(token);
                await BenchmarkObjectPoolSpawningAsync(token);
                await BenchmarkPoolingVsInstantiationAsync(token);
                await BenchmarkMemoryUsageAsync(token);
                await BenchmarkStressTestAsync(token);

                // Pairwise scenarios
                if (includePairwiseBenchmarks)
                {
                    await BenchmarkPairwiseInstantiateDestroyAsync(token);
                    await BenchmarkPairwisePoolSpawnDespawnAsync(token);
                }

                // Cold start vs Prewarmed scenarios
                if (includeColdStart)
                {
                    await BenchmarkPoolScenarioAsync("Pool Spawn/Despawn (Cold)", prewarm: 0, token);
                }
                if (includePrewarmed)
                {
                    await BenchmarkPoolScenarioAsync($"Pool Spawn/Despawn (Prewarmed {prewarmSize})", prewarm: prewarmSize, token);
                }

                // Reclaim pooled objects (do not clear pool), and clean up non-pooled
                _bulletPool.DespawnAllActive();
                _bulletPool.Maintenance();
                if (_directInstantiatedObjects != null && _directInstantiatedObjects.Count > 0)
                {
                    for (int i = 0; i < _directInstantiatedObjects.Count; i++)
                    {
                        var obj = _directInstantiatedObjects[i];
                        if (obj != null) DestroyImmediate(obj);
                    }
                    _directInstantiatedObjects.Clear();
                }

                _runner.PrintSummary();
                
                string reportLabel = $"GameObject_Pool_{maxConcurrentObjects}Objects_UniTask";
                _runner.GenerateReport(reportLabel);
                
                Debug.Log("All benchmarks completed! Report generated.");
            }
            catch (System.OperationCanceledException)
            {
                Debug.Log("Benchmark run was cancelled.");
            }
            catch (System.Exception ex)
            {
                Debug.LogError($"An error occurred during the benchmark run: {ex}", this);
            }
        }

        private async UniTask BenchmarkDirectInstantiationAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.DirectInstantiation");
            
            System.Action op = () =>
            {
                if (enableDetailedProfiling) Profiler.BeginSample("Op.Instantiate");
                var obj = Instantiate(bulletPrefab.gameObject, spawnParent);
                _directInstantiatedObjects.Add(obj);
                if (enableDetailedProfiling) Profiler.EndSample();
            };
            
            System.Action cleanup = () =>
            {
                if (enableDetailedProfiling) Profiler.BeginSample("Op.Cleanup");
                foreach (var obj in _directInstantiatedObjects)
                {
                    if (obj != null) DestroyImmediate(obj);
                }
                _directInstantiatedObjects.Clear();
                if (enableDetailedProfiling) Profiler.EndSample();
            };

            var runnerAction = useHighPrecisionMode
                ? _runner.RunUltraHighPrecisionBenchmarkAsync("Direct GameObject.Instantiate", measurementIterations, op, benchmarkTrials, warmupIterations, cleanup, cancellationToken)
                : _runner.RunQuickBenchmarkAsync("Direct GameObject.Instantiate (Quick)", measurementIterations, op, cleanup, cancellationToken);
            
            await runnerAction;

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkFactoryInstantiationAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.FactoryInstantiation");
            
            var spawnedBullets = new List<BenchmarkBullet>(measurementIterations);
            
            System.Action op = () =>
            {
                if (enableDetailedProfiling) Profiler.BeginSample("Op.Factory.Create");
                var bullet = _bulletFactory.Create();
                spawnedBullets.Add(bullet);
                if (enableDetailedProfiling) Profiler.EndSample();
            };
            
            System.Action cleanup = () =>
            {
                if (enableDetailedProfiling) Profiler.BeginSample("Op.Cleanup");
                foreach (var bullet in spawnedBullets)
                {
                    if (bullet != null) DestroyImmediate(bullet.gameObject);
                }
                spawnedBullets.Clear();
                if (enableDetailedProfiling) Profiler.EndSample();
            };

            var runnerAction = useHighPrecisionMode
                ? _runner.RunUltraHighPrecisionBenchmarkAsync("Factory GameObject Creation", measurementIterations, op, benchmarkTrials, warmupIterations, cleanup, cancellationToken)
                : _runner.RunQuickBenchmarkAsync("Factory GameObject Creation (Quick)", measurementIterations, op, cleanup, cancellationToken);

            await runnerAction;

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkObjectPoolSpawningAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.ObjectPoolSpawning");

            var benchmarkData = new BulletSpawnData
            {
                Position = Vector2.zero,
                Direction = Vector2.up,
                Speed = 10f,
                Lifetime = 0.001f // Short lifetime for immediate despawn
            };

            System.Action op = () =>
            {
                if (enableDetailedProfiling) Profiler.BeginSample("Op.Pool.Spawn");
                var bullet = _bulletPool.Spawn(benchmarkData);
                if (enableDetailedProfiling) Profiler.EndSample();
            };
            
            System.Action cleanup = () =>
            {
                if (enableDetailedProfiling) Profiler.BeginSample("Op.Pool.Tick");
                _bulletPool.Maintenance(); // Process despawns
                if (enableDetailedProfiling) Profiler.EndSample();
            };

            var runnerAction = useHighPrecisionMode
                ? _runner.RunUltraHighPrecisionBenchmarkAsync("Object Pool Spawn/Despawn", measurementIterations, op, benchmarkTrials, warmupIterations, cleanup, cancellationToken)
                : _runner.RunQuickBenchmarkAsync("Object Pool Spawn/Despawn (Quick)", measurementIterations, op, cleanup, cancellationToken);

            await runnerAction;

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkPoolingVsInstantiationAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.PoolingVsInstantiation");
            
            var instantiationTimes = new List<float>();
            var poolingTimes = new List<float>();
            const int comparisons = 100;

            for (int i = 0; i < comparisons; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                float startTime = Time.realtimeSinceStartup;
                var obj = Instantiate(bulletPrefab.gameObject, spawnParent);
                instantiationTimes.Add((Time.realtimeSinceStartup - startTime) * 1000f);
                DestroyImmediate(obj);

                await UniTask.DelayFrame(1, cancellationToken: cancellationToken);

                startTime = Time.realtimeSinceStartup;
                var data = new BulletSpawnData { Position = Vector2.zero, Direction = Vector2.up, Speed = 10f, Lifetime = 1f };
                var bullet = _bulletPool.Spawn(data);
                poolingTimes.Add((Time.realtimeSinceStartup - startTime) * 1000f);
                _bulletPool.Despawn(bullet);

                await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
            }

            var avgInstantiation = instantiationTimes.Average();
            var avgPooling = poolingTimes.Average();
            var speedup = avgPooling > 0 ? avgInstantiation / avgPooling : float.PositiveInfinity;

            Debug.Log($"[Comparison] Pooling is {speedup:F2}x faster than direct instantiation. (Avg Instantiation: {avgInstantiation:F4}ms, Avg Pooling: {avgPooling:F4}ms)");

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkMemoryUsageAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.MemoryUsage");
            Debug.Log("--- Running Memory Usage Benchmark ---");

            const int count = 500;
            
            // --- Measure Instantiation ---
            await ForceGCAndSettleAsync(cancellationToken);
            long baselineMemory = Profiler.GetTotalAllocatedMemoryLong();
            
            var objects = new List<GameObject>(count);
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                objects.Add(Instantiate(bulletPrefab.gameObject, spawnParent));
                if (i % 100 == 0) await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
            }
            long instantiationMemory = Profiler.GetTotalAllocatedMemoryLong();
            foreach (var obj in objects) { if (obj != null) DestroyImmediate(obj); }
            objects.Clear();
            
            // --- Measure Pooling ---
            await ForceGCAndSettleAsync(cancellationToken);
            long poolBaselineMemory = Profiler.GetTotalAllocatedMemoryLong();
            
            var pooledBullets = new List<BenchmarkBullet>(count);
            var data = new BulletSpawnData { Lifetime = 100f }; // Long lifetime
            for (int i = 0; i < count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                pooledBullets.Add(_bulletPool.Spawn(data));
                if (i % 100 == 0) await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
            }
            long poolingMemory = Profiler.GetTotalAllocatedMemoryLong();
            foreach (var bullet in pooledBullets) { _bulletPool.Despawn(bullet); }
            _bulletPool.Maintenance();
            pooledBullets.Clear();

            // --- Report ---
            // This manual benchmark logs its own results and adds them to the runner
            // for inclusion in the final summary report.
            var resultInstantiation = new UnityBenchmarkResult
            {
                Name = "Memory - Direct Instantiation",
                Iterations = count,
                AllocatedMemoryDelta = instantiationMemory - baselineMemory,
                MemoryPerIteration = (instantiationMemory - baselineMemory) / count
            };
            _runner.AddResult(resultInstantiation);

            var resultPooling = new UnityBenchmarkResult
            {
                Name = "Memory - Pool Spawning",
                Iterations = count,
                AllocatedMemoryDelta = poolingMemory - poolBaselineMemory,
                MemoryPerIteration = (poolingMemory - poolBaselineMemory) / count
            };
            _runner.AddResult(resultPooling);

            Debug.Log($"[Memory] Instantiation allocated {resultInstantiation.AllocatedMemoryDelta} bytes. Pooling allocated {resultPooling.AllocatedMemoryDelta} bytes.");
            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkStressTestAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.StressTest");
            Debug.Log($"--- Running Stress Test: {maxConcurrentObjects} objects over {stressTestDuration}s ---");

            float startTime = Time.realtimeSinceStartup;
            var frameTimes = new List<float>();

            while (Time.realtimeSinceStartup - startTime < stressTestDuration)
            {
                cancellationToken.ThrowIfCancellationRequested();
                float frameStart = Time.realtimeSinceStartup;

                if (enableDetailedProfiling) Profiler.BeginSample("Op.Stress.SpawnBatch");
                for (int i = 0; i < spawnBatchSize; i++)
                {
                    if (_bulletPool.NumActive >= maxConcurrentObjects) break;
                    var data = new BulletSpawnData
                    {
                        Position = Random.insideUnitCircle * 10f,
                        Direction = Random.insideUnitCircle.normalized,
                        Speed = Random.Range(1f, 10f),
                        Lifetime = Random.Range(1f, 5f)
                    };
                    _bulletPool.Spawn(data);
                }
                if (enableDetailedProfiling) Profiler.EndSample();

                if (enableDetailedProfiling) Profiler.BeginSample("Op.Stress.PoolTick");
                _bulletPool.UpdateActiveItems(b => b.Tick());
                _bulletPool.Maintenance();
                if (enableDetailedProfiling) Profiler.EndSample();

                frameTimes.Add((Time.realtimeSinceStartup - frameStart) * 1000f);
                await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
            }

            var result = new UnityBenchmarkResult
            {
                Name = "FrameTime - Stress Test",
                Iterations = frameTimes.Count,
                TotalTimeSeconds = stressTestDuration,
                AverageTimeMs = frameTimes.Average(),
                MinFrameTimeMs = frameTimes.Min(),
                MaxFrameTimeMs = frameTimes.Max(),
                FrameTimeStdDev = frameTimes.StandardDeviation()
            };
            _runner.AddResult(result);

            Debug.Log($"[Stress Test] Avg FrameTime: {result.AverageTimeMs:F2}ms, Max: {result.MaxFrameTimeMs:F2}ms. Final active objects: {_bulletPool.NumActive}");
            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkPairwiseInstantiateDestroyAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.Pairwise.InstantiateDestroy");

            System.Action op = () =>
            {
                var obj = Instantiate(bulletPrefab.gameObject, spawnParent);
                DestroyImmediate(obj);
            };

            await _runner.RunUltraHighPrecisionBenchmarkAsync(
                "Pairwise Direct Instantiate+Destroy",
                measurementIterations,
                op,
                benchmarkTrials,
                warmupIterations,
                null,
                cancellationToken);

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkPairwisePoolSpawnDespawnAsync(CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.Pairwise.PoolSpawnDespawn");

            var data = new BulletSpawnData { Position = Vector2.zero, Direction = Vector2.up, Speed = 10f, Lifetime = 0.01f };
            System.Action op = () =>
            {
                var bullet = _bulletPool.Spawn(data);
                _bulletPool.Despawn(bullet);
            };

            await _runner.RunUltraHighPrecisionBenchmarkAsync(
                "Pairwise Pool Spawn+Despawn",
                measurementIterations,
                op,
                benchmarkTrials,
                warmupIterations,
                null,
                cancellationToken);

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask BenchmarkPoolScenarioAsync(string scenarioName, int prewarm, CancellationToken cancellationToken)
        {
            if (enableDetailedProfiling) Profiler.BeginSample("Benchmark.PoolScenario");

            // Prepare pool size
            _bulletPool.DespawnAllActive();
            _bulletPool.Maintenance();
            if (prewarm > 0)
            {
                _bulletPool.Resize(prewarm);
            }
            else
            {
                _bulletPool.Resize(0);
            }

            var data = new BulletSpawnData { Position = Vector2.zero, Direction = Vector2.up, Speed = 10f, Lifetime = 0.01f };
            System.Action op = () =>
            {
                // Burst spawn then burst despawn to surface expansion cost when prewarm=0
                var temp = new List<BenchmarkBullet>(scenarioBurstSize);
                for (int i = 0; i < scenarioBurstSize; i++)
                {
                    var bullet = _bulletPool.Spawn(data);
                    temp.Add(bullet);
                }
                for (int i = 0; i < temp.Count; i++)
                {
                    _bulletPool.Despawn(temp[i]);
                }
                _bulletPool.Maintenance();
            };

            System.Action trialCleanup = () =>
            {
                _bulletPool.DespawnAllActive();
                _bulletPool.Maintenance();
                _bulletPool.Resize(prewarm > 0 ? prewarm : 0);
            };

            await _runner.RunUltraHighPrecisionBenchmarkAsync(
                $"{scenarioName} (Burst {scenarioBurstSize})",
                measurementIterations,
                op,
                benchmarkTrials,
                warmupIterations,
                trialCleanup,
                cancellationToken);

            if (enableDetailedProfiling) Profiler.EndSample();
        }

        private async UniTask ForceGCAndSettleAsync(CancellationToken cancellationToken)
        {
            for (int i = 0; i < 3; i++)
            {
                System.GC.Collect();
                System.GC.WaitForPendingFinalizers();
                await UniTask.Delay(100, cancellationToken: cancellationToken);
            }
            System.GC.Collect();
            await UniTask.DelayFrame(1, cancellationToken: cancellationToken);
        }
        
        [ContextMenu("Run Direct Instantiation Benchmark")]
        public void RunDirectInstantiationBenchmark() => RunSingleBenchmark(BenchmarkDirectInstantiationAsync).Forget();

        [ContextMenu("Run Pool Spawning Benchmark")]
        public void RunPoolSpawningBenchmark() => RunSingleBenchmark(BenchmarkObjectPoolSpawningAsync).Forget();

        [ContextMenu("Run Stress Test")]
        public void RunStressTest() => RunSingleBenchmark(BenchmarkStressTestAsync).Forget();
        
        private async UniTaskVoid RunSingleBenchmark(System.Func<CancellationToken, UniTask> benchmarkFunc)
        {
            var token = _cancellationTokenSource.Token;
            try
            {
                await benchmarkFunc(token);
                _runner.PrintSummary();
                _runner.GenerateReport("Manual_Report");
            }
            catch (System.OperationCanceledException) { Debug.Log("Benchmark cancelled."); }
            catch (System.Exception ex) { Debug.LogError($"Benchmark failed: {ex}", this); }
        }

		[ContextMenu("Run Pairwise (Instantiate+Destroy vs Pool)")]
		public void RunPairwiseBenchmarks()
		{
			RunPairwiseBenchmarksAsync().Forget();
		}

		private async UniTaskVoid RunPairwiseBenchmarksAsync()
		{
			var token = _cancellationTokenSource.Token;
			try
			{
				_runner.ClearResults();
				await BenchmarkPairwiseInstantiateDestroyAsync(token);
				await BenchmarkPairwisePoolSpawnDespawnAsync(token);
				_runner.GenerateReport("Pairwise_Only");
			}
			catch { }
		}
    }

    /// <summary>
    /// Data structure for spawning benchmark bullets
    /// </summary>
    [System.Serializable]
    public struct BulletSpawnData
    {
        public Vector2 Position;
        public Vector2 Direction;
        public float Speed;
        public float Lifetime;
    }
}

// Extension method for calculating standard deviation
public static class ListExtensions
{
    public static float StandardDeviation(this List<float> values)
    {
        if (values.Count == 0) return 0f;
        
        float avg = values.Average();
        float sumOfSquares = 0f;
        for (int i = 0; i < values.Count; i++)
        {
            float diff = values[i] - avg;
            sumOfSquares += diff * diff;
        }
        return Mathf.Sqrt(sumOfSquares / values.Count);
    }

    public static float Average(this List<float> values)
    {
        if (values.Count == 0) return 0f;
        float sum = 0f;
        for (int i = 0; i < values.Count; i++)
        {
            sum += values[i];
        }
        return sum / values.Count;
    }

    public static float Max(this List<float> values)
    {
        if (values.Count == 0) return 0f;
        float max = float.MinValue;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] > max) max = values[i];
        }
        return max;
    }
    public static float Min(this List<float> values)
    {
        if (values.Count == 0) return 0f;
        float min = float.MaxValue;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] < min) min = values[i];
        }
        return min;
    }
}
