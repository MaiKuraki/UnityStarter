using System;
using System.Collections.Generic;
using System.Diagnostics;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Load harness for the dirty-attribute aggregation path (RecalculateDirtyAttributes).
    ///
    /// Drives a population of AbilitySystemComponents where a periodic effect re-dirties every
    /// attribute of every character each tick, forcing full-load aggregation every tick.
    /// Each of the 8 duration effects per character carries 8 modifiers spanning 8 attributes,
    /// so aggregation work per dirty attribute spans all 8 effects.
    ///
    /// Coverage: Add / Multiply / Override branches, a non-snapshot AttributeBasedMagnitude
    /// (live recalculation branch), periodic executions, and a correctness invariant comparing
    /// final fixed-point values against independently derived expectations.
    ///
    /// Measurements come from the editor-only GASAggregationProbe plus an outer Stopwatch around
    /// ASC.Tick. Editor/Mono numbers are indicative only; Player-build confirmation is separate.
    /// </summary>
    public sealed class DirtyAttributeAggregationLoadTests
    {
        private const int CharacterCount = 500;
        private const int AttributeCount = 8;
        private const int DurationEffectsPerCharacter = 8;
        private const int WarmupTicks = 60;
        private const int MeasuredTicks = 300;
        private const float DeltaTime = 1f / 60f;

        [Test]
        public void AggregationLoad_WasteAndTimeShare()
        {
            var characters = new List<Character>(CharacterCount);
            for (int i = 0; i < CharacterCount; i++)
            {
                characters.Add(new Character(i));
            }

            // Warmup: stabilizes pools/caches and settles the simulation before measuring.
            for (int t = 0; t < WarmupTicks; t++)
            {
                TickAll(characters);
            }

            GASAggregationProbe.Reset();
            var tickTimestamps = new long[MeasuredTicks];
            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            for (int t = 0; t < MeasuredTicks; t++)
            {
                long start = Stopwatch.GetTimestamp();
                TickAll(characters);
                tickTimestamps[t] = Stopwatch.GetTimestamp() - start;
            }
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Array.Sort(tickTimestamps);
            long medianTickTicks = tickTimestamps[MeasuredTicks / 2];
            long totalTickTicks = 0;
            for (int t = 0; t < MeasuredTicks; t++)
            {
                totalTickTicks += tickTimestamps[t];
            }

            double tickFrequency = Stopwatch.Frequency;
            double medianTickMs = medianTickTicks * 1000.0 / tickFrequency;
            double totalTickMs = totalTickTicks * 1000.0 / tickFrequency;
            double aggregationMs = GASAggregationProbe.AggregationTimestampTicks * 1000.0 / tickFrequency;
            double aggregationShare = totalTickTicks > 0
                ? (double)GASAggregationProbe.AggregationTimestampTicks / totalTickTicks
                : 0;
            long wastedVisits = GASAggregationProbe.ModifierVisits - GASAggregationProbe.ModifierHits;
            double wasteRatio = GASAggregationProbe.ModifierVisits > 0
                ? (double)wastedVisits / GASAggregationProbe.ModifierVisits
                : 0;

            UnityEngine.Debug.Log(
                "[AggregationLoad] " +
                $"characters={CharacterCount} attrsPerChar={AttributeCount} durationEffects={DurationEffectsPerCharacter} " +
                $"ticks={MeasuredTicks} (warmup {WarmupTicks}) | " +
                $"medianTick={medianTickMs:F3}ms totalTick={totalTickMs:F1}ms | " +
                $"aggregationTotal={aggregationMs:F1}ms share={aggregationShare:P1} | " +
                $"recalcPasses={GASAggregationProbe.RecalculatePasses} dirtyAttrs={GASAggregationProbe.DirtyAttributesProcessed} " +
                $"effectsSkipped={GASAggregationProbe.EffectsSkippedPeriodicOrInhibited} | " +
                $"modifierVisits={GASAggregationProbe.ModifierVisits} hits={GASAggregationProbe.ModifierHits} " +
                $"wasted={wastedVisits} wasteRatio={wasteRatio:P1} | " +
                $"allocatedBytes={allocatedBytes}");

            // Correctness invariant: independently derived fixed-point expectations.
            int executions = characters[0].PeriodicExecutions;
            for (int c = 1; c < characters.Count; c++)
            {
                Assert.That(characters[c].PeriodicExecutions, Is.EqualTo(executions),
                    $"Character {c} diverged in periodic execution count; the deterministic driver broke.");
            }

            long expectedBase = 100 + executions;
            // Aggregation: (base + additive) * multiplier with additive = 4x(+1) + 0 + (+Power=2) = 6,
            // multiplier = 1 * 2 * 2 = 4. All operands are integers, so fixed-point math is exact.
            long expectedCurrent = GASFixedValue.FromInt((int)((expectedBase + 6) * 4)).RawValue;
            for (int c = 0; c < characters.Count; c++)
            {
                Character character = characters[c];
                Assert.That(character.Attributes.Attr0.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(500).RawValue),
                    $"Character {c} Attr0 must equal the Override value.");
                for (int a = 1; a < AttributeCount; a++)
                {
                    Assert.That(character.Attributes.ValueOf(a).CurrentValueRaw, Is.EqualTo(expectedCurrent),
                        $"Character {c} Attr{a} mismatch. expected={expectedCurrent} actual={character.Attributes.ValueOf(a).CurrentValueRaw}");
                }
            }

            foreach (Character character in characters)
            {
                character.Dispose();
            }
        }

        private static void TickAll(List<Character> characters)
        {
            for (int i = 0; i < characters.Count; i++)
            {
                characters[i].Asc.Tick(DeltaTime, isServer: true);
            }
        }

        private sealed class Character : IDisposable
        {
            internal readonly AbilitySystemComponent Asc;
            internal readonly LoadAttributeSet Attributes;
            internal int PeriodicExecutions;

            internal Character(int index)
            {
                Asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
                Attributes = new LoadAttributeSet();
                Asc.AddAttributeSet(Attributes);
                for (int a = 0; a < AttributeCount; a++)
                {
                    Attributes.SetBaseValue(Attributes.ValueOf(a), GASFixedValue.FromInt(100));
                }
                Attributes.SetBaseValue(Attributes.Power, GASFixedValue.FromInt(2));
                Attributes.Attr0.OnBaseValueChanged += OnBaseChanged;

                // 8 infinite-duration effects x 8 modifiers each; each effect's modifiers span
                // all 8 attributes, so every attribute aggregates contributions from all 8 effects.
                ApplyInfinite(BuildAddEffect($"Aggro.Add.{index}.a", 1));
                ApplyInfinite(BuildAddEffect($"Aggro.Add.{index}.b", 1));
                ApplyInfinite(BuildAddEffect($"Aggro.Add.{index}.c", 1));
                ApplyInfinite(BuildAddEffect($"Aggro.Add.{index}.d", 1));
                ApplyInfinite(BuildMultiplyEffect($"Aggro.Mul.{index}.a", 2));
                ApplyInfinite(BuildMultiplyEffect($"Aggro.Mul.{index}.b", 2));
                ApplyInfinite(BuildOverrideEffect($"Aggro.Override.{index}"));
                ApplyInfinite(BuildLiveAttributeEffect($"Aggro.Live.{index}"));

                // Periodic driver: +1 base on every attribute once per tick, keeping every
                // attribute dirty so RecalculateDirtyAttributes runs at full load every tick.
                var periodic = new GameplayEffect(
                    $"Aggro.Periodic.{index}",
                    EDurationPolicy.Infinite,
                    period: DeltaTime,
                    modifiers: BuildModifiers((name) => new ModifierInfo(name, EAttributeModifierOperation.Add, new ScalableFloat(1f))));
                GameplayEffectApplicationResult result = Asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(periodic, Asc));
                Assert.That(result.Succeeded, Is.True, "Periodic driver effect failed to apply.");
            }

            private void OnBaseChanged(float oldValue, float newValue)
            {
                PeriodicExecutions++;
            }

            private void ApplyInfinite(GameplayEffect effect)
            {
                GameplayEffectApplicationResult result = Asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, Asc));
                Assert.That(result.Succeeded, Is.True, $"Effect '{effect.Name}' failed to apply.");
            }

            private static GameplayEffect BuildAddEffect(string name, int magnitude)
            {
                return new GameplayEffect(name, EDurationPolicy.Infinite,
                    modifiers: BuildModifiers((attrName) => new ModifierInfo(attrName, EAttributeModifierOperation.Add, new ScalableFloat(magnitude))));
            }

            private static GameplayEffect BuildMultiplyEffect(string name, int factor)
            {
                return new GameplayEffect(name, EDurationPolicy.Infinite,
                    modifiers: BuildModifiers((attrName) => new ModifierInfo(attrName, EAttributeModifierOperation.Multiply, new ScalableFloat(factor))));
            }

            private static GameplayEffect BuildOverrideEffect(string name)
            {
                // Override only Attr0; keep 8 modifiers so per-effect modifier count stays uniform.
                var modifiers = new List<ModifierInfo>(AttributeCount);
                modifiers.Add(new ModifierInfo("Attr0", EAttributeModifierOperation.Override, new ScalableFloat(500)));
                for (int a = 1; a < AttributeCount; a++)
                {
                    modifiers.Add(new ModifierInfo($"Attr{a}", EAttributeModifierOperation.Add, new ScalableFloat(0)));
                }
                return new GameplayEffect(name, EDurationPolicy.Infinite, modifiers: modifiers);
            }

            private static GameplayEffect BuildLiveAttributeEffect(string name)
            {
                return new GameplayEffect(name, EDurationPolicy.Infinite,
                    modifiers: BuildModifiers((attrName) => new ModifierInfo(
                        attrName,
                        EAttributeModifierOperation.Add,
                        new AttributeBasedMagnitude(
                            "Power",
                            EGameplayEffectAttributeCaptureSource.Target,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(1f),
                            new ScalableFloat(0f),
                            new ScalableFloat(0f),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))));
            }

            private static List<ModifierInfo> BuildModifiers(Func<string, ModifierInfo> factory)
            {
                var modifiers = new List<ModifierInfo>(AttributeCount);
                for (int a = 0; a < AttributeCount; a++)
                {
                    modifiers.Add(factory($"Attr{a}"));
                }
                return modifiers;
            }

            public void Dispose()
            {
                Attributes.Attr0.OnBaseValueChanged -= OnBaseChanged;
                Asc.Dispose();
            }
        }

        private sealed class LoadAttributeSet : AttributeSet
        {
            internal readonly GameplayAttribute Attr0 = new GameplayAttribute("Attr0");
            private readonly GameplayAttribute[] _attributes;
            internal readonly GameplayAttribute Power = new GameplayAttribute("Power");

            internal LoadAttributeSet()
            {
                _attributes = new GameplayAttribute[AttributeCount];
                _attributes[0] = Attr0;
                for (int a = 1; a < AttributeCount; a++)
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
                RegisterAttribute(Power);
            }
        }
    }
}
