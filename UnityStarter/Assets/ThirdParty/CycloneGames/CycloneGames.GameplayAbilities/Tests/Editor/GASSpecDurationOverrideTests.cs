using System;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Tests for GameplayEffectSpec.SetDuration / SetDurationRaw and the per-instance
    /// cooldown duration reported by AbilitySystemComponent.GetCooldownInfo.
    /// </summary>
    public sealed class GASSpecDurationOverrideTests
    {
        [Test]
        public void SetDuration_OverridesInstanceDuration_DefinitionUnchanged()
        {
            var asc = new AbilitySystemComponent();
            var definition = new GameplayEffect("OverrideDuration.Basic", EDurationPolicy.HasDuration, duration: 10f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);

            spec.SetDuration(2.5f);

            Assert.That(spec.Duration, Is.EqualTo(2.5f));
            Assert.That(spec.DurationRaw, Is.EqualTo(GASFixedValue.FromFloat(2.5f).RawValue));
            Assert.That(definition.Duration, Is.EqualTo(10f), "The shared definition must never be mutated.");

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ActiveEffect.TimeRemaining, Is.EqualTo(2.5f));
            Assert.That(definition.Duration, Is.EqualTo(10f));

            asc.Dispose();
        }

        [Test]
        public void SetDurationRaw_CooldownReductionScenario_AppliesComputedDuration()
        {
            var asc = new AbilitySystemComponent();
            // Base cooldown 8s by level; 25% cooldown reduction from attributes => 6s final.
            var definition = new GameplayEffect("OverrideDuration.CDR", EDurationPolicy.HasDuration, duration: 8f);
            GASFixedValue finalCooldown = GASFixedValue.FromFloat(8f) * (GASFixedValue.FromInt(1) - GASFixedValue.FromFloat(0.25f));
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);

            spec.SetDuration(finalCooldown);

            Assert.That(spec.DurationRaw, Is.EqualTo(finalCooldown.RawValue));
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ActiveEffect.TimeRemainingRaw, Is.EqualTo(finalCooldown.RawValue));

            asc.Dispose();
        }

        [Test]
        public void SetDuration_AfterSubmission_Throws()
        {
            var asc = new AbilitySystemComponent();
            var definition = new GameplayEffect("OverrideDuration.Submitted", EDurationPolicy.HasDuration, duration: 10f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);
            Assert.That(result.Succeeded, Is.True);

            Assert.Throws<InvalidOperationException>(() => spec.SetDuration(1f));
            Assert.Throws<InvalidOperationException>(() => spec.SetDurationRaw(GASFixedValue.FromInt(1).RawValue));

            asc.Dispose();
        }

        [Test]
        public void SetDuration_NonDurationPolicy_Throws()
        {
            var asc = new AbilitySystemComponent();
            var instant = new GameplayEffect("OverrideDuration.Instant", EDurationPolicy.Instant);
            var infinite = new GameplayEffect("OverrideDuration.Infinite", EDurationPolicy.Infinite);

            GameplayEffectSpec instantSpec = GameplayEffectSpec.Create(instant, asc);
            GameplayEffectSpec infiniteSpec = GameplayEffectSpec.Create(infinite, asc);
            try
            {
                Assert.Throws<InvalidOperationException>(() => instantSpec.SetDuration(1f));
                Assert.Throws<InvalidOperationException>(() => instantSpec.SetDurationRaw(GASFixedValue.FromInt(1).RawValue));
                Assert.Throws<InvalidOperationException>(() => infiniteSpec.SetDuration(1f));
                Assert.Throws<InvalidOperationException>(() => infiniteSpec.SetDurationRaw(GASFixedValue.FromInt(1).RawValue));
            }
            finally
            {
                instantSpec.Discard();
                infiniteSpec.Discard();
                asc.Dispose();
            }
        }

        [Test]
        public void SetDuration_InvalidValues_Throw()
        {
            var asc = new AbilitySystemComponent();
            var definition = new GameplayEffect("OverrideDuration.Invalid", EDurationPolicy.HasDuration, duration: 10f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
            try
            {
                Assert.Throws<ArgumentOutOfRangeException>(() => spec.SetDuration(0f));
                Assert.Throws<ArgumentOutOfRangeException>(() => spec.SetDuration(-1f));
                Assert.Throws<ArgumentOutOfRangeException>(() => spec.SetDuration(float.NaN));
                Assert.Throws<ArgumentOutOfRangeException>(() => spec.SetDuration(float.PositiveInfinity));
                Assert.Throws<ArgumentOutOfRangeException>(() => spec.SetDurationRaw(-1L));

                // Zero raw duration is legal: the effect expires on its first tick.
                spec.SetDurationRaw(0L);
                Assert.That(spec.DurationRaw, Is.EqualTo(0L));
            }
            finally
            {
                spec.Discard();
                asc.Dispose();
            }
        }

        [Test]
        public void SetDuration_StackingRefresh_UsesOverriddenDuration()
        {
            var asc = new AbilitySystemComponent();
            var stacking = new GameplayEffectStacking(
                EGameplayEffectStackingType.AggregateByTarget,
                limit: 4,
                durationPolicy: EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication);
            var definition = new GameplayEffect("OverrideDuration.Stacking", EDurationPolicy.HasDuration, duration: 10f, stacking: stacking);

            GameplayEffectSpec firstSpec = GameplayEffectSpec.Create(definition, asc);
            firstSpec.SetDuration(8f);
            GameplayEffectApplicationResult first = asc.ApplyGameplayEffectSpecToSelf(firstSpec);
            Assert.That(first.Succeeded, Is.True);
            Assert.That(first.ActiveEffect.TimeRemaining, Is.EqualTo(8f));

            GameplayEffectSpec secondSpec = GameplayEffectSpec.Create(definition, asc);
            secondSpec.SetDuration(3f);
            GameplayEffectApplicationResult second = asc.ApplyGameplayEffectSpecToSelf(secondSpec);
            Assert.That(second.Succeeded, Is.True);

            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1), "Stacking must aggregate into a single active effect.");
            Assert.That(asc.ActiveEffects[0].TimeRemaining, Is.EqualTo(3f),
                "Duration refresh on stacking must use the new spec's overridden duration.");
            Assert.That(asc.ActiveEffects[0].Spec.Duration, Is.EqualTo(8f),
                "The active effect deliberately retains the first application's spec lease; " +
                "only the duration is adopted from the incoming spec.");

            asc.Dispose();
        }

        [Test]
        public void AttackSpeedCooldown_CommitAbility_UsesOverriddenDurationAndFeedsCooldownQuery()
        {
            GameplayTag cooldownTag = RegisterTag("Test.GAS.DurationOverride.AttackSpeedCooldown");
            var grantedTags = new GameplayTagContainer();
            grantedTags.AddTag(cooldownTag);
            // Shared definition: a nominal 10s cooldown. The ability computes 2s from attack speed.
            var cooldownDefinition = new GameplayEffect(
                "OverrideDuration.AttackSpeedCooldown.GE",
                EDurationPolicy.HasDuration,
                duration: 10f,
                grantedTags: grantedTags);

            var asc = new AbilitySystemComponent();
            var ability = new ComputedCooldownAbility("AttackSpeedAbility", cooldownDefinition, computedCooldownSeconds: 2f);
            GameplayAbilitySpec abilitySpec = asc.GrantAbility(ability);

            Assert.That(asc.TryActivateAbility(abilitySpec), Is.True);

            Assert.That(asc.IsAbilityOnCooldown(ability), Is.True, "The cooldown tag must be granted after commit.");
            Assert.That(asc.GetCooldownInfo(ability, out float timeRemaining, out float totalDuration), Is.True);
            Assert.That(timeRemaining, Is.EqualTo(2f));
            Assert.That(totalDuration, Is.EqualTo(2f),
                "GetCooldownInfo must report the per-spec overridden duration, not the definition's 10s.");
            Assert.That(cooldownDefinition.Duration, Is.EqualTo(10f), "The shared definition must never be mutated.");

            // A second activation while on cooldown must be rejected through the normal commit path.
            Assert.That(asc.TryActivateAbility(abilitySpec), Is.False);

            asc.Dispose();
        }

        [Test]
        public void GetCooldownInfo_ReturnsSpecInstanceDuration()
        {
            GameplayTag cooldownTag = RegisterTag("Test.GAS.DurationOverride.CooldownInfo");
            var grantedTags = new GameplayTagContainer();
            grantedTags.AddTag(cooldownTag);
            var cooldownDefinition = new GameplayEffect(
                "OverrideDuration.CooldownInfo.GE",
                EDurationPolicy.HasDuration,
                duration: 10f,
                grantedTags: grantedTags);

            var asc = new AbilitySystemComponent();
            var ability = new ComputedCooldownAbility("CooldownInfoAbility", cooldownDefinition, computedCooldownSeconds: 10f);

            GameplayEffectSpec spec = GameplayEffectSpec.Create(cooldownDefinition, asc);
            spec.SetDuration(3.5f);
            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);
            Assert.That(result.Succeeded, Is.True);

            Assert.That(asc.GetCooldownInfo(ability, out float timeRemaining, out float totalDuration), Is.True);
            Assert.That(timeRemaining, Is.EqualTo(3.5f));
            Assert.That(totalDuration, Is.EqualTo(3.5f));

            asc.Dispose();
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities spec duration override test tag.");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.Request(name);
        }

        /// <summary>
        /// Ability that computes its cooldown duration per activation (attack speed / CDR style)
        /// by overriding CreateCooldownEffectSpec and calling SetDuration on the caller-owned spec.
        /// </summary>
        private sealed class ComputedCooldownAbility : GameplayAbility
        {
            private readonly float computedCooldownSeconds;

            public ComputedCooldownAbility(string name, GameplayEffect cooldown, float computedCooldownSeconds)
            {
                this.computedCooldownSeconds = computedCooldownSeconds;
                Initialize(
                    name,
                    EGameplayAbilityInstancingPolicy.InstancedPerActor,
                    EAbilityExecutionPolicy.LocalOnly,
                    null,
                    cooldown,
                    null,
                    null,
                    null,
                    null,
                    null);
            }

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                CommitAbility(actorInfo, spec);
                EndAbility();
            }

            protected override GameplayEffectSpec CreateCooldownEffectSpec(AbilitySystemComponent asc, GameplayAbilitySpec spec)
            {
                GameplayEffectSpec cooldownSpec = base.CreateCooldownEffectSpec(asc, spec);
                cooldownSpec?.SetDuration(computedCooldownSeconds);
                return cooldownSpec;
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new ComputedCooldownAbility(Name, CooldownEffectDefinition, computedCooldownSeconds);
            }

            protected override void ResetRuntimeState()
            {
            }
        }
    }
}
