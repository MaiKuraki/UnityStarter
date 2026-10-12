using System;
using System.Collections.Generic;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Core;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Tests for declarative DurationMagnitude, the ExtendDuration stacking policy, predicted
    /// stacking fail-closed semantics, and cross-instance replication consistency of per-spec
    /// durations (full-state snapshot round-trip + checksum).
    /// </summary>
    public sealed class GASDurationMagnitudeTests
    {
        #region DurationMagnitude

        [Test]
        public void ScalableFloatDurationMagnitude_ScalesWithLevel()
        {
            var asc = new AbilitySystemComponent();
            var definition = new GameplayEffect(
                "DurationMagnitude.Scalable",
                EDurationPolicy.HasDuration,
                duration: 99f, // Ignored: the magnitude wins.
                durationMagnitude: new DurationMagnitudeInfo(
                    new ScalableFloat(2f) { ScalingFactorPerLevel = 1f }));

            GameplayEffectSpec levelOne = GameplayEffectSpec.Create(definition, asc, level: 1);
            GameplayEffectSpec levelThree = GameplayEffectSpec.Create(definition, asc, level: 3);
            try
            {
                Assert.That(levelOne.DurationRaw, Is.EqualTo(GASFixedValue.FromFloat(2f).RawValue));
                Assert.That(levelThree.DurationRaw, Is.EqualTo(GASFixedValue.FromFloat(4f).RawValue));
            }
            finally
            {
                levelOne.Discard();
                levelThree.Discard();
                asc.Dispose();
            }
        }

        [Test]
        public void AttributeBasedDurationMagnitude_CapturesSourceAttribute()
        {
            var asc = new AbilitySystemComponent();
            var attributes = new DurationAttributeSet();
            attributes.CooldownDuration.SetBaseValue(6f);
            attributes.CooldownDuration.SetCurrentValue(6f);
            asc.AddAttributeSet(attributes);

            var definition = new GameplayEffect(
                "DurationMagnitude.Attribute",
                EDurationPolicy.HasDuration,
                duration: 0f,
                durationMagnitude: new DurationMagnitudeInfo(
                    new AttributeBasedMagnitude(DurationAttributeSet.CooldownDurationName)));

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(definition, asc));
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ActiveEffect.TimeRemainingRaw, Is.EqualTo(GASFixedValue.FromFloat(6f).RawValue),
                "The declarative attribute-driven duration must evaluate through the fixed-point pipeline.");

            asc.Dispose();
        }

        [Test]
        public void SetByCallerDurationMagnitude_UpdatesUntilExplicitOverride()
        {
            GameplayTag durationTag = RegisterTag("Test.GAS.DurationMagnitude.SetByCaller");
            var asc = new AbilitySystemComponent();
            var definition = new GameplayEffect(
                "DurationMagnitude.SetByCaller",
                EDurationPolicy.HasDuration,
                duration: 0f,
                durationMagnitude: new DurationMagnitudeInfo(
                    new SetByCallerMagnitude(durationTag, 10f, warnIfNotFound: false)));

            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
            Assert.That(spec.Duration, Is.EqualTo(10f), "Missing SetByCaller key must fall back to the authored default.");

            spec.SetSetByCallerMagnitude(durationTag, 4f);
            Assert.That(spec.Duration, Is.EqualTo(4f), "SetByCaller updates must re-evaluate the declarative duration.");

            spec.SetDuration(7f);
            spec.SetSetByCallerMagnitude(durationTag, 1f);
            Assert.That(spec.Duration, Is.EqualTo(7f),
                "An explicit SetDuration override always wins over later SetByCaller re-evaluation.");

            GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(spec);
            Assert.That(result.Succeeded, Is.True);
            Assert.That(result.ActiveEffect.TimeRemaining, Is.EqualTo(7f));

            asc.Dispose();
        }

        [Test]
        public void SetByCallerDurationMagnitude_NegativeClampsToZero()
        {
            GameplayTag durationTag = RegisterTag("Test.GAS.DurationMagnitude.Negative");
            var asc = new AbilitySystemComponent();
            var definition = new GameplayEffect(
                "DurationMagnitude.Negative",
                EDurationPolicy.HasDuration,
                duration: 0f,
                durationMagnitude: new DurationMagnitudeInfo(
                    new SetByCallerMagnitude(durationTag, -5f, warnIfNotFound: false)));

            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
            try
            {
                Assert.That(spec.DurationRaw, Is.EqualTo(0L), "Negative evaluated durations clamp to zero deterministically.");
            }
            finally
            {
                spec.Discard();
                asc.Dispose();
            }
        }

        [Test]
        public void DurationMagnitude_OnNonDurationPolicy_Throws()
        {
            var magnitude = new DurationMagnitudeInfo(new ScalableFloat(5f));

            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect("DurationMagnitude.BadInstant", EDurationPolicy.Instant, durationMagnitude: magnitude));
            Assert.Throws<ArgumentException>(() =>
                new GameplayEffect("DurationMagnitude.BadInfinite", EDurationPolicy.Infinite, durationMagnitude: magnitude));
        }

        #endregion

        #region ExtendDuration stacking

        [Test]
        public void ExtendDuration_AccumulatesRemainingTime()
        {
            var asc = new AbilitySystemComponent();
            GameplayEffect definition = CreateExtendDurationDefinition("ExtendDuration.Basic", limit: 5);

            GameplayEffectSpec first = GameplayEffectSpec.Create(definition, asc);
            first.SetDuration(3f);
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(first).Succeeded, Is.True);

            GameplayEffectSpec second = GameplayEffectSpec.Create(definition, asc);
            second.SetDuration(2f);
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(second).Succeeded, Is.True);

            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(2));
            Assert.That(asc.ActiveEffects[0].TimeRemainingRaw,
                Is.EqualTo(GASFixedValue.FromFloat(5f).RawValue),
                "ExtendDuration must sum the new application's duration onto the remaining time (exact fixed-point add).");

            asc.Dispose();
        }

        [Test]
        public void ExtendDuration_AtStackLimit_StillExtends()
        {
            var asc = new AbilitySystemComponent();
            GameplayEffect definition = CreateExtendDurationDefinition("ExtendDuration.AtLimit", limit: 2);

            ApplyWithDuration(asc, definition, 3f);
            ApplyWithDuration(asc, definition, 2f);
            ApplyWithDuration(asc, definition, 1f); // Overflow application at the stack limit.

            Assert.That(asc.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(asc.ActiveEffects[0].StackCount, Is.EqualTo(2));
            Assert.That(asc.ActiveEffects[0].TimeRemainingRaw,
                Is.EqualTo(GASFixedValue.FromFloat(6f).RawValue),
                "An accepted overflow application still extends the duration.");

            asc.Dispose();
        }

        [Test]
        public void ExtendDuration_SaturatesOnOverflow()
        {
            var asc = new AbilitySystemComponent();
            GameplayEffect definition = CreateExtendDurationDefinition("ExtendDuration.Saturate", limit: 5);

            GameplayEffectSpec first = GameplayEffectSpec.Create(definition, asc);
            first.SetDurationRaw(long.MaxValue - 10L);
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(first).Succeeded, Is.True);

            GameplayEffectSpec second = GameplayEffectSpec.Create(definition, asc);
            second.SetDurationRaw(100L);
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(second).Succeeded, Is.True);

            Assert.That(asc.ActiveEffects[0].TimeRemainingRaw, Is.EqualTo(long.MaxValue),
                "Duration accumulation must saturate instead of overflowing to a negative value.");

            asc.Dispose();
        }

        [Test]
        public void ExtendDuration_RequiresClearEntireStackExpiration()
        {
            Assert.Throws<ArgumentException>(() => new GameplayEffect(
                "ExtendDuration.BadExpiration",
                EDurationPolicy.HasDuration,
                duration: 5f,
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.ExtendDuration,
                    EGameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration)));
        }

        #endregion

        #region Prediction and replication consistency

        /// <summary>
        /// Locks the fail-closed design: a predicted application that would stack onto an
        /// existing effect is rejected with PredictionUnsupported before any mutation, so
        /// predicted paths can never perturb the duration timer of an authoritative effect.
        /// </summary>
        [Test]
        public void PredictedStackApplication_FailsClosed_DurationTimerUntouched()
        {
            var replicaContext = new GASRuntimeContext(GASRuntimeAuthorityMode.Replica);
            var replica = new AbilitySystemComponent(replicaContext, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            GameplayEffect definition = CreateRefreshStackingDefinition("PredictedStack.FailClosed", limit: 4);

            // Established (authoritative-looking) local state: one stack at 5s remaining.
            GameplayEffectSpec initial = GameplayEffectSpec.Create(definition, replica);
            initial.SetDuration(5f);
            Assert.That(replica.ApplyGameplayEffectSpecToSelf(initial).Succeeded, Is.True);
            Assert.That(replica.ActiveEffects[0].TimeRemaining, Is.EqualTo(5f));

            GameplayAbilitySpec replicaAbilitySpec = replica.GrantAbility(new PredictionTestAbility());
            GASPredictionKey predictionKey = replica.OpenPredictionWindow(replicaAbilitySpec);
            GameplayEffectApplicationResultCode predictedCode;
            using (replica.BeginPredictionScope(predictionKey))
            {
                GameplayEffectSpec predictedSpec = GameplayEffectSpec.Create(definition, replica);
                predictedSpec.SetDuration(9f);
                predictedCode = replica.ApplyGameplayEffectSpecToSelf(predictedSpec).Code;
            }

            Assert.That(predictedCode, Is.EqualTo(GameplayEffectApplicationResultCode.PredictionUnsupported),
                "Predicted stack-onto-existing must fail closed.");
            Assert.That(replica.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(replica.ActiveEffects[0].StackCount, Is.EqualTo(1));
            Assert.That(replica.ActiveEffects[0].TimeRemaining, Is.EqualTo(5f),
                "A rejected predicted application must leave the duration timer untouched.");

            Assert.That(replica.RollbackPredictionWindow(predictionKey), Is.True);
            Assert.That(replica.ActiveEffects[0].TimeRemaining, Is.EqualTo(5f));

            replica.ClearAbility(replicaAbilitySpec);
            replica.Dispose();
            replicaContext.Dispose();
        }

        [Test]
        public void FullStateSync_PreservesOverriddenDurationAndChecksum()
        {
            var context = new GASRuntimeContext();
            var authority = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replica = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);

            var definition = new GameplayEffect("Sync.OverriddenDuration", EDurationPolicy.HasDuration, duration: 10f);
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, authority);
            spec.SetDuration(3.5f);
            Assert.That(authority.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);

            var snapshot = new GASAbilitySystemFullStateBuffer();
            authority.CaptureFullStateNonAlloc(snapshot);
            Assert.That(replica.TryApplyFullStateSnapshot(snapshot, out GASStateDeltaRejectionReason reason), Is.True);

            Assert.That(replica.ActiveEffects.Count, Is.EqualTo(1));
            Assert.That(replica.ActiveEffects[0].Spec.DurationRaw,
                Is.EqualTo(GASFixedValue.FromFloat(3.5f).RawValue),
                "The per-spec overridden duration must survive the replication round-trip.");
            Assert.That(replica.ComputeReplicatedStateChecksum(), Is.EqualTo(snapshot.StateChecksum),
                "Cross-instance checksum must match: duration overrides are part of the replicated contract.");

            authority.Dispose();
            replica.Dispose();
            context.Dispose();
        }

        [Test]
        public void FullStateSync_PreservesDurationMagnitudeEvaluation()
        {
            var context = new GASRuntimeContext();
            var authority = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var authorityAttributes = new DurationAttributeSet();
            authorityAttributes.CooldownDuration.SetBaseValue(6f);
            authorityAttributes.CooldownDuration.SetCurrentValue(6f);
            authority.AddAttributeSet(authorityAttributes);
            var replica = new AbilitySystemComponent(context, GASAbilitySystemRuntimeOptions.RuntimeOnly);
            var replicaAttributes = new DurationAttributeSet();
            replicaAttributes.CooldownDuration.SetBaseValue(6f);
            replicaAttributes.CooldownDuration.SetCurrentValue(6f);
            replica.AddAttributeSet(replicaAttributes);

            var definition = new GameplayEffect(
                "Sync.DurationMagnitude",
                EDurationPolicy.HasDuration,
                duration: 0f,
                durationMagnitude: new DurationMagnitudeInfo(
                    new AttributeBasedMagnitude(DurationAttributeSet.CooldownDurationName)));
            Assert.That(authority.ApplyGameplayEffectSpecToSelf(
                GameplayEffectSpec.Create(definition, authority)).Succeeded, Is.True);

            var snapshot = new GASAbilitySystemFullStateBuffer();
            authority.CaptureFullStateNonAlloc(snapshot);
            Assert.That(replica.TryApplyFullStateSnapshot(snapshot, out GASStateDeltaRejectionReason reason), Is.True);

            Assert.That(replica.ActiveEffects[0].Spec.DurationRaw,
                Is.EqualTo(GASFixedValue.FromFloat(6f).RawValue),
                "The server evaluates the magnitude once; clients consume the replicated raw duration " +
                "without re-evaluating, so cross-device results cannot diverge.");
            Assert.That(replica.ComputeReplicatedStateChecksum(), Is.EqualTo(snapshot.StateChecksum));

            authority.Dispose();
            replica.Dispose();
            context.Dispose();
        }

        #endregion

        #region Helpers

        private static GameplayEffect CreateExtendDurationDefinition(string name, int limit)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.HasDuration,
                duration: 10f,
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    limit,
                    EGameplayEffectStackingDurationPolicy.ExtendDuration));
        }

        private static GameplayEffect CreateRefreshStackingDefinition(string name, int limit)
        {
            return new GameplayEffect(
                name,
                EDurationPolicy.HasDuration,
                duration: 10f,
                stacking: new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    limit,
                    EGameplayEffectStackingDurationPolicy.RefreshOnSuccessfulApplication));
        }

        private static void ApplyWithDuration(AbilitySystemComponent asc, GameplayEffect definition, float seconds)
        {
            GameplayEffectSpec spec = GameplayEffectSpec.Create(definition, asc);
            spec.SetDuration(seconds);
            Assert.That(asc.ApplyGameplayEffectSpecToSelf(spec).Succeeded, Is.True);
        }

        private static GameplayTag RegisterTag(string name)
        {
            GameplayTagManager.RegisterDynamicTag(name, "GameplayAbilities duration magnitude test tag.");
            GameplayTagManager.InitializeIfNeeded();
            return GameplayTagManager.Request(name);
        }

        private sealed class DurationAttributeSet : AttributeSet
        {
            public const string CooldownDurationName = "CooldownDuration";
            public GameplayAttribute CooldownDuration { get; } = new GameplayAttribute(CooldownDurationName);

            protected override void RegisterAttributes()
            {
                RegisterAttribute(CooldownDuration);
            }
        }

        private sealed class PredictionTestAbility : GameplayAbility
        {
            public PredictionTestAbility()
            {
                Initialize(
                    "PredictionTestAbility",
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

            public override void ActivateAbility(
                GameplayAbilityActorInfo actorInfo,
                GameplayAbilitySpec spec,
                GameplayAbilityActivationInfo activationInfo)
            {
                EndAbility();
            }

            public override GameplayAbility CreateRuntimeInstance()
            {
                return new PredictionTestAbility();
            }

            protected override void ResetRuntimeState()
            {
            }
        }

        #endregion
    }
}
