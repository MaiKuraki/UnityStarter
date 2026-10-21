using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Cross-directional permutation, boundary and reentrancy suite:
    /// 1. Aggregation formula oracle sweep (operation x evaluation channel x stack count x base value)
    ///    and a multi-channel ordering case, each compared against an independently derived result.
    ///    NOTE: the oracle encodes the framework's current product semantics for Multiply/Division
    ///    combination (M1*M2, M^stack). UE's stock aggregator uses bias-sum (1 + sum(Mi-1)); this is a
    ///    deliberate, documented divergence - see the UE alignment section of the audit report.
    /// 2. Cross-feature interaction: ongoing-tag-requirement inhibition gating aggregated values.
    /// 3. Boundary / hostile inputs: invalid delta time, over-limit modifier counts, extreme magnitudes.
    /// 4. Reentrancy: applying an effect from inside an attribute observer callback.
    /// 5. Performance sweep across {effect count} x {dirty attribute count} combinations with exact
    ///    hit accounting and zero-allocation assertions.
    /// </summary>
    public sealed class GASPermutationAndBoundaryTests
    {
        private const float DeltaTime = 1f / 60f;

        // ---------------------------------------------------------------------
        // 1. Aggregation formula oracle sweep
        // ---------------------------------------------------------------------

        [Test]
        public void AggregationFormula_OracleSweep_OperationChannelStackBase()
        {
            EAttributeModifierOperation[] operations =
            {
                EAttributeModifierOperation.Add,
                EAttributeModifierOperation.Multiply,
                EAttributeModifierOperation.Division,
                EAttributeModifierOperation.Override
            };
            GASModifierEvaluationChannel[] channels =
            {
                GASModifierEvaluationChannel.Channel0,
                GASModifierEvaluationChannel.Channel1,
                GASModifierEvaluationChannel.Channel2
            };
            int[] stackCounts = { 1, 2, 3 };
            int[] baseValues = { 100, 1000 };

            int cases = 0;
            foreach (EAttributeModifierOperation operation in operations)
            {
                foreach (GASModifierEvaluationChannel channel in channels)
                {
                    foreach (int stackCount in stackCounts)
                    {
                        foreach (int baseValue in baseValues)
                        {
                            VerifySingleModifierCase(operation, channel, stackCount, baseValue);
                            cases++;
                        }
                    }
                }
            }

            UnityEngine.Debug.Log($"[Permutation][Formula] PASS: {cases} oracle cases (op x channel x stack x base) all matched.");
        }

        private static void VerifySingleModifierCase(
            EAttributeModifierOperation operation,
            GASModifierEvaluationChannel channel,
            int stackCount,
            int baseValue)
        {
            int factor = operation switch
            {
                EAttributeModifierOperation.Add => 7,
                EAttributeModifierOperation.Multiply => 2,
                EAttributeModifierOperation.Division => 4,
                _ => 500
            };

            GameplayEffect effect = BuildStackingEffect(
                $"Permutation.Formula.{operation}.{(int)channel}.{stackCount}.{baseValue}",
                new ModifierInfo("Attr0", operation, new ScalableFloat(factor), channel),
                stackLimit: 10);

            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(baseValue);
                for (int i = 0; i < stackCount; i++)
                {
                    Assert.That(
                        asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded,
                        Is.True, $"Stack application {i} failed for {effect.Name}.");
                }

                asc.Tick(DeltaTime, isServer: true);

                // Independently derived expectation using the documented per-channel semantics.
                var expected = GASFixedValue.FromInt(baseValue);
                switch (operation)
                {
                    case EAttributeModifierOperation.Add:
                        expected += GASFixedValue.FromInt(factor) * GASFixedValue.FromInt(stackCount);
                        break;
                    case EAttributeModifierOperation.Multiply:
                        for (int i = 0; i < stackCount; i++) expected *= GASFixedValue.FromInt(factor);
                        break;
                    case EAttributeModifierOperation.Division:
                        for (int i = 0; i < stackCount; i++) expected /= GASFixedValue.FromInt(factor);
                        break;
                    case EAttributeModifierOperation.Override:
                        expected = GASFixedValue.FromInt(factor);
                        break;
                }

                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(expected.RawValue),
                    $"Oracle mismatch for {operation} channel={(int)channel} stack={stackCount} base={baseValue}. " +
                    $"expected={expected.RawValue} actual={attributes.ValueOf(0).CurrentValueRaw}");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void AggregationFormula_MultiChannelAndSameChannelOrdering()
        {
            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100);

                // Channel 0: Add +5 then Add +7 ; Channel 1: Multiply x2, x2 ; Channel 2: Division /4.
                var effect = new GameplayEffect(
                    "Permutation.MultiChannel",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(5f), GASModifierEvaluationChannel.Channel0),
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(7f), GASModifierEvaluationChannel.Channel0),
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(2f), GASModifierEvaluationChannel.Channel1),
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Multiply, new ScalableFloat(2f), GASModifierEvaluationChannel.Channel1),
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Division, new ScalableFloat(4f), GASModifierEvaluationChannel.Channel2)
                    });
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);
                asc.Tick(DeltaTime, isServer: true);

                // Oracle: ((100 + 5 + 7) * 2 * 2) / 4 = 112.
                var expected = GASFixedValue.FromInt(100);
                expected += GASFixedValue.FromInt(5);
                expected += GASFixedValue.FromInt(7);
                expected *= GASFixedValue.FromInt(2);
                expected *= GASFixedValue.FromInt(2);
                expected /= GASFixedValue.FromInt(4);

                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(expected.RawValue),
                    $"Multi-channel oracle mismatch. expected={expected.RawValue} actual={attributes.ValueOf(0).CurrentValueRaw}");
                UnityEngine.Debug.Log($"[Permutation][Formula] PASS: multi-channel oracle matched, value={expected.RawValue}.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 2. Cross-feature: inhibition gates aggregation
        // ---------------------------------------------------------------------

        [Test]
        public void Inhibition_TagRequirementTransitions_GateAggregatedValue()
        {
            const string requiredTagName = "Permutation.Inhibition.Required";
            GameplayTagManager.RegisterDynamicTag(requiredTagName, "permutation inhibition test");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag requiredTag = GameplayTagManager.Request(requiredTagName);

            var required = new GameplayTagContainer();
            required.AddTag(requiredTag);
            var forbidden = new GameplayTagContainer();

            var effect = new GameplayEffect(
                "Permutation.Inhibition",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(10f))
                },
                ongoingTagRequirements: new GameplayTagRequirements(forbidden, required));

            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                attributes.ValueOf(0).SetBaseValue(100);
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);

                // Without the required tag the effect is inhibited: no contribution.
                asc.Tick(DeltaTime, isServer: true);
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue),
                    "Inhibited effect must not contribute to the aggregated value.");

                // Granting the tag lifts inhibition; the contribution appears.
                asc.AddLooseGameplayTag(requiredTag);
                asc.Tick(DeltaTime, isServer: true);
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(110).RawValue),
                    "Uninhibited effect must contribute to the aggregated value.");

                // Removing the tag restores inhibition.
                asc.RemoveLooseGameplayTag(requiredTag);
                asc.Tick(DeltaTime, isServer: true);
                Assert.That(attributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue),
                    "Re-inhibited effect must stop contributing.");

                UnityEngine.Debug.Log("[Permutation][Inhibition] PASS: inhibited -> uninhibited -> inhibited transitions gate the value correctly.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 3. Boundary / hostile inputs
        // ---------------------------------------------------------------------

        [Test]
        public void Boundary_InvalidDeltaTime_Rejected()
        {
            var asc = CreateAsc(out _);
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => asc.Tick(float.NaN, isServer: true));
                Assert.Throws<ArgumentOutOfRangeException>(() => asc.Tick(float.PositiveInfinity, isServer: true));
                Assert.Throws<ArgumentOutOfRangeException>(() => asc.Tick(-0.01f, isServer: true));
                asc.Tick(0f, isServer: true); // zero is legal
                UnityEngine.Debug.Log("[Permutation][Boundary] PASS: NaN/Infinity/negative delta time rejected; zero accepted.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void Boundary_ModifierCountOverLimit_RejectedAtConstruction()
        {
            var modifiers = new List<ModifierInfo>(GameplayEffect.MaxModifierCount + 1);
            for (int i = 0; i <= GameplayEffect.MaxModifierCount; i++)
            {
                modifiers.Add(new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f)));
            }

            Assert.Throws<ArgumentException>(() => new GameplayEffect("Permutation.OverLimit", EDurationPolicy.Instant, modifiers: modifiers));
            UnityEngine.Debug.Log($"[Permutation][Boundary] PASS: {GameplayEffect.MaxModifierCount + 1} modifiers rejected at construction (limit {GameplayEffect.MaxModifierCount}).");
        }

        [Test]
        public void Boundary_ExtremeMagnitudes_DeterministicAcrossInstances()
        {
            long nearMaxBase = long.MaxValue / 2;
            var effect = new GameplayEffect(
                "Permutation.Extreme",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1e9f))
                });

            var first = CreateAsc(out PermutationAttributeSet firstAttributes);
            var second = CreateAsc(out PermutationAttributeSet secondAttributes);
            try
            {
                firstAttributes.ValueOf(0).SetBaseValueRawUnchecked(nearMaxBase);
                secondAttributes.ValueOf(0).SetBaseValueRawUnchecked(nearMaxBase);
                Assert.That(first.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, first)).Succeeded, Is.True);
                Assert.That(second.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, second)).Succeeded, Is.True);
                first.Tick(DeltaTime, isServer: true);
                second.Tick(DeltaTime, isServer: true);

                Assert.That(secondAttributes.ValueOf(0).CurrentValueRaw, Is.EqualTo(firstAttributes.ValueOf(0).CurrentValueRaw),
                    "Extreme magnitudes must resolve bit-identically across instances (no float drift, no exceptions).");
                UnityEngine.Debug.Log(
                    $"[Permutation][Boundary] PASS: extreme-magnitude result bit-identical across instances, raw={firstAttributes.ValueOf(0).CurrentValueRaw}.");
            }
            finally
            {
                first.Dispose();
                second.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 4. Reentrancy: apply from inside an observer callback
        // ---------------------------------------------------------------------

        [Test]
        public void Reentrancy_ApplyInsideObserverCallback_RemainsControlled()
        {
            var instant = new GameplayEffect(
                "Permutation.Reentrancy.Instant",
                EDurationPolicy.Instant,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                });

            var asc = CreateAsc(out PermutationAttributeSet attributes);
            try
            {
                int callbackInvocations = 0;
                GameplayEffectApplicationResult observedResult = default;
                Action<float, float> observer = (_, _) =>
                {
                    callbackInvocations++;
                    if (callbackInvocations == 1)
                    {
                        // Nested apply from inside the dispatch; the outcome must be deterministic.
                        observedResult = asc.ApplyGameplayEffectSpecToSelf(
                            GameplayEffectSpec.Create(instant, asc));
                    }
                };
                attributes.ValueOf(0).OnCurrentValueChanged += observer;

                var driver = new GameplayEffect(
                    "Permutation.Reentrancy.Driver",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Attr0", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                    });
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(driver, asc)).Succeeded, Is.True);
                asc.Tick(DeltaTime, isServer: true);

                Assert.That(callbackInvocations, Is.GreaterThan(0), "Observer must have been invoked (harness sanity).");

                Assert.That(observedResult.Code, Is.EqualTo(GameplayEffectApplicationResultCode.Executed),
                    "An apply issued from an attribute observer runs outside the locked mutation scope.");

                // The component must remain consistent and usable afterwards.
                attributes.ValueOf(0).OnCurrentValueChanged -= observer;
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(instant, asc)).Succeeded, Is.True);
                asc.Tick(DeltaTime, isServer: true);
            }
            finally
            {
                asc.Dispose();
            }
        }

        // ---------------------------------------------------------------------
        // 5. Performance sweep: effect count x dirty attribute count
        // ---------------------------------------------------------------------

        [Test]
        public void PerformanceSweep_EffectCountByDirtyCount_LinearAndZeroAlloc()
        {
            int[] effectCounts = { 16, 32, 64 };
            int[] dirtyCounts = { 8, 64, 128 };
            const int entities = 32;
            const int ticks = 30;

            var report = new System.Text.StringBuilder();
            foreach (int effectCount in effectCounts)
            {
                foreach (int dirtyCount in dirtyCounts)
                {
                    var entitiesList = BuildSweepPopulation(effectCount, dirtyCount, entities, out GameplayEffect[] periodicDrivers);
                    try
                    {
                        for (int t = 0; t < 5; t++)
                        {
                            for (int e = 0; e < entitiesList.Count; e++) entitiesList[e].Asc.Tick(DeltaTime, isServer: true);
                        }

                        GASAggregationProbe.Reset();
                        long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
                        long totalTicks = 0;
                        for (int t = 0; t < ticks; t++)
                        {
                            long start = Stopwatch.GetTimestamp();
                            for (int e = 0; e < entitiesList.Count; e++) entitiesList[e].Asc.Tick(DeltaTime, isServer: true);
                            totalTicks += Stopwatch.GetTimestamp() - start;
                        }
                        long allocated = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;
                        double perTickMs = totalTicks * 1000.0 / Stopwatch.Frequency / ticks;

                        long expectedHits = (long)entities * dirtyCount * (effectCount / 16) * ticks;
                        report.AppendLine(
                            $"  effects={effectCount,3} dirty={dirtyCount,3} perTick={perTickMs:F3}ms hits={GASAggregationProbe.ModifierHits} (expected {expectedHits}) alloc={allocated}");

                        Assert.That(GASAggregationProbe.ModifierHits, Is.EqualTo(expectedHits),
                            $"Hit accounting mismatch at effects={effectCount} dirty={dirtyCount}.");
                        GASAllocationProbe.AssertNoMeasuredAllocation(allocated, $"Steady-state sweep must not allocate (effects={effectCount} dirty={dirtyCount}).");
                    }
                    finally
                    {
                        for (int e = 0; e < entitiesList.Count; e++) entitiesList[e].Dispose();
                    }
                }
            }

            UnityEngine.Debug.Log($"[Permutation][Sweep] effectCount x dirtyCount grid (entities={entities}, ticks={ticks}):\n{report}");
        }

        // ---------------------------------------------------------------------
        // Harness
        // ---------------------------------------------------------------------

        private static GameplayEffect BuildStackingEffect(string name, ModifierInfo modifier, int stackLimit)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo> { modifier },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    stackLimit,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));
        }

        private static AbilitySystemComponent CreateAsc(out PermutationAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new PermutationAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private static List<SweepEntity> BuildSweepPopulation(int effectCount, int dirtyCount, int entityCount, out GameplayEffect[] periodicDrivers)
        {
            // Uniform coverage: effect j covers attributes [j*8, j*8+8) mod 128, so each attribute
            // is aggregated from effectCount/16 effects (16, 32, 64 -> 1, 2, 4 hits per attribute).
            var effects = new GameplayEffect[effectCount];
            for (int j = 0; j < effectCount; j++)
            {
                var modifiers = new List<ModifierInfo>(8);
                for (int m = 0; m < 8; m++)
                {
                    modifiers.Add(new ModifierInfo(
                        $"Attr{(j * 8 + m) % 128}",
                        EAttributeModifierOperation.Add,
                        new ScalableFloat(1f)));
                }
                effects[j] = new GameplayEffect($"Sweep.Effect.{effectCount}.{j}", EDurationPolicy.Infinite, modifiers: modifiers);
            }

            // Periodic drivers dirty the requested subset of attributes each tick.
            var drivers = new List<GameplayEffect>(2);
            int remaining = dirtyCount;
            int start = 0;
            while (remaining > 0)
            {
                int batch = Math.Min(64, remaining);
                var modifiers = new List<ModifierInfo>(batch);
                for (int a = 0; a < batch; a++)
                {
                    modifiers.Add(new ModifierInfo($"Attr{start + a}", EAttributeModifierOperation.Add, new ScalableFloat(1f)));
                }
                drivers.Add(new GameplayEffect($"Sweep.Periodic.{effectCount}.{dirtyCount}.{start}", EDurationPolicy.Infinite, period: DeltaTime, modifiers: modifiers));
                start += batch;
                remaining -= batch;
            }
            periodicDrivers = drivers.ToArray();

            var entities = new List<SweepEntity>(entityCount);
            for (int i = 0; i < entityCount; i++)
            {
                entities.Add(new SweepEntity(effects, periodicDrivers));
            }
            return entities;
        }

        private sealed class SweepEntity : IDisposable
        {
            internal readonly AbilitySystemComponent Asc;

            internal SweepEntity(GameplayEffect[] effects, GameplayEffect[] periodicDrivers)
            {
                Asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                var attributes = new PermutationAttributeSet();
                Asc.AddAttributeSet(attributes);
                for (int a = 0; a < 128; a++)
                {
                    attributes.ValueOf(a).SetBaseValue(100f);
                }

                for (int j = 0; j < effects.Length; j++)
                {
                    Assert.That(Asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effects[j], Asc)).Succeeded, Is.True);
                }
                for (int p = 0; p < periodicDrivers.Length; p++)
                {
                    Assert.That(Asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(periodicDrivers[p], Asc)).Succeeded, Is.True);
                }
            }

            public void Dispose()
            {
                Asc.Dispose();
            }
        }

        private sealed class PermutationAttributeSet : AttributeSet
        {
            private readonly GameplayAttribute[] _attributes = new GameplayAttribute[128];

            internal PermutationAttributeSet()
            {
                for (int a = 0; a < _attributes.Length; a++)
                {
                    _attributes[a] = new GameplayAttribute($"Attr{a}");
                }
            }

            internal GameplayAttribute ValueOf(int index) => _attributes[index];

            protected override void RegisterAttributes()
            {
                for (int a = 0; a < _attributes.Length; a++)
                {
                    RegisterAttribute(_attributes[a]);
                }
            }
        }
    }
}
