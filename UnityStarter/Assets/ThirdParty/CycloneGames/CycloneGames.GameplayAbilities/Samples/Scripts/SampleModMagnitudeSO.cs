using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// Template for a project-authored magnitude asset: the asset is what a modifier references,
    /// the nested calculation is created when the effect definition is built.
    /// </summary>
    [CreateAssetMenu(
        menuName = "CycloneGames/GameplayAbilities/Samples/Magnitude/Sample Attack Magnitude",
        fileName = "CALC_SampleAttackMagnitude")]
    public sealed class SampleModMagnitudeSO : GameplayModMagnitudeCalculationSO
    {
        public override GameplayModMagnitudeCalculation CreateCalculation()
        {
            return new AttackMagnitude();
        }

        private sealed class AttackMagnitude : GameplayModMagnitudeCalculation
        {
            public override void GetAttributeCaptureDefinitions(
                List<GameplayAttributeCaptureDefinition> definitions)
            {
                definitions.Add(new GameplayAttributeCaptureDefinition(
                    GASSampleTags.Attribute_Primary_Attack,
                    EGameplayEffectAttributeCaptureSource.Source));
            }

            public override float CalculateMagnitude(GameplayEffectSpec spec)
            {
                return spec.TryGetCapturedAttribute(
                    EGameplayEffectAttributeCaptureSource.Source,
                    GASSampleTags.Attribute_Primary_Attack,
                    out GameplayAttribute attack)
                    ? attack.CurrentValue
                    : 0f;
            }
        }
    }
}
