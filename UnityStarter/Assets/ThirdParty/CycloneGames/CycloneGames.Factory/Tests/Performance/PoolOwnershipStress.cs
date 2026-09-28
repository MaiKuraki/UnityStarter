using System;
using System.Collections.Generic;
using CycloneGames.Factory.Runtime;

namespace CycloneGames.Factory.Tests.Performance
{
    public enum PoolOperationMix
    {
        SpawnHeavy = 0,
        TrimHeavy = 1,
        DrainHeavy = 2,
    }

    public readonly struct PoolStressProfile
    {
        public PoolStressProfile(int seed, PoolOperationMix mix)
        {
            Seed = seed;
            Mix = mix;
        }

        public int Seed { get; }

        public PoolOperationMix Mix { get; }

        public override string ToString()
        {
            return Mix + "_seed" + Seed;
        }
    }

    public sealed class PoolStressResult
    {
        public bool Consistent { get; set; }

        public string Detail { get; set; }
    }

    /// <summary>
    /// Operation-mix stress driver shared by the Unity test suite and the standalone .NET verification
    /// harness. It depends on nothing but the pool core and the BCL, so both runtimes execute the exact
    /// same sequences and their results are directly comparable.
    /// </summary>
    public static class PoolOwnershipStress
    {
        private const int SoftCapacity = 64;
        private const int DefaultOperations = 20000;

        /// <summary>Profile matrix: three operation mixes crossed with three seeds.</summary>
        public static readonly PoolStressProfile[] Profiles =
        {
            new PoolStressProfile(20260928, PoolOperationMix.SpawnHeavy),
            new PoolStressProfile(20260928, PoolOperationMix.TrimHeavy),
            new PoolStressProfile(20260928, PoolOperationMix.DrainHeavy),
            new PoolStressProfile(1, PoolOperationMix.SpawnHeavy),
            new PoolStressProfile(7, PoolOperationMix.TrimHeavy),
            new PoolStressProfile(12345, PoolOperationMix.DrainHeavy),
            new PoolStressProfile(99991, PoolOperationMix.SpawnHeavy),
            new PoolStressProfile(424242, PoolOperationMix.TrimHeavy),
            new PoolStressProfile(31337, PoolOperationMix.DrainHeavy),
        };

        public static PoolStressResult Run(PoolStressProfile profile)
        {
            return Run(profile, DefaultOperations);
        }

        public static PoolStressResult Run(PoolStressProfile profile, int operations)
        {
            var factory = new StressPoolableFactory();
            using var pool = new ObjectPool<int, StressPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: SoftCapacity, hardCapacity: -1));

            int[] weights = GetWeights(profile.Mix);
            var random = new Random(profile.Seed);
            var active = new List<StressPoolable>();

            long spawned = 0;
            long despawned = 0;
            string failure = null;
            int step = 0;

            for (; step < operations && failure == null; step++)
            {
                switch (SelectAction(random, weights))
                {
                    case 0:
                    {
                        int batch = 1 + random.Next(8);
                        for (int i = 0; i < batch; i++)
                        {
                            active.Add(pool.Spawn(step));
                            spawned++;
                        }

                        break;
                    }

                    case 1:
                    {
                        if (active.Count > 0)
                        {
                            int index = random.Next(active.Count);
                            StressPoolable item = active[index];
                            active[index] = active[active.Count - 1];
                            active.RemoveAt(active.Count - 1);
                            if (!pool.Despawn(item))
                            {
                                failure = "an owned item was rejected on despawn";
                            }

                            despawned++;
                        }

                        break;
                    }

                    case 2:
                    {
                        pool.TrimInactive(random.Next(SoftCapacity + 1));
                        break;
                    }

                    case 3:
                    {
                        pool.Prewarm(random.Next(1, 9));
                        break;
                    }

                    case 4:
                    {
                        pool.WarmupStep(random.Next(1, 9));
                        break;
                    }

                    default:
                    {
                        despawned += pool.CountActive;
                        pool.DespawnAll();
                        active.Clear();
                        break;
                    }
                }

                failure = Inspect(pool, active.Count, spawned, despawned, step);
            }

