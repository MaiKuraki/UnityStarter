using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASReentrancyContainmentTests
    {
        [Test]
        public void PeriodicExecution_NestedRemovalIsRejectedAndEffectSurvives()
        {
            var attributes = new ProbeAttributeSet();
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            var effect = new GameplayEffect(
                "Reentrancy.Probe",
                EDurationPolicy.Infinite,
                period: 1f,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-1f))
                },
                executePeriodicEffectOnApplication: false);

            asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, asc));
            attributes.Asc = asc;

            asc.Tick(1f, true);

            Assert.That(attributes.RemovalAttempts, Is.EqualTo(1),
                "The periodic execution must have reached the attribute hook.");
            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1),
                "A rejected nested mutation must not remove the effect that is being ticked.");
            Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue),
                "The rejected mutation aborts the current execution, which rolls back atomically.");

            asc.Dispose();
        }

        private sealed class ProbeAttributeSet : AttributeSet
        {
            public GameplayAttribute Health { get; } = new GameplayAttribute("Health");
            public AbilitySystemComponent Asc;
            public int RemovalAttempts;

            protected override void RegisterAttributes()
            {
                RegisterAttribute(Health);
            }

            public override void PostGameplayEffectExecute(GameplayEffectModCallbackData data)
            {
                RemovalAttempts++;
                Asc?.RemoveActiveEffectsWithGrantedTags(new GameplayTagContainer());
            }
        }
    }
}
