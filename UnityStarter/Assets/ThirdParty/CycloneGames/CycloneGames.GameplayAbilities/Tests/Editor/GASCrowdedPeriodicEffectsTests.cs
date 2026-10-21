using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Regression for the crowded-effect scenario reported from the sample: several periodic
    /// effects with different capture sources/policies coexist on one victim while base values
    /// change mid-duration. This exercises the dependents scan while specs are being executed and
    /// must not corrupt speculative magnitude storage.
    /// </summary>
    public sealed class GASCrowdedPeriodicEffectsTests
    {
        [Test]
        public void FiveCoexistingPeriodicEffects_WithMidDurationBuff_StayStable()
        {
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var caster = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var victim = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var casterSet = new CrowdedSet();
            var victimSet = new CrowdedSet();
            caster.AddAttributeSet(casterSet);
            victim.AddAttributeSet(victimSet);
            try
            {
                casterSet.Power.SetBaseValue(40);
                victimSet.Power.SetBaseValue(40);
                victimSet.Health.SetBaseValue(1000);
                caster.Tick(0f, isServer: true);
                victim.Tick(0f, isServer: true);

                // Same shapes as Samples/Scripts/CombatCaptureTimingSample: live ignite, snapshot
                // ignite, %-current-health poison, stacked bleed, live heal-over-time.
                Apply(victim, caster, PowerEffect("Crowded.Ignite.Live", -1f, snapshot: false,
                    EGameplayEffectAttributeCaptureSource.Source));
                Apply(victim, caster, PowerEffect("Crowded.Ignite.Snapshot", -1f, snapshot: true,
                    EGameplayEffectAttributeCaptureSource.Source));
                Apply(victim, caster, PowerEffect("Crowded.HoT.Live", 1f, snapshot: false,
                    EGameplayEffectAttributeCaptureSource.Source));
                Apply(victim, caster, PercentHealthEffect("Crowded.Poison", 0.5f));
                Apply(victim, caster, FlatEffect("Crowded.Bleed", 15f));
                Apply(victim, caster, FlatEffect("Crowded.Bleed", 15f));

                for (int tick = 0; tick < 4; tick++)
                {
                    casterSet.Power.SetBaseValue(40 + tick * 10);
                    caster.Tick(0f, isServer: true);
                    victim.Tick(1f, isServer: true);
                }

                // The only assertion that matters here: the world stays coherent and finite.
                Assert.That(victim.ActiveEffects.Count, Is.GreaterThan(0));
                Assert.That(float.IsNaN(victimSet.Health.CurrentValue), Is.False);
            }
            finally
            {
                victim.Dispose();
                caster.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void SpecCapacity_ArrayLengthsDiverge_NormalizedBeforeDependencyScan()
        {
            // Regression for a pooled-state divergence: a reused spec can hold a long magnitude
            // array next to a short dependency array. EnsureCapacity must normalize every array
            // independently, otherwise ResolveDependencySourceAttributes indexes out of bounds.
            var context = new GASRuntimeContext(GASRuntimeAuthorityMode.Authority);
            var asc = new AbilitySystemComponent(context);
            var set = new CrowdedSet();
            asc.AddAttributeSet(set);
            try
            {
                set.Health.SetBaseValue(100);
                set.Power.SetBaseValue(5);
                asc.Tick(0f, isServer: true);

                var effect = new GameplayEffect(
                    "Crowded.Capacity",
                    EDurationPolicy.Infinite,
                    period: 1f,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new AttributeBasedMagnitude(
                            "Power", EGameplayEffectAttributeCaptureSource.Target,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(1f), new ScalableFloat(0f), new ScalableFloat(0f),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot)),
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f)),
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    },
                    executePeriodicEffectOnApplication: false);

                GameplayEffectSpec spec = GameplayEffectSpec.Create(effect, asc);

                // Simulate a spec whose dependency array is shorter than its magnitude arrays.
                var dependencyField = typeof(GameplayEffectSpec).GetField(
                    "dependencySourceAttributes",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                var resolvedField = typeof(GameplayEffectSpec).GetField(
                    "dependencySourceAttributesResolved",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                Assert.That(dependencyField, Is.Not.Null);
                Assert.That(resolvedField, Is.Not.Null);
                dependencyField.SetValue(spec, System.Array.Empty<GameplayAttribute>());
                resolvedField.SetValue(spec, false);

                Assert.That(asc.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);
                asc.Tick(1f, isServer: true);

                Assert.That(set.Health.CurrentValueRaw, Is.GreaterThan(GASFixedValue.FromInt(100).RawValue),
                    "The dependent scan must survive the normalized capacity and keep applying modifiers.");
            }
            finally
            {
                asc.Dispose();
                context.Dispose();
            }
        }

        private static void Apply(AbilitySystemComponent victim, AbilitySystemComponent caster, GameplayEffect effect)
        {
            Assert.That(victim.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, caster)).Succeeded, Is.True,
                $"Effect '{effect.Name}' failed to apply.");
        }

        private static GameplayEffect PowerEffect(
            string name,
            float signedCoefficient,
            bool snapshot,
            EGameplayEffectAttributeCaptureSource captureSource)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        "Health",
                        EAttributeModifierOperation.Add,
                        new AttributeBasedMagnitude(
                            "Power",
                            captureSource,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(signedCoefficient),
                            new ScalableFloat(0f),
                            new ScalableFloat(0f),
                            snapshot
                                ? EGameplayEffectAttributeCaptureSnapshot.Snapshot
                                : EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                },
                executePeriodicEffectOnApplication: false);
        }

        private static GameplayEffect PercentHealthEffect(string name, float coefficient)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        "Health",
                        EAttributeModifierOperation.Add,
                        new AttributeBasedMagnitude(
                            "Health",
                            EGameplayEffectAttributeCaptureSource.Target,
                            EAttributeBasedFloatCalculationType.AttributeMagnitude,
                            new ScalableFloat(-coefficient),
                            new ScalableFloat(0f),
                            new ScalableFloat(0f),
                            EGameplayEffectAttributeCaptureSnapshot.NotSnapshot))
                },
                executePeriodicEffectOnApplication: false);
        }

        private static GameplayEffect FlatEffect(string name, float damagePerStack)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-damagePerStack))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    5,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication),
                executePeriodicEffectOnApplication: false);
        }

        private sealed class CrowdedSet : AttributeSet
        {
            internal readonly GameplayAttribute Health = new GameplayAttribute("Health");
            internal readonly GameplayAttribute Power = new GameplayAttribute("Power");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
                RegisterAttribute(Power);
            }
        }
    }
}