            if (failure == null)
            {
                // Trimming to zero must release every inactive item while active leases stay tracked.
                pool.TrimInactive(0);
                failure = Inspect(pool, active.Count, spawned, despawned, step) ??
                    (pool.CountInactive == 0 ? null : "inactive items survived trimming to zero");
            }

            if (failure == null)
            {
                // Releasing the remaining leases must then empty the pool and the ownership set together.
                despawned += pool.CountActive;
                pool.DespawnAll();
                active.Clear();
                pool.TrimInactive(0);
                failure = Inspect(pool, 0, spawned, despawned, step) ??
                    (pool.CountAll == 0 && pool.TrackedItemCount == 0
                        ? null
                        : $"ownership set did not drain after full release (tracked={pool.TrackedItemCount})");
            }

            return new PoolStressResult
            {
                Consistent = failure == null,
                Detail = failure ??
                    $"{profile}, {operations} ops, final active={pool.CountActive} inactive={pool.CountInactive}",
            };
        }

        private static string Inspect(
            ObjectPool<int, StressPoolable> pool,
            int expectedActive,
            long spawned,
            long despawned,
            int step)
        {
            if (pool.CountActive != expectedActive)
            {
                return $"step {step}: active {pool.CountActive} diverged from the model {expectedActive}";
            }

            if (pool.TrackedItemCount != pool.CountAll)
            {
                return $"step {step}: ownership set {pool.TrackedItemCount} drifted from CountAll {pool.CountAll}";
            }

            PoolDiagnostics diagnostics = pool.Diagnostics;
            if (diagnostics.TotalCreated - diagnostics.TotalDestroyed != pool.CountAll)
            {
                return $"step {step}: created-minus-destroyed diverged from CountAll {pool.CountAll}";
            }

            if (diagnostics.TotalSpawned != spawned || diagnostics.TotalDespawned != despawned)
            {
                return $"step {step}: spawn/despawn counters drifted ({diagnostics.TotalSpawned}/{diagnostics.TotalDespawned})";
            }

            if (diagnostics.InvalidDespawns != 0
                || diagnostics.CallbackFailures != 0
                || diagnostics.QuarantinedItems != 0)
            {
                return $"step {step}: invalid={diagnostics.InvalidDespawns} callbackFailures={diagnostics.CallbackFailures} quarantined={diagnostics.QuarantinedItems}";
            }

            return null;
        }

        private static int SelectAction(Random random, int[] weights)
        {
            int roll = random.Next(100);
            int cumulative = 0;
            for (int index = 0; index < weights.Length; index++)
            {
                cumulative += weights[index];
                if (roll < cumulative)
                {
                    return index;
                }
            }

            return weights.Length - 1;
        }

        /// <summary>Action weights out of 100: spawn, despawn, trim, prewarm, warmup, drain.</summary>
        private static int[] GetWeights(PoolOperationMix mix)
        {
            switch (mix)
            {
                case PoolOperationMix.SpawnHeavy:
                    return new[] { 55, 30, 5, 3, 2, 5 };
                case PoolOperationMix.TrimHeavy:
                    return new[] { 20, 20, 45, 5, 5, 5 };
                default:
                    return new[] { 25, 15, 5, 5, 5, 45 };
            }
        }
    }

    /// <summary>Poolable used by the shared stress driver.</summary>
    internal sealed class StressPoolable : IPoolable<int, StressPoolable>
    {
        public int Payload { get; set; }

        public void OnSpawned(int data, IDespawnableMemoryPool<StressPoolable> pool)
        {
            Payload = data;
        }

        public void OnDespawned()
        {
            Payload = 0;
        }
    }

    /// <summary>Factory used by the shared stress driver.</summary>
    internal sealed class StressPoolableFactory : IFactory<StressPoolable>
    {
        public StressPoolable Create()
        {
            return new StressPoolable();
        }
    }
}
