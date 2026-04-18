using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using CycloneGames.Factory.Runtime;
#if PRESENT_COLLECTIONS
using CycloneGames.Factory.DOD.Runtime;
using Unity.Collections;
#endif

namespace CycloneGames.Factory.Samples.Benchmarks.PureCSharp
{
    /// <summary>
    /// Benchmarks for testing Factory and ObjectPool performance in pure C# scenarios.
    /// Measures allocation performance, pooling efficiency, and memory usage patterns.
    /// </summary>
    public class FactoryBenchmark
    {
        private readonly BenchmarkRunner _runner = new BenchmarkRunner();

        public void RunAllBenchmarks()
        {
            Console.WriteLine("=== CycloneGames.Factory Performance Benchmarks ===\n");
            
            BenchmarkDirectAllocation();
            BenchmarkFactoryAllocation();
            BenchmarkObjectPoolSpawning();
            BenchmarkObjectPoolStress();
            BenchmarkObjectPoolScaling();
            BenchmarkConcurrentPoolAccess();
            BenchmarkPoolDiagnostics();
            
            _runner.PrintSummary();
            
            // Generate comprehensive report
            string reportLabel = "Factory_Performance_Analysis";
            _runner.GenerateReport(reportLabel);
            
            Console.WriteLine("All benchmarks completed! Detailed report generated.");
        }

        /// <summary>
        /// Baseline: Direct object allocation without any factory pattern
        /// </summary>
        private void BenchmarkDirectAllocation()
        {
            const int iterations = 100000;
            
            _runner.RunBenchmark("Direct Allocation", iterations, () =>
            {
                var particle = new BenchmarkParticle();
                particle.Initialize(Vector2.Zero, Vector2.One, 100);
                // Simulate some work
                particle.Update();
            });
        }

        /// <summary>
        /// Test factory creation performance vs direct allocation
        /// </summary>
        private void BenchmarkFactoryAllocation()
        {
            const int iterations = 100000;
            var factory = new DefaultFactory<BenchmarkParticle>();
            
            _runner.RunBenchmark("Factory Allocation", iterations, () =>
            {
                var particle = factory.Create();
                particle.Initialize(Vector2.Zero, Vector2.One, 100);
                particle.Update();
            });
        }

        /// <summary>
        /// Test object pool spawn/despawn performance
        /// </summary>
        private void BenchmarkObjectPoolSpawning()
        {
            const int iterations = 50000;
            var factory = new DefaultFactory<BenchmarkParticle>();
            var pool = new ObjectPool<ParticleData, BenchmarkParticle>(factory, 1000);
            
            _runner.RunBenchmark("Object Pool Spawn/Despawn", iterations, () =>
            {
                var data = new ParticleData
                {
                    StartPosition = Vector2.Zero,
                    Velocity = Vector2.One,
                    LifetimeTicks = 1 // Will despawn immediately in next tick
                };
                
                pool.Spawn(data);
            });
            
            pool.Dispose();
        }

        /// <summary>
        /// Stress test with many concurrent active objects
        /// </summary>
        private void BenchmarkObjectPoolStress()
        {
            const int maxActive = 10000;
            const int spawnBatches = 100;
            
            var factory = new DefaultFactory<BenchmarkParticle>();
            var pool = new ObjectPool<ParticleData, BenchmarkParticle>(factory, 100);
            var activeParticles = new List<BenchmarkParticle>();
            
            _runner.RunBenchmark("Object Pool Stress Test", spawnBatches, () =>
            {
                // Spawn 100 particles per iteration
                for (int i = 0; i < 100; i++)
                {
                    var data = new ParticleData
                    {
                        StartPosition = new Vector2(i % 100, i / 100),
                        Velocity = Vector2.UnitX,
                        LifetimeTicks = maxActive / 100 // Vary lifetime
                    };
                    
                    activeParticles.Add(pool.Spawn(data));
                }
                
                // Tick all particles
                pool.ForEachActive(p => p.Tick());
                
                // Remove despawned particles from our tracking
                activeParticles.RemoveAll(p => p.IsDestroyed);
            });
            
            Console.WriteLine($"  Final active particles: {pool.CountActive}");
            Console.WriteLine($"  Final inactive particles: {pool.CountInactive}");
            pool.Dispose();
        }

