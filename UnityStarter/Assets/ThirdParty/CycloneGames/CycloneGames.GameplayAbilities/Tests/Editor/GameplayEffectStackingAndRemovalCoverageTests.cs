using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayEffectStackingAndRemovalCoverageTests
    {
        [Test]
        public void AggregateBySource_KeepsOneEffectPerSourceAndStacksWithinSource()
        {
            var context = new GASRuntimeContext();
            AbilitySystemComponent target = CreateAsc(context);
            AbilitySystemComponent sourceA = CreateAsc(context);
            AbilitySystemComponent sourceB = CreateAsc(context);

            var effect = new GameplayEffect(
                "Coverage.BySource",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(-5f))
                },
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateBySource,
                    3,
                    EGameplayEffectStackingDurationPolicy.NeverRefresh));

            try
            {
                target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, sourceA));
                target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, sourceA));
                target.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(effect, sourceB));

                Assert.That(target.ActiveEffects.Count, Is.EqualTo(2),
                    "Each source owns one aggregated effect.");
                Assert.That(target.ActiveEffects[0].StackCount + target.ActiveEffects[1].StackCount,
                    Is.EqualTo(3));
            }
            finally
            {
                target.Dispose();
                sourceA.Dispose();
                sourceB.Dispose();
                context.Dispose();
            }
        }

        [Test]
        public void RemoveGameplayEffectsWithTags_ClearsOnlyMatchingEffects()
        {
            GameplayTagManager.RegisterDynamicTag("Test.Coverage.Cleanse.Marker", "Removal coverage probe.");
            GameplayTagManager.InitializeIfNeeded();
            GameplayTag marker = GameplayTagManager.Request("Test.Coverage.Cleanse.Marker");

            var context = new GASRuntimeContext();
            AbilitySystemComponent asc = CreateAsc(context);
            try
            {
                var tagged = new GameplayEffect(
                    "Coverage.Tagged",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(1f))
                    },
                    assetTags: Tags(marker));

                var plain = new GameplayEffect(
                    "Coverage.Plain",
                    EDurationPolicy.Infinite,
                    modifiers: new List<ModifierInfo>
                    {
                        new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(2f))
                    });

                var cleanser = new GameplayEffect(
                    "Coverage.Cleanser",
                    EDurationPolicy.Instant,
                    removeGameplayEffectsWithTags: Tags(marker));

                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(tagged, asc));
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(plain, asc));
                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(2));

                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(cleanser, asc));

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(asc.ActiveEffects[0].Spec.Def.Name, Is.EqualTo("Coverage.Plain"));
            }
            finally
            {
                asc.Dispose();
                context.Dispose();
            }
        }

        private static AbilitySystemComponent CreateAsc(GASRuntimeContext context)
        {
            var asc = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            return asc;
        }

        private static GameplayTagContainer Tags(GameplayTag tag)
        {
            var container = new GameplayTagContainer();
            container.AddTag(tag);
            return container;
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
