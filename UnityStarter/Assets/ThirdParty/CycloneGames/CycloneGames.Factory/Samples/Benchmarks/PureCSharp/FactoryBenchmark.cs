using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using CycloneGames.Factory.Runtime;

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
                
                var particle = pool.Spawn(data);
                pool.Maintenance(); // Process despawn
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
                pool.UpdateActiveItems(p => p.Tick());
                pool.Maintenance();
                
                // Remove despawned particles from our tracking
                activeParticles.RemoveAll(p => p.IsDestroyed);
            });
            
            Console.WriteLine($"  Final active particles: {pool.NumActive}");
            Console.WriteLine($"  Final inactive particles: {pool.NumInactive}");
            pool.Dispose();
        }

        /// <summary>
        /// Test pool auto-scaling behavior
        /// </summary>
        private void BenchmarkObjectPoolScaling()
        {
            const int phases = 10;
            var factory = new DefaultFactory<BenchmarkParticle>();
            var pool = new ObjectPool<ParticleData, BenchmarkParticle>(factory, 10);
            
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
                    
                    // Process several ticks to allow natural despawning
                    for (int tick = 0; tick < 10; tick++)
                    {
                        pool.Maintenance();
                    }
                }
                
                // Phase 2: Let pool shrink
                for (int i = 0; i < 50; i++)
                {
                    pool.Maintenance();
                }
            });
            
            Console.WriteLine($"  Final pool size: {pool.NumTotal} (Active: {pool.NumActive}, Inactive: {pool.NumInactive})");
            pool.Dispose();
        }

        /// <summary>
        /// Test concurrent access performance (if threading is used)
        /// </summary>
        private void BenchmarkConcurrentPoolAccess()
        {
            const int iterations = 1000;
            const int threadsCount = 4;
            
            var factory = new DefaultFactory<BenchmarkParticle>();
            var pool = new ObjectPool<ParticleData, BenchmarkParticle>(factory, 100);
            
            _runner.RunBenchmark("Concurrent Pool Access", iterations, () =>
            {
                var tasks = new List<System.Threading.Tasks.Task>();
                
                for (int t = 0; t < threadsCount; t++)
                {
                    int threadId = t;
                    var task = System.Threading.Tasks.Task.Run(() =>
                    {
                        for (int i = 0; i < 10; i++)
                        {
                            var data = new ParticleData
                            {
                                StartPosition = new Vector2(threadId, i),
                                Velocity = Vector2.UnitX,
                                LifetimeTicks = 5
                            };
                            
                            var particle = pool.Spawn(data);
                            System.Threading.Thread.Sleep(1); // Simulate work
                        }
                    });
                    tasks.Add(task);
                }
                
                System.Threading.Tasks.Task.WaitAll(tasks.ToArray());
                pool.Maintenance();
            });
            
            pool.Dispose();
        }
    }

    /// <summary>
    /// Test particle class for benchmarking
    /// </summary>
    public class BenchmarkParticle : IPoolable<ParticleData, IMemoryPool>, ITickable
    {
        private Vector2 _position;
        private Vector2 _velocity;
        private int _lifetimeTicks;
        private int _currentTick;
        private IMemoryPool _pool;

        public bool IsDestroyed { get; private set; }

        public void Initialize(Vector2 position, Vector2 velocity, int lifetimeTicks)
        {
            _position = position;
            _velocity = velocity;
            _lifetimeTicks = lifetimeTicks;
            _currentTick = 0;
            IsDestroyed = false;
        }

        public void OnSpawned(ParticleData data, IMemoryPool pool)
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
}
