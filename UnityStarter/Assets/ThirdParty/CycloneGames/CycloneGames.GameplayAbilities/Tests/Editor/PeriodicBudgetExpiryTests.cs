using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class PeriodicBudgetExpiryTests
    {
        [Test]
        public void BudgetExhaustedOnExpiryTick_DropsUndeliveredIntervals()
        {
            var limits = new GASRuntimeLimits(maxPeriodicEffectExecutionsPerTick: 3);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            var effect = new GameplayEffect(
                "Budget.Expiry",
                EDurationPolicy.HasDuration,
                1f,
                period: 0.125f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-1f))
                },
                executePeriodicEffectOnApplication: false);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));

            // One 1s tick is due for 10 executions; the budget allows 3 and the duration expires in the
            // same tick, so the remaining intervals are dropped with the effect rather than carried.
            asc.Tick(1f, true);

            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(97).RawValue),
                "Only the budgeted executions run before the effect expires.");
            Assert.That(asc.ActiveEffects, Is.Empty,
                "The effect expires on the same tick that the budget is exhausted.");

            asc.Dispose();
        }

        [Test]
        public void BudgetExhaustedBeforeExpiry_CarriesBacklogToLaterTicks()
        {
            var limits = new GASRuntimeLimits(maxPeriodicEffectExecutionsPerTick: 3);
            var asc = new AbilitySystemComponent(
                null,
                new GASAbilitySystemRuntimeOptions(GASCoreStateMode.RuntimeOnly, limits));
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            var effect = new GameplayEffect(
                "Budget.Backlog",
                EDurationPolicy.Infinite,
                period: 0.125f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-1f))
                },
                executePeriodicEffectOnApplication: false);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));

            asc.Tick(0.5f, true);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(97).RawValue));

            asc.Tick(0f, true);
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(96).RawValue),
                "The undelivered intervals stay in the timer and drain on later ticks.");

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
