using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class ExecCalc_Burn : GameplayEffectExecutionCalculation
    {
        public override void GetAttributeCaptureDefinitions(List<GameplayAttributeCaptureDefinition> definitions)
        {
            definitions.Add(new GameplayAttributeCaptureDefinition(
                GASSampleTags.Attribute_Primary_Attack,
                EGameplayEffectAttributeCaptureSource.Source));
        }

        public override void Execute(GameplayEffectSpec spec, GameplayEffectExecutionOutput executionOutput)
        {
            float sourceAttack = spec.TryGetCapturedAttribute(
                EGameplayEffectAttributeCaptureSource.Source,
                GASSampleTags.Attribute_Primary_Attack,
                out var sourceAttackAttribute)
                ? sourceAttackAttribute.CurrentValue
                : 0f;
            float damageToDeal = sourceAttack * 0.3f;

            var damageModifier = new ModifierInfo(GASSampleTags.Attribute_Meta_Damage, EAttributeModifierOperation.Add, new ScalableFloat(damageToDeal));

            executionOutput.Add(damageModifier);
        }
    }
}
