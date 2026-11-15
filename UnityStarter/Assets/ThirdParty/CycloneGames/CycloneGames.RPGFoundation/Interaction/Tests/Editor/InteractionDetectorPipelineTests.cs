using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using Cysharp.Threading.Tasks;
using CycloneGames.RPGFoundation.Interaction.Core;
using CycloneGames.RPGFoundation.Interaction.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Compares the tiered candidate walk against a single wide query plus a sort, and measures
    /// the per-update allocation of a real <see cref="Interactable"/>. Tiering costs one grid walk
    /// per occupied tier, so it only wins while the number of occupied tiers stays small; the data
    /// here decides which path the detector should take.
    /// </summary>
    public sealed class InteractionDetectorPipelineTests
    {
        private static readonly ulong[] Seeds = { 0UL, 42UL, 3735928559UL };

        private GameObject _systemObject;
        private InteractionSystem _system;

        [SetUp]
        public void SetUp()
        {
            _systemObject = new GameObject("InteractionPipelineSystem");
            _system = _systemObject.AddComponent<InteractionSystem>();
            _system.Initialize(is2DMode: false, cellSize: 10f);
        }

        [TearDown]
        public void TearDown()
        {
            if (_systemObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_systemObject);
            }
        }

        [Test]
        public void Candidates_TieredWalkVersusSingleQueryPlusSort()
        {
            var csv = new StressCsvWriter("detector_strategy_v2.csv",
                "scenario,seed,interactables,tiers,strategy,max_results,cycles,p50_us,p95_us,p99_us,alloc_bytes_per_cycle,collected_mean,oracle_mismatch");

            try
            {
                foreach (int scale in new[] { 1000, 5000, 10000 })
                {
                    foreach (int tiers in new[] { 1, 4, 16 })
                    {
                        foreach (ulong seed in Seeds)
                        {
                            RunStrategyComparison(csv, scale, tiers, seed);
                        }
                    }
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Interactable_UpdatePositionIsAllocationFree()
        {
            var csv = new StressCsvWriter("interactable_update_alloc_v2.csv",
                "scenario,components,updates,alloc_bytes_per_update,gc0_delta,note");
            try
            {
                foreach (int count in new[] { 1000, 10000 })
                {
                    RunUpdateAllocation(csv, count);
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Interactable_AuthoritySnapshotAllocationIsObservable()
        {
            var go = new GameObject("SnapshotProbe");
            try
            {
                var component = go.AddComponent<TestInteractable>();
                component.Configure(priority: 1, distance: 5f,
                    actions: new[] { new InteractionAction("open", "Open") },
                    stableId: "entity/probe");

                Assert.That(component.Actions.Count, Is.EqualTo(1), "actions must be configured before probing");

                // Prime the path once, then measure. This bypasses the system entirely so a
                // registry miss cannot hide an allocation that the snapshot itself performs.
                InteractionTargetSnapshot primed = component.CreateAuthoritySnapshot(0);
                Assert.That(primed.EnabledActionIds, Is.Not.Null, "enabled action ids must be materialised");
                Assert.That(primed.EnabledActionIds.Length, Is.EqualTo(1), "snapshot must carry the configured action");

                const int Iterations = 1000000;
                AllocationProbe probe = AllocationMeter.Measure(Iterations, () =>
                {
                    for (int i = 0; i < Iterations; i++)
                    {
                        component.CreateAuthoritySnapshot(0);
                    }
                });

                var csv = new StressCsvWriter("interactable_snapshot_alloc_v2.csv",
                    "iterations,alloc_bytes_per_call,gen0_delta");
                using (csv)
                {
                    csv.Row(
                        StressCsvWriter.Int(Iterations),
                        StressCsvWriter.Num(probe.BytesPerOperation, 3),
                        StressCsvWriter.Int(probe.ForcedCollectionDelta));
                }
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(go);
            }
        }

        [Test]
        public void Detector_SaturatedPoolStillCollectsHighestPriority()
        {
            ResetSystem();

            // Far more targets than the candidate buffer holds, so the single wide query saturates
            // and the detector must fall back to a per-tier walk to keep the top band.
            const int LowCount = 3000;
            var low = new StubInteractable[LowCount];
            for (int i = 0; i < LowCount; i++)
            {
                low[i] = new StubInteractable { Priority = 0, Position = Vector3.zero };
                _system.Register(low[i]);
            }

            var high = new StubInteractable { Priority = 9, Position = Vector3.zero };
            _system.Register(high);

            int highSlot = -1;
            var probe = new int[8];
            int found = _system.QuerySlots(Vector3.zero, 1f, probe, probe.Length, 1u << 9);
            Assert.That(found, Is.EqualTo(1));
            highSlot = probe[0];

            var detectorObject = new GameObject("DetectorProbe");
            try
            {
                var detector = detectorObject.AddComponent<InteractionDetector>();
                var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

                var radiusField = typeof(InteractionDetector).GetField("detectionRadius", flags);
                Assert.That(radiusField, Is.Not.Null);
                radiusField.SetValue(detector, 1f);

                var systemField = typeof(InteractionDetector).GetField("_system", flags);
                Assert.That(systemField, Is.Not.Null);
                systemField.SetValue(detector, _system);

                var collect = typeof(InteractionDetector).GetMethod("CollectCandidates", flags);
                Assert.That(collect, Is.Not.Null, "CollectCandidates must exist for this probe");

                int count = (int)collect.Invoke(detector, new object[] { Vector3.zero });
                Assert.That(count, Is.GreaterThan(0), "collection must return candidates at all");

                var bufferField = typeof(InteractionDetector).GetField("_slotQueryBuffer", flags);
                var buffer = (int[])bufferField.GetValue(detector);

                bool containsHigh = false;
                for (int i = 0; i < count; i++)
                {
                    if (buffer[i] == highSlot)
                    {
                        containsHigh = true;
                        break;
                    }
                }

                Assert.That(containsHigh, Is.True,
                    "a saturated candidate pool must still surface the highest priority target (count=" + count + ")");
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(detectorObject);
            }
        }

        [Test]
        public void Detector_LosBudgetDominatesFrameCost()
        {
            // maxLosChecksPerFrame defaults to 0 (unlimited), so every candidate costs one
            // Physics.RaycastNonAlloc. This measures what capping the budget actually buys.
            var csv = new StressCsvWriter("detector_los_budget_v2.csv",
                "max_los_checks,candidates,cycles,p50_us,p95_us,p99_us,alloc_bytes_per_cycle,los_checks_per_cycle");
            var flags = System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance;

            try
            {
                foreach (int losBudget in new[] { 0, 8, 32, 64 })
                {
                    ResetSystem();

                    const int Count = 200;
                    for (int i = 0; i < Count; i++)
                    {
                        var stub = new StubInteractable
                        {
                            Priority = 1,
                            Position = new Vector3((i % 20) * 0.4f - 4f, 0f, (i / 20) * 0.4f - 4f)
                        };
                        _system.Register(stub);
                    }

                    var detectorObject = new GameObject("LosBudgetDetector");
                    try
                    {
                        detectorObject.transform.position = Vector3.zero;
                        var detector = detectorObject.AddComponent<InteractionDetector>();

                        typeof(InteractionDetector).GetField("_system", flags).SetValue(detector, _system);
                        typeof(InteractionDetector).GetField("detectionRadius", flags).SetValue(detector, 8f);
                        typeof(InteractionDetector).GetField("maxInteractables", flags).SetValue(detector, 64);
                        typeof(InteractionDetector).GetField("maxLosChecksPerFrame", flags).SetValue(detector, losBudget);
                        typeof(InteractionDetector).GetField("detectionOrigin", flags).SetValue(detector, detectorObject.transform);

                        var normalize = typeof(InteractionDetector).GetMethod("NormalizeSettings", flags);
                        normalize?.Invoke(detector, null);

                        var perform = typeof(InteractionDetector).GetMethod("PerformSpatialHashDetection", flags);
                        Assert.That(perform, Is.Not.Null);

                        // Warm up so JIT and the LOS cache reach steady state.
                        for (int i = 0; i < 20; i++)
                        {
                            perform.Invoke(detector, null);
                        }

                        const int Cycles = 300;
                        var histogram = new LatencyHistogram(10L, 8192);
                        AllocationProbe allocation = AllocationMeter.Measure(50, () =>
                        {
                            for (int i = 0; i < 50; i++)
                            {
                                perform.Invoke(detector, null);
                            }
                        });

                        long start = Stopwatch.GetTimestamp();
                        for (int c = 0; c < Cycles; c++)
                        {
                            long opStart = Stopwatch.GetTimestamp();
                            perform.Invoke(detector, null);
                            histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);
                        }

                        var losCountField = typeof(InteractionDetector).GetField("_losCheckCount", flags);
                        int losPerCycle = losCountField != null ? (int)losCountField.GetValue(detector) : -1;

                        csv.Row(
                            StressCsvWriter.Int(losBudget),
                            StressCsvWriter.Int(Count),
                            StressCsvWriter.Int(Cycles),
                            StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                            StressCsvWriter.Num(histogram.PercentileUs(0.95), 3),
                            StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                            StressCsvWriter.Num(allocation.BytesPerOperation, 2),
                            StressCsvWriter.Int(losPerCycle));
                    }
                    finally
                    {
                        UnityEngine.Object.DestroyImmediate(detectorObject);
                    }
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        private void RunStrategyComparison(StressCsvWriter csv, int scale, int tiers, ulong seed)
        {
            ResetSystem();

            var random = new DeterministicRng(seed ^ (ulong)scale ^ ((ulong)tiers << 32));
            var items = new StubInteractable[scale];
            float extent = MathF.Sqrt(scale) * 6f;

            for (int i = 0; i < scale; i++)
            {
                double r = random.NextDouble();
                float spread = (float)Math.Pow(r, 1.5) * extent * 0.5f;
                double angle = random.NextDouble() * Math.PI * 2.0;
                var item = new StubInteractable
                {
                    Priority = tiers == 1 ? 0 : random.NextInt(tiers),
                    Position = new Vector3((float)(Math.Cos(angle) * spread), 0f, (float)(Math.Sin(angle) * spread))
                };
                items[i] = item;
                _system.Register(item);
            }

            const int MaxResults = 64;
            const int Cycles = 400;
            var slotBuffer = new int[MaxResults];
            var sortKeys = new long[1024];

            foreach (bool tiered in new[] { true, false })
            {
                var histogram = new LatencyHistogram(10L, 8192);
                long collected = 0;
                long mismatches = 0;
                var probe = new DeterministicRng(seed + 17UL);

                AllocationProbe allocation = AllocationMeter.Measure(Math.Min(Cycles, 100), () =>
                {
                    for (int c = 0; c < 100; c++)
                    {
                        QueryOnce(tiered, MaxResults, slotBuffer, sortKeys, ref probe, ref collected);
                    }
                });

                long start = Stopwatch.GetTimestamp();
                for (int c = 0; c < Cycles; c++)
                {
                    long opStart = Stopwatch.GetTimestamp();
                    int count = QueryOnce(tiered, MaxResults, slotBuffer, sortKeys, ref probe, ref collected);
                    histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);

                    if (count > MaxResults) mismatches++;
                }

                double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
                double cyclesPerSecond = elapsedNs <= 0 ? 0 : Cycles / (elapsedNs / 1_000_000_000.0);

                csv.Row(
                    "detector_" + scale + "_tiers_" + tiers,
                    StressCsvWriter.Int((long)seed),
                    StressCsvWriter.Int(scale),
                    StressCsvWriter.Int(tiers),
                    tiered ? "tiered" : "single_plus_sort",
                    StressCsvWriter.Int(MaxResults),
                    StressCsvWriter.Int(Cycles),
                    StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                    StressCsvWriter.Num(histogram.PercentileUs(0.95), 3),
                    StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                    StressCsvWriter.Num(allocation.BytesPerOperation, 2),
                    StressCsvWriter.Num(collected / (double)Cycles, 2),
                    StressCsvWriter.Int(mismatches));

                Assert.That(mismatches, Is.Zero, "strategy returned more results than the budget");
            }
        }

        private int QueryOnce(bool tiered, int maxResults, int[] slotBuffer, long[] sortKeys, ref DeterministicRng probe, ref long collected)
        {
            float cx = probe.NextFloat(-120f, 120f);
            float cz = probe.NextFloat(-120f, 120f);
            var origin = new Vector3(cx, 0f, cz);

            int total;
            if (tiered)
            {
                total = 0;
                uint pending = _system.OccupiedTiers;
                while (pending != 0 && total < maxResults)
                {
                    uint layer = 1u << HighestTier(pending);
                    pending &= ~layer;
                    int budget = maxResults - total;
                    total += _system.QuerySlots(origin, 8f, slotBuffer, total + budget > slotBuffer.Length ? slotBuffer.Length - total : budget, layer);
                }
            }
            else
            {
                int capacity = slotBuffer.Length;
                total = _system.QuerySlots(origin, 8f, slotBuffer, capacity, uint.MaxValue);
                // Ranking key: tier first, then a stable id so ties are deterministic.
                int usable = total < sortKeys.Length ? total : sortKeys.Length;
                for (int i = 0; i < usable; i++)
                {
                    IInteractable item = _system.GetInteractable(slotBuffer[i]);
                    int priority = item?.Priority ?? 0;
                    sortKeys[i] = ((long)priority << 32) | (uint)slotBuffer[i];
                }

                Array.Sort(sortKeys, 0, usable);
                Array.Reverse(sortKeys, 0, usable);
                total = usable;
            }

            collected += total;
            return total;
        }

        private static int HighestTier(uint mask)
        {
            int tier = InteractionSystem.PriorityTierCount - 1;
            while ((mask & (1u << tier)) == 0)
            {
                tier--;
            }

            return tier;
        }

        private void RunUpdateAllocation(StressCsvWriter csv, int count)
        {
            ResetSystem();

            var objects = new List<GameObject>(count);
            var components = new TestInteractable[count];
            var random = new DeterministicRng(9001UL);

            for (int i = 0; i < count; i++)
            {
                var go = new GameObject("Interactable_" + i);
                var component = go.AddComponent<TestInteractable>();
                component.Configure(
                    priority: i % 4,
                    distance: 5f,
                    actions: new[] { new InteractionAction("open", "Open") },
                    stableId: "entity/" + i);
                components[i] = component;
                objects.Add(go);
            }

            // Registration happened in OnEnable; the first update primes the snapshot path.
            for (int i = 0; i < count; i++)
            {
                components[i].transform.position = new Vector3(random.NextFloat(-100f, 100f), 0f, random.NextFloat(-100f, 100f));
                components[i].ForceUpdatePosition();
            }

            int gen0Before = GC.CollectionCount(0);
            AllocationProbe probe = AllocationMeter.Measure(count, () =>
            {
                for (int i = 0; i < count; i++)
                {
                    components[i].transform.position = new Vector3(random.NextFloat(-100f, 100f), 0f, random.NextFloat(-100f, 100f));
                    components[i].ForceUpdatePosition();
                }
            });

            csv.Row(
                "interactable_update_with_actions",
                StressCsvWriter.Int(count),
                StressCsvWriter.Int(count),
                StressCsvWriter.Num(probe.BytesPerOperation, 3),
                StressCsvWriter.Int(GC.CollectionCount(0) - gen0Before),
                "real Interactable, one enabled action each, stable id set");

            for (int i = 0; i < objects.Count; i++)
            {
                UnityEngine.Object.DestroyImmediate(objects[i]);
            }
        }

        private void ResetSystem()
        {
            if (_systemObject != null)
            {
                UnityEngine.Object.DestroyImmediate(_systemObject);
            }

            _systemObject = new GameObject("InteractionPipelineSystem");
            _system = _systemObject.AddComponent<InteractionSystem>();
            _system.Initialize(is2DMode: false, cellSize: 10f);
        }

        /// <summary>Real component so the authority-snapshot path (the known allocation source) runs.</summary>
        private sealed class TestInteractable : Interactable
        {
            public void Configure(int priority, float distance, InteractionAction[] actions, string stableId)
            {
                this.priority = priority;
                this.interactionDistance = distance;
                this.actions = actions;
                this.stableId = stableId;
                InvalidateAuthorityCache();
            }

            public void ForceUpdatePosition()
            {
                NotifyPositionChanged();
            }
        }

        private sealed class StubInteractable : IInteractable
        {
            public int Priority { get; set; }
            public Vector3 Position { get; set; }
            public float InteractionDistance { get; set; } = 10f;
            public InteractionChannel Channel { get; set; } = InteractionChannel.All;

            public string InteractionPrompt => string.Empty;
            public InteractionPromptData? PromptData => null;
            public bool IsInteractable => true;
            public bool AutoInteract => false;
            public bool IsInteracting => false;
            public InteractionStateType CurrentState => InteractionStateType.Idle;
            public IReadOnlyList<IInteractionRequirement> Requirements => Array.Empty<IInteractionRequirement>();
            public float InteractionProgress => 0f;
            public IReadOnlyList<InteractionAction> Actions => Array.Empty<InteractionAction>();
            public InstigatorHandle CurrentInstigator => null;
            public float HoldDuration => 0f;
            public float MaxInteractionRange => 0f;
            public bool IsBusy => false;

            public event Action<IInteractable, InteractionStateType> OnStateChanged { add { } remove { } }
            public event Action<IInteractable, float> OnProgressChanged { add { } remove { } }
            public event Action<IInteractable, InteractionCancelReason> OnInteractionCancelled { add { } remove { } }

            public UniTask<bool> TryInteractAsync(CancellationToken cancellationToken = default) => UniTask.FromResult(false);
            public UniTask<bool> TryInteractAsync(string actionId, CancellationToken cancellationToken = default) => UniTask.FromResult(false);
            public UniTask<bool> TryInteractAsync(InstigatorHandle instigator, string actionId, CancellationToken cancellationToken = default) => UniTask.FromResult(false);

            public bool CanInteract(InstigatorHandle instigator) => true;
            public void OnFocus() { }
            public void OnDefocus() { }
            public void ForceEndInteraction(InteractionCancelReason reason = InteractionCancelReason.Manual) { }
        }
    }
}
