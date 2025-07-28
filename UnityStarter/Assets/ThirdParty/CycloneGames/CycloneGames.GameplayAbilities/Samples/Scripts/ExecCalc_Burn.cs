using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class ExecCalc_Burn : GameplayEffectExecutionCalculation
    {
        public override void Execute(GameplayEffectSpec spec, ref List<ModifierInfo> executionOutput)
        {
            // TODO: you must implement your tur logic in a real project, Here just a example. you can put ExecCalc_BurnSO in the GE_Burn, or GE_Fireball_Impact...

            float MoreDamage = spec.Target.GetAttribute(GASSampleTags.Attribute_Primary_Attack).CurrentValue;

            float damageToDeal = MoreDamage * 0.3f;

            var damageModifier = new ModifierInfo(GASSampleTags.Attribute_Meta_Damage, EAttributeModifierOperation.Add, new ScalableFloat(damageToDeal));

            executionOutput.Add(damageModifier);
        }
    }
}