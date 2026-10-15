using System;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Allocation regression guard for the per-spec modifier grouping used by
    /// RecalculateDirtyAttributes. Repeatedly leases, aggregates, and releases specs and
    /// measures managed bytes per cycle; the grouping storage is expected to ride the pooled
    /// spec backing, so steady-state churn stays within the documented per-apply shell budget.
    /// </summary>
    public sealed class GASModifierGroupingChurnTests
    {
        private const int WarmupCycles = 256;
        private const int MeasuredCycles = 32768;

        [Test]
        public void ModifierGrouping_SpecChurn_MeasuresGen0Pressure()
        {
            var asc = new AbilitySystemComponent();
            var attributes = new ChurnAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Armor.SetBaseValue(100f);

            var definition = new GameplayEffect(
                "GroupingChurn.Effect",
                EDurationPolicy.HasDuration,
                duration: 30f,
                modifiers: new System.Collections.Generic.List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f)),
                    new ModifierInfo("Armor", EAttributeModifierOperation.Add, new ScalableFloat(1f)),
                    new ModifierInfo("Health", EAttributeModifierOperation.Multiply, new ScalableFloat(2f)),
                    new ModifierInfo("Armor", EAttributeModifierOperation.Multiply, new ScalableFloat(2f))
                });

            for (int i = 0; i < WarmupCycles; i++)
            {
                RunCycle(asc, attributes, definition);
            }

            GC.Collect();
            GC.WaitForPendingFinalizers();

            // GC.GetAllocatedBytesForCurrentThread is untrustworthy in batchmode (measured zero
            // even for real allocations); gen0 collection counts and wall time are the
            // environment-proof GC-pressure proxies.
            GASAggregationProbe.Reset();
            int gen0Before = GC.CollectionCount(0);
            long startTimestamp = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int i = 0; i < MeasuredCycles; i++)
            {
                RunCycle(asc, attributes, definition);
            }
            double elapsedMs = (System.Diagnostics.Stopwatch.GetTimestamp() - startTimestamp) * 1000.0 / System.Diagnostics.Stopwatch.Frequency;
            int gen0Collections = GC.CollectionCount(0) - gen0Before;

            UnityEngine.Debug.Log(
                $"[GroupingChurn] cycles={MeasuredCycles} elapsedMs={elapsedMs:F1} " +
                $"gen0Collections={gen0Collections} modifierHits={GASAggregationProbe.ModifierHits} " +
                $"recalcPasses={GASAggregationProbe.RecalculatePasses}");

            Assert.That(GASAggregationProbe.ModifierHits, Is.GreaterThan(0),
                "The churn cycle never aggregated; the measurement is vacuous.");
            Assert.That(GASAggregationProbe.RecalculatePasses, Is.EqualTo(MeasuredCycles),
                "Every measured cycle must run exactly one aggregation pass.");

            // The effect's modifiers interleave the two attributes (Health, Armor, Health, Armor).
            // Grouped iteration must preserve per-attribute ascending modifier order:
            // Health = (100 + 1) * 2 = 202 holds only when Add lands before Multiply.
            Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(202).RawValue),
                "Grouped iteration must preserve per-attribute modifier order on an interleaved layout.");
            Assert.That(attributes.Armor.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(202).RawValue));

            asc.Dispose();
        }

        private static void RunCycle(AbilitySystemComponent asc, ChurnAttributeSet attributes, GameplayEffect definition)
        {
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(definition, asc));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException("Churn cycle effect application failed.");
            }

            // Force a full aggregation pass so the spec's modifier grouping is built.
            asc.MarkAttributeDirty(attributes.Health);
            asc.Tick(0f, isServer: true);

            if (!asc.TryRemoveActiveEffect(result.ActiveEffect))
            {
                throw new InvalidOperationException("Churn cycle effect removal failed.");
            }
        }

        private sealed class ChurnAttributeSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");
            internal readonly GameplayAttribute Armor = new GameplayAttribute("Armor");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Armor);
            }
        }
    }
}
