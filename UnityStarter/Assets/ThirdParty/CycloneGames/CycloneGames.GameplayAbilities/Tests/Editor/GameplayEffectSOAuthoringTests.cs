using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Tests.Editor
{
    public sealed class GameplayEffectSOAuthoringTests
    {
        [Test]
        public void CustomMagnitudeAsset_IsMappedIntoRuntimeModifier()
        {
            var effectSO = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            var calculationSO = ScriptableObject.CreateInstance<ConstantMagnitudeSO>();
            try
            {
                effectSO.EffectName = "SO.Custom";
                effectSO.DurationPolicy = EDurationPolicy.Infinite;
                effectSO.SerializableModifiers = new List<ModifierInfoSerializable>
                {
                    new ModifierInfoSerializable
                    {
                        AttributeName = "Health",
                        Operation = EAttributeModifierOperation.Add,
                        MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.CustomCalculation,
                        CustomCalculation = calculationSO,
                        AttributeSnapshotPolicy = EGameplayEffectAttributeCaptureSnapshot.NotSnapshot
                    }
                };

                GameplayEffect effect = effectSO.GetGameplayEffect();

                Assert.That(effect.Modifiers.Count, Is.EqualTo(1));
                Assert.That(effect.Modifiers[0].MagnitudeCalculationType,
                    Is.EqualTo(EGameplayEffectMagnitudeCalculation.CustomCalculation));
                Assert.That(effect.Modifiers[0].CustomCalculation, Is.Not.Null);
                Assert.That(effect.Modifiers[0].SnapshotPolicy,
                    Is.EqualTo(EGameplayEffectAttributeCaptureSnapshot.NotSnapshot));
            }
            finally
            {
                Object.DestroyImmediate(effectSO);
                Object.DestroyImmediate(calculationSO);
            }
        }

        [Test]
        public void CustomMagnitudeWithoutAsset_FallsBackToScalableFloat()
        {
            var effectSO = ScriptableObject.CreateInstance<ConcreteGameplayEffectSO>();
            try
            {
                effectSO.EffectName = "SO.CustomMissing";
                effectSO.DurationPolicy = EDurationPolicy.Infinite;
                effectSO.SerializableModifiers = new List<ModifierInfoSerializable>
                {
                    new ModifierInfoSerializable
                    {
                        AttributeName = "Health",
                        Operation = EAttributeModifierOperation.Add,
                        MagnitudeCalculationType = EGameplayEffectMagnitudeCalculation.CustomCalculation,
                        Magnitude = new ScalableFloat(3f)
                    }
                };

                GameplayEffect effect = effectSO.GetGameplayEffect();

                Assert.That(effect.Modifiers[0].MagnitudeCalculationType,
                    Is.EqualTo(EGameplayEffectMagnitudeCalculation.ScalableFloat));
                Assert.That(effect.Modifiers[0].CalculateMagnitude(null, 1), Is.EqualTo(3f));
            }
            finally
            {
                Object.DestroyImmediate(effectSO);
            }
        }

        private sealed class ConstantMagnitudeSO : GameplayModMagnitudeCalculationSO
        {
            public override GameplayModMagnitudeCalculation CreateCalculation()
            {
                return new ConstantMagnitude();
            }
        }

        private sealed class ConstantMagnitude : GameplayModMagnitudeCalculation
        {
            public override float CalculateMagnitude(GameplayEffectSpec spec)
            {
                return 7f;
            }
        }
    }
}
