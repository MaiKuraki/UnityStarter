using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Samples
{
    /// <summary>
    /// [INTERMEDIATE] Demonstrates effect stacking mechanics.
    /// This ability grants a stacking armor buff that increases with each application.
    /// 
    /// Key Concepts:
    /// - EGameplayEffectStackingType.AggregateByTarget
    /// - Stack limit configuration
    /// - Duration refresh on stack application
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Armor Stack")]
    public class GA_ArmorStack_SO : GameplayAbilitySO
    {
        [Header("Stacking Configuration")]
        [Tooltip("The stacking buff effect to apply")]
        public GameplayEffectSO ArmorStackEffect;
        
        [Tooltip("Maximum number of stacks allowed")]
        public int MaxStacks = 5;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var armorEffect = ArmorStackEffect ? ArmorStackEffect.GetGameplayEffect() : null;
            var ability = new GA_ArmorStack(armorEffect);
            InitializeAbility(ability);
            return ability;
        }
    }

    public class GA_ArmorStack : GameplayAbility
    {
        private readonly GameplayEffect armorStackEffect;

        public GA_ArmorStack(GameplayEffect armorEffect)
        {
            this.armorStackEffect = armorEffect;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }
            
            var owner = spec.Owner;
            
            if (armorStackEffect != null)
            {
                var effectSpec = GameplayEffectSpec.Create(armorStackEffect, owner, spec.Level);
                owner.ApplyGameplayEffectSpecToSelf(effectSpec);
                
                GASLog.Info($"[ArmorStack] Applied armor stack. View stacks in GAS Debugger.");
            }
            
            EndAbility();
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_ArmorStack(armorStackEffect);
        }
    }
}
