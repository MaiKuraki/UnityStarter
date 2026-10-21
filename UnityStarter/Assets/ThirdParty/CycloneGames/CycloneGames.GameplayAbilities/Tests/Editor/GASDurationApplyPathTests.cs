using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GASDurationApplyPathTests
    {
        [Test]
        public void DirectApply_DurationModifierReachesAttribute()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            try
            {
                asc.ApplyGameplayEffectSpecToSelf(GameplayEffectSpec.Create(BuildEffect(), asc));
                asc.Tick(0f, true);

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void AbilityApply_DurationModifierReachesAttribute()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            try
            {
                GameplayAbilitySpec spec = asc.GrantAbility(new EffectApplyingAbility(BuildEffect()));
                Assert.That(asc.TryActivateAbility(spec), Is.True);
                asc.Tick(0f, true);

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
                Assert.That(attributes.Health.BaseValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue));
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue));
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void AbilityApply_WithRemovalFlag_IsRemovedWhenAbilityEnds()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            try
            {
                GameplayAbilitySpec spec = asc.GrantAbility(new EffectApplyingAbility(BuildEffect(removeOnAbilityEnd: true)));
                Assert.That(asc.TryActivateAbility(spec), Is.True);
                asc.Tick(0f, true);

                Assert.That(asc.ActiveEffects, Is.Empty,
                    "The effect must be removed when the granting ability ends.");
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(100).RawValue),
                    "The aggregated modifier must be withdrawn with the effect.");
            }
            finally
            {
                asc.Dispose();
            }
        }

        [Test]
        public void DirectApply_WithRemovalFlag_HasNoAbilityToTrack()
        {
            var asc = new AbilitySystemComponent(null, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var attributes = new HealthAttributeSet();
            asc.AddAttributeSet(attributes);
            attributes.Health.SetBaseValue(100f);
            attributes.Health.SetCurrentValue(100f);
            asc.Tick(0f, true);

            try
            {
                asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(BuildEffect(removeOnAbilityEnd: true), asc));
                asc.Tick(0f, true);

                Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1),
                    "Without an ability context the removal flag has nothing to track.");
                Assert.That(attributes.Health.CurrentValueRaw, Is.EqualTo(GASFixedValue.FromInt(105).RawValue));
            }
            finally
            {
                asc.Dispose();
            }
        }

        private static GameplayEffect BuildEffect(bool removeOnAbilityEnd = false)
        {
            return new GameplayEffect(
                "ApplyPath.Probe",
                EDurationPolicy.Infinite,
                modifiers: new List<ModifierInfo>
                {
                    new ModifierInfo("Health", EAttributeModifierOperation.Add, new ScalableFloat(5f))
                },
                removeGameplayEffectsAfterAbilityEnds: removeOnAbilityEnd);
        }

        private sealed class EffectApplyingAbility : GameplayAbility
        {
            private readonly GameplayEffect effect;

            public EffectApplyingAbility(GameplayEffect effect)
            {
                this.effect = effect;
                Initialize(
                    "ApplyPath.ProbeAbility",
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new EffectApplyingAbility(effect);
            }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                GameplayEffectContext context = AbilitySystemComponent.MakeEffectContext();
                context.AddInstigator(AbilitySystemComponent, this);
                AbilitySystemComponent.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(effect, AbilitySystemComponent, context, spec.Level));
                EndAbility();
            }
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