        /// <summary>
        /// Test pool auto-scaling behavior
        /// </summary>
        private void BenchmarkObjectPoolScaling()
        {
            const int phases = 10;
            var factory = new DefaultFactory<BenchmarkParticle>();
            var pool = new ObjectPool<ParticleData, BenchmarkParticle>(factory, 10, 512);
            
            _runner.RunBenchmark("Object Pool Auto-Scaling", phases, () =>
            {
                // Phase 1: Gradually increase load
                for (int wave = 1; wave <= 5; wave++)
                {
                    for (int i = 0; i < wave * 50; i++)
                    {
                        var data = new ParticleData
                        {
                            StartPosition = Vector2.Zero,
                            Velocity = Vector2.UnitY,
                            LifetimeTicks = 20 + (i % 10) // Varying lifetimes
                        };
                        pool.Spawn(data);
                    }
                    
                    for (int tick = 0; tick < 10; tick++)
                    {
                        pool.ForEachActive(p => p.Tick());
                    }
                }

                // Manual trim replaces the old implicit maintenance/shrink path.
                if (pool.CountInactive > 10)
                {
                    pool.TrimInactive(10);
                }
            });
            
            Console.WriteLine($"  Final pool size: {pool.CountAll} (Active: {pool.CountActive}, Inactive: {pool.CountInactive})");
            pool.Dispose();
        }

        /// <summary>
        /// Main-thread pools intentionally do not support concurrent access.
        /// This benchmark is retained as a note so the report still explains the design choice.
        /// </summary>
        private void BenchmarkConcurrentPoolAccess()
        {
            Console.WriteLine("  Concurrent Pool Access benchmark skipped: ObjectPool is now explicitly main-thread only by design.");
        }

        private void BenchmarkPoolDiagnostics()
        {
            var factory = new DefaultFactory<BenchmarkParticle>();
            var pool = new ObjectPool<ParticleData, BenchmarkParticle>(
                factory,
                new PoolCapacitySettings(softCapacity: 64, hardCapacity: 1024, overflowPolicy: PoolOverflowPolicy.ReturnNull));

            for (int i = 0; i < 512; i++)
            {
                pool.TrySpawn(new ParticleData
                {
                    StartPosition = Vector2.Zero,
                    Velocity = Vector2.UnitX,
                    LifetimeTicks = 4
                }, out _);
            }

            pool.ForEachActive(p => p.Tick());
            pool.DespawnAll();

            var diagnostics = pool.Diagnostics;
            Console.WriteLine($"  Diagnostics: PeakCountActive={diagnostics.PeakCountActive}, PeakCountAll={diagnostics.PeakCountAll}, RejectedSpawns={diagnostics.RejectedSpawns}");
            pool.Dispose();
        }
    }

    /// <summary>
    /// Test particle class for benchmarking
    /// </summary>
    public class BenchmarkParticle : IPoolable<ParticleData, BenchmarkParticle>, ITickable
    {
        private Vector2 _position;
        private Vector2 _velocity;
        private int _lifetimeTicks;
        private int _currentTick;
        private IDespawnableMemoryPool<BenchmarkParticle> _pool;

        public bool IsDestroyed { get; private set; }

        public void Initialize(Vector2 position, Vector2 velocity, int lifetimeTicks)
        {
            _position = position;
            _velocity = velocity;
            _lifetimeTicks = lifetimeTicks;
            _currentTick = 0;
            IsDestroyed = false;
        }

        public void OnSpawned(ParticleData data, IDespawnableMemoryPool<BenchmarkParticle> pool)
        {
            _pool = pool;
            Initialize(data.StartPosition, data.Velocity, data.LifetimeTicks);
        }

