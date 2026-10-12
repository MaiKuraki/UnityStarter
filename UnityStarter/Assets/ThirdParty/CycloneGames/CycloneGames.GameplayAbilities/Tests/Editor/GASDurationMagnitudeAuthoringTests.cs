using System;

using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Unity.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    /// <summary>
    /// Tests for the ScriptableObject authoring path of data-driven durations and stacking:
    /// DurationMagnitudeSerializable conversion, ConcreteGameplayEffectSO end-to-end definition
    /// construction, and authoring-time validation constraints.
    /// </summary>
    public sealed class GASDurationMagnitudeAuthoringTests
    {
        [Test]
        public void DurationMagnitudeSerializable_Disabled_ReturnsNull()
        {
            var authoring = new DurationMagnitudeSerializable { Enabled = false };
            Assert.That(authoring.ToRuntimeInfo(), Is.Null);
        }

        [Test]
        public void DurationMagnitudeSerializable_AttributeBased_PreservesAllFields()
        {
            var authoring = new DurationMagnitudeSerializable
            {
                Enabled = true,
                CalculationType = EGameplayEffectMagnitudeCalculation.AttributeBased,
                BackingAttributeName = "CooldownDuration",
                AttributeCaptureSource = EGameplayEffectAttributeCaptureSource.Target,
                AttributeCalculationType = EAttributeBasedFloatCalculationType.AttributeBaseValue,
                AttributeSnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.NotSnapshot,
                AttributeCoefficient = new ScalableFloat(0.75f),
                AttributePreMultiplyAdditiveValue = new ScalableFloat(1f),
                AttributePostMultiplyAdditiveValue = new ScalableFloat(2f)
            };

            DurationMagnitudeInfo info = authoring.ToRuntimeInfo();

            Assert.That(info, Is.Not.Null);
            Assert.That(info.CalculationType, Is.EqualTo(EGameplayEffectMagnitudeCalculation.AttributeBased));
            Assert.That(info.AttributeBased.AttributeName, Is.EqualTo("CooldownDuration"));
            Assert.That(info.AttributeBased.CaptureSource, Is.EqualTo(EGameplayEffectAttributeCaptureSource.Target));
            Assert.That(info.AttributeBased.CalculationType, Is.EqualTo(EAttributeBasedFloatCalculationType.AttributeBaseValue));
            Assert.That(info.AttributeBased.SnapshotPolicy, Is.EqualTo(EGameplayEffectAttributeCaptureSnapshot.NotSnapshot));
            Assert.That(info.AttributeBased.Coefficient.GetValueAtLevel(1), Is.EqualTo(0.75f));
            Assert.That(info.AttributeBased.PreMultiplyAdditiveValue.GetValueAtLevel(1), Is.EqualTo(1f));
            Assert.That(info.AttributeBased.PostMultiplyAdditiveValue.GetValueAtLevel(1), Is.EqualTo(2f));
        }

        [Test]
        public void DurationMagnitudeSerializable_SetByCaller_PrefersTagKey()
        {
            var authoring = new DurationMagnitudeSerializable
            {
                Enabled = true,
                CalculationType = EGameplayEffectMagnitudeCalculation.SetByCaller,
                SetByCallerDataTag = new SerializableGameplayTag("Data.Cooldown"),
                SetByCallerDataName = "FallbackName",
                SetByCallerDefaultValue = 3.5f,
                WarnIfSetByCallerMissing = false
            };

            DurationMagnitudeInfo info = authoring.ToRuntimeInfo();

            Assert.That(info, Is.Not.Null);
            Assert.That(info.CalculationType, Is.EqualTo(EGameplayEffectMagnitudeCalculation.SetByCaller));
            Assert.That(info.SetByCaller.DefaultValue, Is.EqualTo(3.5f));
        }

        [Test]
        public void ConcreteEffectSO_ScalableFloatDurationMagnitude_EndToEnd()
        {
            var effectSO = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                effectSO.EffectName = "Authoring.ScalableDuration";
                effectSO.DurationPolicy = EDurationPolicy.HasDuration;
                effectSO.Duration = 0f;
                effectSO.DurationMagnitude = new DurationMagnitudeSerializable
                {
                    Enabled = true,
                    CalculationType = EGameplayEffectMagnitudeCalculation.ScalableFloat,
                    Magnitude = new ScalableFloat(6f)
                };

                GameplayEffect definition = effectSO.GetGameplayEffect();
                Assert.That(definition.DurationMagnitude, Is.Not.Null);

                var asc = new AbilitySystemComponent();
                GameplayEffectApplicationResult result = asc.ApplyGameplayEffectSpecToSelf(
                    GameplayEffectSpec.Create(definition, asc));
                Assert.That(result.Succeeded, Is.True);
                Assert.That(result.ActiveEffect.TimeRemainingRaw, Is.EqualTo(GASFixedValue.FromFloat(6f).RawValue));
                asc.Dispose();
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(effectSO);
            }
        }

        [Test]
        public void ConcreteEffectSO_ExtendDurationStacking_PassesThrough()
        {
            var effectSO = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                effectSO.EffectName = "Authoring.ExtendDuration";
                effectSO.DurationPolicy = EDurationPolicy.HasDuration;
                effectSO.Duration = 5f;
                effectSO.Stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.ExtendDuration);

                GameplayEffect definition = effectSO.GetGameplayEffect();
                Assert.That(definition.Stacking.DurationPolicy, Is.EqualTo(EGameplayEffectStackingDurationPolicy.ExtendDuration));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(effectSO);
            }
        }

        [Test]
        public void ConcreteEffectSO_ExtendDurationWithPerStackExpiry_RejectedAtDefinitionBuild()
        {
            var effectSO = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                effectSO.EffectName = "Authoring.ExtendDuration.Invalid";
                effectSO.DurationPolicy = EDurationPolicy.HasDuration;
                effectSO.Duration = 5f;
                effectSO.Stacking = new GameplayEffectStacking(
                    EGameplayEffectStackingType.AggregateByTarget,
                    3,
                    EGameplayEffectStackingDurationPolicy.ExtendDuration,
                    EGameplayEffectStackingExpirationPolicy.RemoveSingleStackAndRefreshDuration);

                Assert.Throws<ArgumentException>(() => effectSO.GetGameplayEffect());
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(effectSO);
            }
        }
    }
}
