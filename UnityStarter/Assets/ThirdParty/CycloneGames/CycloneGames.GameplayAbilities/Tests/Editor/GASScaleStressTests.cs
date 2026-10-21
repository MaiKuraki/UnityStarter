using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Extreme-scale stress suite modelling a 64-player open-world area: 264 ticking
    /// AbilitySystemComponents (64 players + 200 NPCs), 128 attribute categories each, roguelike-style
    /// stacked duration effects, live (non-snapshot) attribute dependencies, and a burst/pulse phase.
    ///
    /// Deterministic assertions: exact modifier-hit accounting, zero managed allocation in steady
    /// state, single coalesced aggregation pass under burst. Timing breakdown (aggregation /
    /// dependents scan / inhibition refresh) is logged via the editor-only GASAggregationProbe.
    /// </summary>
    public sealed class GASScaleStressTests
    {
        private const float DeltaTime = 1f / 60f;
        private const int EntityCount = 264;         // 64 players + 200 NPCs in one area
        private const int AttributeCount = 128;      // 100+ attribute categories
        private const int DurationEffectsPerEntity = 24;
        private const int ModifiersPerEffect = 16;
        private const int RealisticDirtyAttributes = 8;
        private const int RealisticTicks = 60;
        private const int CeilingTicks = 30;
        private static readonly GameplayEffect[] EmptyPeriodicDrivers = new GameplayEffect[0];

        [Test]
        public void OpenWorld_264Entities_128Attributes_RealisticLoad()
        {
            List<ScaleEntity> entities = BuildPopulation(out _);
            try
            {
                for (int t = 0; t < 10; t++) TickAll(entities); // warmup
                GASAggregationProbe.Reset();

                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long totalTickTimestamps = MeasureTicks(entities, RealisticTicks);
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                long expectedHits = (long)EntityCount * (RealisticDirtyAttributes + ModifiersPerEffect) * 3 * RealisticTicks;
                LogBreakdown("Realistic", expectedHits, totalTickTimestamps, allocatedBytes);

                Assert.That(GASAggregationProbe.ModifierHits, Is.EqualTo(expectedHits),
                    "Every dirty attribute (periodic subset + live-dependency targets) must aggregate exactly its three matching modifiers per tick.");
                Assert.That(GASAggregationProbe.ModifierVisits, Is.EqualTo(GASAggregationProbe.ModifierHits));
                GASAllocationProbe.AssertNoMeasuredAllocation(allocatedBytes, "Steady-state open-world ticking must not allocate managed memory.");
            }
            finally
            {
                DisposeAll(entities);
            }
        }

        [Test]
        public void OpenWorld_264Entities_128Attributes_FullDirtyCeiling()
        {
            List<ScaleEntity> entities = BuildPopulation(out GameplayEffect[] ceilingDrivers, ceiling: true);
            try
            {
                for (int t = 0; t < 5; t++) TickAll(entities);
                GASAggregationProbe.Reset();

                long totalTickTimestamps = MeasureTicks(entities, CeilingTicks);

                long expectedHits = (long)EntityCount * AttributeCount * 3 * CeilingTicks;
                LogBreakdown("Ceiling ", expectedHits, totalTickTimestamps, 0L);

                Assert.That(GASAggregationProbe.ModifierHits, Is.EqualTo(expectedHits),
                    "Full-dirty ceiling must aggregate exactly three matching modifiers per attribute per tick.");
            }
            finally
            {
                DisposeAll(entities);
            }
        }

        [Test]
        public void OpenWorld_264Entities_AllIdle_PeacefulBaseline()
        {
            List<ScaleEntity> entities = BuildPopulation(out _, dirtyEntityCount: 0);
            try
            {
                for (int t = 0; t < 10; t++)
                {
                    TickAll(entities);
                }

                GASAggregationProbe.Reset();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long totalTickTimestamps = MeasureTicks(entities, 300);
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                double perTickMs = totalTickTimestamps * 1000.0 / Stopwatch.Frequency / 300;

                UnityEngine.Debug.Log(
                    $"[ScaleStress][AllIdle] entities={EntityCount} effects={DurationEffectsPerEntity} ticks=300 | " +
                    $"perTick={perTickMs:F3}ms recalcPasses={GASAggregationProbe.RecalculatePasses} " +
                    $"hits={GASAggregationProbe.ModifierHits} allocatedBytes={allocatedBytes}");

                Assert.Zero(GASAggregationProbe.ModifierHits, "Idle entities (nothing dirtied) must not aggregate.");
                GASAllocationProbe.AssertNoMeasuredAllocation(allocatedBytes, "Idle steady state must not allocate managed memory.");
            }
            finally
            {
                DisposeAll(entities);
            }
        }

        [Test]
        public void OpenWorld_264Entities_QuarterInCombat_MixedDuty()
        {
            const int combatEntities = EntityCount / 4; // 66 in combat, 198 idle
            List<ScaleEntity> entities = BuildPopulation(out _, dirtyEntityCount: combatEntities);
            try
            {
                for (int t = 0; t < 10; t++)
                {
                    TickAll(entities);
                }

                GASAggregationProbe.Reset();
                long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                long totalTickTimestamps = MeasureTicks(entities, RealisticTicks);
                long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

                long expectedHits = (long)combatEntities * (RealisticDirtyAttributes + ModifiersPerEffect) * 3 * RealisticTicks;
                double perTickMs = totalTickTimestamps * 1000.0 / Stopwatch.Frequency / RealisticTicks;

                UnityEngine.Debug.Log(
                    $"[ScaleStress][MixedDuty] total={EntityCount} combat={combatEntities} idle={EntityCount - combatEntities} | " +
                    $"perTick={perTickMs:F2}ms hits={GASAggregationProbe.ModifierHits} (expected {expectedHits}) allocatedBytes={allocatedBytes}");

                Assert.That(GASAggregationProbe.ModifierHits, Is.EqualTo(expectedHits),
                    "Mixed duty must aggregate exactly the combat entities' dirty attributes.");
                GASAllocationProbe.AssertNoMeasuredAllocation(allocatedBytes, "Mixed duty steady state must not allocate managed memory.");
            }
            finally
            {
                DisposeAll(entities);
            }
        }

        [Test]
        public void EffectCountSweep_PerEntityActiveEffects_LinearAndZeroAlloc()
        {
            // Content breadth (how many distinct effect TYPES exist) has no per-tick cost: the tick
            // path only iterates active effect instances, never the definition space. This sweep
            // measures the dimension that does scale: ACTIVE effects per entity (16..256).
            int[] effectCounts = { 16, 64, 128, 256 };
            const int entitiesPerCombo = 64;
            const int ticks = 30;

            var report = new System.Text.StringBuilder();
            foreach (int effectCount in effectCounts)
            {
                GameplayEffect[] durationEffects = BuildUniformEffects(effectCount);
                GameplayEffect periodic = BuildPeriodicDriver($"Scale.Sweep.Periodic.{effectCount}", 0, RealisticDirtyAttributes);

                var entities = new List<ScaleEntity>(entitiesPerCombo);
                for (int i = 0; i < entitiesPerCombo; i++)
                {
                    entities.Add(new ScaleEntity(i, durationEffects, new[] { periodic }));
                }

                try
                {
                    for (int t = 0; t < 10; t++)
                    {
                        TickAll(entities);
                    }

                    GASAggregationProbe.Reset();
                    long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                    long totalTickTimestamps = MeasureTicks(entities, ticks);
                    long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                    double perTickMs = totalTickTimestamps * 1000.0 / Stopwatch.Frequency / ticks;

                    long expectedHits = (long)entitiesPerCombo * RealisticDirtyAttributes * (effectCount / 16) * ticks;
                    report.AppendLine(
                        $"  effects/entity={effectCount,3} slots={effectCount * 8,4} perTick={perTickMs:F2}ms " +
                        $"hits={GASAggregationProbe.ModifierHits} (expected {expectedHits}) alloc={allocatedBytes}");

                    Assert.That(GASAggregationProbe.ModifierHits, Is.EqualTo(expectedHits),
                        $"Hit accounting mismatch at {effectCount} active effects per entity.");
                    GASAllocationProbe.AssertNoMeasuredAllocation(allocatedBytes, $"Steady state must not allocate at {effectCount} active effects per entity.");
                }
                finally
                {
                    DisposeAll(entities);
                }
            }

            UnityEngine.Debug.Log($"[ScaleStress][EffectSweep] per-entity active effects (entities={entitiesPerCombo}, ticks={ticks}):\n{report}");
        }

        private static GameplayEffect[] BuildUniformEffects(int count)
        {
            var effects = new GameplayEffect[count];
            for (int j = 0; j < count; j++)
            {
                var modifiers = new List<ModifierInfo>(8);
                for (int m = 0; m < 8; m++)
                {
                    modifiers.Add(new ModifierInfo(
                        $"Attr{(j * 8 + m) % AttributeCount}",
                        EAttributeModifierOperation.Add,
                        new ScalableFloat(1f)));
                }
                effects[j] = new GameplayEffect($"Scale.Sweep.Effect.{count}.{j}", EDurationPolicy.Infinite, modifiers: modifiers);
            }
            return effects;
        }

        [Test]
        public void Burst_PulseOfHundredsOfEffects_CoalescesIntoOneAggregationPass()
        {
            const int burstEffects = 300;
            var entity = new ScaleEntity();
            try
            {
                entity.Asc.Tick(DeltaTime, isServer: true); // register owner thread

                // Pulse phase: apply 300 distinct stacking effects inside one frame window.
                var burstDefs = new GameplayEffect[burstEffects];
                for (int i = 0; i < burstEffects; i++)
                {
                    var modifiers = new List<ModifierInfo>(ModifiersPerEffect);
                    for (int m = 0; m < ModifiersPerEffect; m++)
                    {
                        modifiers.Add(new ModifierInfo(
                            $"Attr{(i + m) % AttributeCount}",
                            EAttributeModifierOperation.Add,
                            new ScalableFloat(1f)));
                    }
                    burstDefs[i] = new GameplayEffect(
                        $"Scale.Burst.{i}",
                        EDurationPolicy.HasDuration,
                        duration: 30f,
                        modifiers: modifiers);
                }

                long burstApplyBefore = GC.GetAllocatedBytesForCurrentThread();
                for (int i = 0; i < burstEffects; i++)
                {
                    GameplayEffectApplicationResult result = entity.Asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(burstDefs[i], entity.Asc));
                    Assert.That(result.Succeeded, Is.True, $"Burst effect {i} failed to apply.");
                }
                long burstApplyAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - burstApplyBefore;

                // Absorption tick: all dirtied attributes coalesce into a single aggregation pass.
                GASAggregationProbe.Reset();
                long tickAllocBefore = GC.GetAllocatedBytesForCurrentThread();
                long tickTimestamp = Stopwatch.GetTimestamp();
                entity.Asc.Tick(DeltaTime, isServer: true);
                long tickTicks = Stopwatch.GetTimestamp() - tickTimestamp;
                long tickAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - tickAllocBefore;

                double tickMs = tickTicks * 1000.0 / Stopwatch.Frequency;
                UnityEngine.Debug.Log(
                    $"[ScaleStress][Burst] effects={burstEffects} burstApplyAllocatedBytes={burstApplyAllocatedBytes} | " +
                    $"absorptionTick={tickMs:F2}ms tickAllocatedBytes={tickAllocatedBytes} " +
                    $"recalcPasses={GASAggregationProbe.RecalculatePasses} " +
                    $"dirtyAttrs={GASAggregationProbe.DirtyAttributesProcessed} " +
                    $"dependentsScanSlots={GASAggregationProbe.DependentsScanSlotVisits}");

                GASAllocationProbe.AssertNoMeasuredAllocation(tickAllocatedBytes, "The burst absorption tick must not allocate managed memory.");
                Assert.That(GASAggregationProbe.RecalculatePasses, Is.LessThanOrEqualTo(2),
                    "A burst frame must coalesce into at most the two standard aggregation passes, not one per effect.");

                // Removal pulse: remove everything in one frame.
                long removeBefore = GC.GetAllocatedBytesForCurrentThread();
                var snapshot = new List<ActiveGameplayEffect>(entity.Asc.ActiveEffects.Count);
                for (int i = 0; i < entity.Asc.ActiveEffects.Count; i++)
                {
                    snapshot.Add(entity.Asc.ActiveEffects[i]);
                }
                for (int i = 0; i < snapshot.Count; i++)
                {
                    entity.Asc.TryRemoveActiveEffect(snapshot[i]);
                }
                long removeAllocatedBytes = GC.GetAllocatedBytesForCurrentThread() - removeBefore;
                UnityEngine.Debug.Log(
                    $"[ScaleStress][Burst] removal pulse: removed={snapshot.Count} removeAllocatedBytes={removeAllocatedBytes}");
            }
            finally
            {
                entity.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static long MeasureTicks(List<ScaleEntity> entities, int tickCount)
        {
            long total = 0;
            for (int t = 0; t < tickCount; t++)
            {
                long start = Stopwatch.GetTimestamp();
                TickAll(entities);
                total += Stopwatch.GetTimestamp() - start;
            }
            return total;
        }

        private static void LogBreakdown(string label, long expectedHits, long totalTickTimestamps, long allocatedBytes)
        {
            double frequency = Stopwatch.Frequency;
            double totalTickMs = totalTickTimestamps * 1000.0 / frequency;
            double perTickMs = totalTickMs / (label == "Ceiling " ? CeilingTicks : RealisticTicks);
            double aggregationMs = GASAggregationProbe.AggregationTimestampTicks * 1000.0 / frequency;
            double dependentsMs = GASAggregationProbe.DependentsScanTimestampTicks * 1000.0 / frequency;
            double inhibitionMs = GASAggregationProbe.InhibitionRefreshTimestampTicks * 1000.0 / frequency;

            UnityEngine.Debug.Log(
                $"[ScaleStress][{label}] entities={EntityCount} attrs={AttributeCount} effects={DurationEffectsPerEntity} | " +
                $"totalTick={totalTickMs:F1}ms perTick={perTickMs:F2}ms | " +
                $"aggregation={aggregationMs:F1}ms dependentsScan={dependentsMs:F1}ms (slots={GASAggregationProbe.DependentsScanSlotVisits}) " +
                $"inhibition={inhibitionMs:F1}ms | hits={GASAggregationProbe.ModifierHits} (expected {expectedHits}) " +
                $"recalcPasses={GASAggregationProbe.RecalculatePasses} allocatedBytes={allocatedBytes}");
        }

        private static void TickAll(List<ScaleEntity> entities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                entities[i].Asc.Tick(DeltaTime, isServer: true);
            }
        }

        private static void DisposeAll(List<ScaleEntity> entities)
        {
            for (int i = 0; i < entities.Count; i++)
            {
                entities[i].Dispose();
            }
        }

        private static List<ScaleEntity> BuildPopulation(out GameplayEffect[] periodicDrivers, bool ceiling = false, int dirtyEntityCount = EntityCount)
        {
            // Shared definitions: 24 duration effects x 16 modifiers = 384 slots over 128 attributes,
            // so every attribute is aggregated from exactly three effects.
            GameplayEffect[] durationEffects = new GameplayEffect[DurationEffectsPerEntity];
            for (int k = 0; k < DurationEffectsPerEntity; k++)
            {
                var modifiers = new List<ModifierInfo>(ModifiersPerEffect);
                bool liveDependencyEffect = k == DurationEffectsPerEntity - 1; // one effect computes from a live attribute
                for (int j = 0; j < ModifiersPerEffect; j++)
                {
                    int attributeIndex = (k * ModifiersPerEffect + j) % AttributeCount;
                    if (liveDependencyEffect)
                    {
                        modifiers.Add(new ModifierInfo(
                            $"Attr{attributeIndex}",
                            EAttributeModifierOperation.Add,
                            new AttributeBasedMagnitude(
                                "Attr0",
                                EGameplayEffectAttributeCaptureSource.Target,
                                EAttributeBasedFloatCalculationType.AttributeMagnitude,
                                new ScalableFloat(1f),
                                new ScalableFloat(0f),
                                new ScalableFloat(0f),
                                EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)));
                    }
                    else
                    {
                        modifiers.Add(new ModifierInfo(
                            $"Attr{attributeIndex}",
                            EAttributeModifierOperation.Add,
                            new ScalableFloat(1f)));
                    }
                }

                durationEffects[k] = new GameplayEffect($"Scale.Duration.{k}", EDurationPolicy.Infinite, modifiers: modifiers);
            }

            // Periodic drivers dirty the realistic subset (or every attribute for the ceiling).
            if (ceiling)
            {
                periodicDrivers = new GameplayEffect[2];
                periodicDrivers[0] = BuildPeriodicDriver("Scale.Periodic.A", 0, 64);
                periodicDrivers[1] = BuildPeriodicDriver("Scale.Periodic.B", 64, 64);
            }
            else
            {
                periodicDrivers = new GameplayEffect[1];
                periodicDrivers[0] = BuildPeriodicDriver("Scale.Periodic.Realistic", 0, RealisticDirtyAttributes);
            }

            var entities = new List<ScaleEntity>(EntityCount);
            for (int i = 0; i < EntityCount; i++)
            {
                // Entities beyond dirtyEntityCount receive no periodic driver: they hold their
                // effects but nothing re-dirties their attributes, modelling peaceful/idle zones.
                entities.Add(new ScaleEntity(i, durationEffects, i < dirtyEntityCount ? periodicDrivers : EmptyPeriodicDrivers));
            }
            return entities;
        }

        private static GameplayEffect BuildPeriodicDriver(string name, int startAttribute, int count)
        {
            var modifiers = new List<ModifierInfo>(count);
            for (int a = 0; a < count; a++)
            {
                modifiers.Add(new ModifierInfo($"Attr{startAttribute + a}", EAttributeModifierOperation.Add, new ScalableFloat(1f)));
            }
            return new GameplayEffect(name, EDurationPolicy.Infinite, period: DeltaTime, modifiers: modifiers);
        }

        private sealed class ScaleEntity : IDisposable
        {
            internal readonly AbilitySystemComponent Asc;
            internal readonly ScaleAttributeSet Attributes;

            internal ScaleEntity(int index, GameplayEffect[] durationEffects, GameplayEffect[] periodicDrivers)
            {
                Asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                Attributes = new ScaleAttributeSet();
                Asc.AddAttributeSet(Attributes);
                for (int a = 0; a < AttributeCount; a++)
                {
                    Attributes.ValueOf(a).SetBaseValue(100f);
                }

                for (int k = 0; k < durationEffects.Length; k++)
                {
                    GameplayEffectApplicationResult result = Asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(durationEffects[k], Asc));
                    Assert.That(result.Succeeded, Is.True, $"Duration effect {k} failed for entity {index}.");
                }
                for (int p = 0; p < periodicDrivers.Length; p++)
                {
                    GameplayEffectApplicationResult result = Asc.ApplyGameplayEffectSpecToSelf(
                        GameplayEffectSpec.Create(periodicDrivers[p], Asc));
                    Assert.That(result.Succeeded, Is.True, $"Periodic driver {p} failed for entity {index}.");
                }
            }

            internal ScaleEntity()
            {
                // Bare entity for the burst test: attributes only, no pre-applied effects.
                Asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                Attributes = new ScaleAttributeSet();
                Asc.AddAttributeSet(Attributes);
                for (int a = 0; a < AttributeCount; a++)
                {
                    Attributes.ValueOf(a).SetBaseValue(100f);
                }
            }

            public void Dispose()
            {
                Asc.Dispose();
            }
        }

        private sealed class ScaleAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute[] _attributes = new GameplayAttribute[AttributeCount];

            internal ScaleAttributeSet()
            {
                for (int a = 0; a < AttributeCount; a++)
                {
                    _attributes[a] = new GameplayAttribute($"Attr{a}");
                }
            }

            internal GameplayAttribute ValueOf(int index) => _attributes[index];

            protected override void RegisterAttributes()
            {
                for (int a = 0; a < AttributeCount; a++)
                {
                    RegisterAttribute(_attributes[a]);
                }
            }
        }
    }
}