        public void OnDespawned()
        {
            _pool = null;
            IsDestroyed = true;
        }

        public void Tick()
        {
            if (IsDestroyed) return;

            _currentTick++;
            _position += _velocity * 0.016f; // Simulate 60 FPS

            // Simulate some computation work
            var distance = Vector2.Distance(_position, Vector2.Zero);
            if (distance > 1000f || _currentTick >= _lifetimeTicks)
            {
                _pool?.Despawn(this);
            }
        }

        public void Update()
        {
            // Simulate work for direct allocation benchmark
            _position += _velocity * 0.016f;
        }

        public void Dispose()
        {
            // Cleanup any managed resources if needed
            // This implementation satisfies the IPoolable<T1, T2> : IDisposable requirement
            IsDestroyed = true;
        }
    }

    /// <summary>
    /// Benchmark parameter data
    /// </summary>
    public struct ParticleData
    {
        public Vector2 StartPosition;
        public Vector2 Velocity;
        public int LifetimeTicks;
    }

    /// <summary>
    /// Default factory implementation for benchmarks
    /// </summary>
    public class DefaultFactory<T> : IFactory<T> where T : new()
    {
        public T Create() => new T();
    }

#if PRESENT_COLLECTIONS
    /// <summary>
    /// Dedicated high-density benchmark for handle-based dense pools.
    /// Focuses on contiguous iteration and stable-handle churn under large active sets.
    /// </summary>
    public sealed class DensePoolBenchmark
    {
        private readonly BenchmarkRunner _runner = new BenchmarkRunner();

        public void RunAllBenchmarks()
        {
            Console.WriteLine("=== CycloneGames.Factory Dense Pool Benchmarks ===\n");

            BenchmarkDenseSpawnAndDespawn();
            BenchmarkDenseIteration();
            BenchmarkDenseChurn();

            _runner.PrintSummary();
            _runner.GenerateReport("Factory_DensePool_Analysis");
        }

        private void BenchmarkDenseSpawnAndDespawn()
        {
            const int capacity = 20000;
            using var pool = new NativeDensePool<DenseParticle>(capacity, Allocator.Temp);

            _runner.RunBenchmark("Dense Pool Spawn/Despawn", capacity, () =>
            {
                pool.TrySpawn(new DenseParticle { X = 1, Y = 2, Lifetime = 60 }, out var handle, out _);
                pool.Despawn(handle);
            });
        }

        private void BenchmarkDenseIteration()
        {
            const int count = 10000;
            using var pool = new NativeDensePool<DenseParticle>(count, Allocator.Temp);
            for (int i = 0; i < count; i++)
            {
                pool.TrySpawn(new DenseParticle { X = i, Y = i * 2, Lifetime = 120 }, out _, out _);
            }

            _runner.RunBenchmark("Dense Pool Iteration", 1000, () =>
            {
                var activeItems = pool.ActiveItems;
                for (int i = 0; i < activeItems.Length; i++)
                {
                    var particle = activeItems[i];
                    particle.X += particle.Y;
                    particle.Lifetime--;
                    activeItems[i] = particle;
                }
            });
        }

        private void BenchmarkDenseChurn()
        {
            const int capacity = 15000;
            using var pool = new NativeDensePool<DenseParticle>(capacity, Allocator.Temp);

            _runner.RunBenchmark("Dense Pool High Churn", 200, () =>
            {
                for (int i = 0; i < capacity; i++)
                {
                    pool.TrySpawn(new DenseParticle { X = i, Y = i + 1, Lifetime = 8 }, out _, out _);
                }

                int removals = Math.Min(5000, pool.CountActive);
                for (int i = 0; i < removals; i++)
                {
                    var handle = pool.GetHandleAtDenseIndex(pool.CountActive - 1);
                    pool.Despawn(handle);
                }

                pool.Clear();
            });
        }

        private struct DenseParticle
        {
            public int X;
            public int Y;
            public int Lifetime;
        }
    }
#endif
}
