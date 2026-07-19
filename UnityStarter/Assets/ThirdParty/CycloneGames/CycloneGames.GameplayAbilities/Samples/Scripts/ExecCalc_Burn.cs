using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class ExecCalc_Burn : GameplayEffectExecutionCalculation
    {
        public override void Execute(GameplayEffectSpec spec, GameplayEffectExecutionOutput executionOutput)
        {
            var sourceAttackAttribute = spec.Source?.GetAttribute(GASSampleTags.Attribute_Primary_Attack);
            float sourceAttack = sourceAttackAttribute != null ? sourceAttackAttribute.CurrentValue : 0f;
            float damageToDeal = sourceAttack * 0.3f;

            var damageModifier = new ModifierInfo(GASSampleTags.Attribute_Meta_Damage, EAttributeModifierOperation.Add, new ScalableFloat(damageToDeal));

            executionOutput.Add(damageModifier);
        }
    }
}
