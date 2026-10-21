using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Contract suite for on-demand attribute settlement. A capture must observe a settled current
    /// value even when the owning ASC has not run its aggregation pass yet (same-frame base writes),
    /// and settlement must terminate on dependency cycles.
    /// </summary>
    public sealed class GASAttributeSettlementTests
    {
        [Test]
        public void SameFrame_BaseWriteThenInstantCapture_ReadsSettledValue()
        {
            var asc = CreateAsc(out SettlementAttributeSet attributes);
            try
            {
                // Deliberately no tick between the base write and the application.
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);

                var effect = new GameplayEffect(
                    "Settlement.SameFrame.Instant",
                    EDurationPolicy.Instant,
                    modifiers: new List<ModifierInfo> { AddFromPower("Health") });
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);

                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue),
                    "A same-frame capture must settle the source attribute instead of reading its stale current value.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void SameFrame_BaseWriteThenDurationCapture_ReadsSettledValue()
        {
            var asc = CreateAsc(out SettlementAttributeSet attributes);
            try
            {
                attributes.Health.SetBaseValue(100);
                attributes.Power.SetBaseValue(5);

                var effect = new GameplayEffect(
                    "Settlement.SameFrame.Duration",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo> { AddFromPower("Health") });
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc)).Succeeded, Is.True);

                asc.Tick(1f / 60f, isServer: true);
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue),
                    "Aggregation in the same frame must also read the settled dependency value.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void MutualDependency_SettlesDeterministicallyWithoutRecursingForever()
        {
            long firstRun = RunMutualDependency();
            long secondRun = RunMutualDependency();
            Assert.That(secondRun, Is.EqualTo(firstRun),
                "Mutual attribute dependencies must settle to the same values on every run.");
        }

        private static long RunMutualDependency()
        {
            var asc = CreateAsc(out SettlementAttributeSet attributes);
            try
            {
                attributes.Left.SetBaseValue(10);
                attributes.Right.SetBaseValue(10);

                var leftEffect = new GameplayEffect(
                    "Settlement.Cycle.Left",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Left", EAttributeModifierOperation.Add, new AttributeBasedMagnitude(
                            "Right", EGameplayEffectAttributeCaptureSource.Target,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(1f), new ScalableFloat(0f), new ScalableFloat(0f),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                    });
                var rightEffect = new GameplayEffect(
                    "Settlement.Cycle.Right",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Right", EAttributeModifierOperation.Add, new AttributeBasedMagnitude(
                            "Left", EGameplayEffectAttributeCaptureSource.Target,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(1f), new ScalableFloat(0f), new ScalableFloat(0f),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                    });

                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(leftEffect, asc)).Succeeded, Is.True);
                Assert.That(asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(rightEffect, asc)).Succeeded, Is.True);

                // Multiple ticks keep the result stable; the settle guard bounds recursion.
                for (int i = 0; i < 3; i++)
                {
                    asc.Tick(1f / 60f, isServer: true);
                }

                return attributes.Left.CurrentValueRaw * 31 + attributes.Right.CurrentValueRaw;
            }
            finally
            {
                asc.Dispose();
            }
        }

        private static ModifierInfo AddFromPower(string targetAttribute)
        {
            return new ModifierInfo(
                targetAttribute,
                EAttributeModifierOperation.Add,
                new AttributeBasedMagnitude(
                    "Power",
                    EGameplayEffectAttributeCaptureSource.Target,
                    EAttributeBasedFloatCalculationType.AttributeMagnitude,
                    new ScalableFloat(1f),
                    new ScalableFloat(0f),
                    new ScalableFloat(0f),
                    EGameplayEffectAttributeCaptureSnapshot.NotSnapshot));
        }

        private static AbilitySystemComponent CreateAsc(out SettlementAttributeSet attributes)
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            attributes = new SettlementAttributeSet();
            asc.AddAttributeSet(attributes);
            return asc;
        }

        private sealed class SettlementAttributeSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");
            internal readonly GameplayAttribute Power = new GameplayAttribute("Power");
            internal readonly GameplayAttribute Left = new GameplayAttribute("Left");
            internal readonly GameplayAttribute Right = new GameplayAttribute("Right");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Power);
                RegisterAttribute(Left);
                RegisterAttribute(Right);
            }
        }
    }
}
