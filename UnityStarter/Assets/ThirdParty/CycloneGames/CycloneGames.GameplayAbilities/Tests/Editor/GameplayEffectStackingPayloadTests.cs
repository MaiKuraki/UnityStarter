using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayEffectStackingPayloadTests
    {
        private const string AmountTagName = "Test.Stacking.Amount";

        private GameplayTag amountTag;

        [SetUp]
        public void SetUp()
        {
            GameplayTagManager.RegisterDynamicTag(AmountTagName, "Stacking payload probe.");
            GameplayTagManager.InitializeIfNeeded();
            amountTag = GameplayTagManager.Request(AmountTagName);
        }

        [Test]
        public void StackedSetByCaller_UsesLatestApplicationValues()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            var effect = new GameplayEffect(
                "Stacking.SetByCaller",
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo(
                        "Health",
                        EAttributeModifierOperation.Add,
                        new SetByCallerMagnitude(amountTag, 0f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    5,
                    EGameplayEffectStackingDurationPolicy.NeverRefresh),
                executePeriodicEffectOnApplication: false);

            GameplayEffectSpec first = GameplayEffectSpec.Create(effect, asc);
            first.SetSetByCallerMagnitude(amountTag, -10f);
            asc.ApplyGameplayEffectSpecToSelf(first);
            asc.Tick(1f, true);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(90).RawValue));

            GameplayEffectSpec second = GameplayEffectSpec.Create(effect, asc);
            second.SetSetByCallerMagnitude(amountTag, -50f);
            asc.ApplyGameplayEffectSpecToSelf(second);

            Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(2));

            asc.Tick(1f, true);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(-10).RawValue),
                "Two stacks of the newest -50 value apply 100 damage on the next tick.");

            asc.Dispose();
        }

        [Test]
        public void StackedStaticModifier_IsUnaffectedByPayloadAdoption()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            var effect = new GameplayEffect(
                "Stacking.Static",
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-5f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    5,
                    EGameplayEffectStackingDurationPolicy.NeverRefresh),
                executePeriodicEffectOnApplication: false);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(1f, true);
            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            asc.Tick(1f, true);

            Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(2));
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(85).RawValue),
                "Periodic magnitude already scales by stack count, independent of payload adoption.");

            asc.Dispose();
        }

        private sealed class HealthAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }
        }
    }
}
