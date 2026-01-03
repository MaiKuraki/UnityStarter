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
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Armor Stack")]
    public class GA_ArmorStack_SO : GameplayAbilitySO
    {
        [Header("Stacking Configuration")]
        [Tooltip("The stacking buff effect to apply")]
        public GameplayEffectSO ArmorStackEffect;
        
        [Tooltip("Maximum number of stacks allowed")]
        public int MaxStacks = 5;

        public override GameplayAbility CreateAbility()
        {
            var armorEffect = ArmorStackEffect ? ArmorStackEffect.GetGameplayEffect() : null;
            var ability = new GA_ArmorStack(armorEffect);
            
            ability.Initialize(
                AbilityName,
                InstancingPolicy,
                NetExecutionPolicy,
                CostEffect?.GetGameplayEffect(),
                CooldownEffect?.GetGameplayEffect(),
                AbilityTags,
                ActivationBlockedTags,
                ActivationRequiredTags,
                CancelAbilitiesWithTag,
                BlockAbilitiesWithTag
            );
            
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
            CommitAbility(actorInfo, spec);
            
            var owner = spec.Owner;
            
            if (armorStackEffect != null)
            {
                var effectSpec = GameplayEffectSpec.Create(armorStackEffect, owner, spec.Level);
                owner.ApplyGameplayEffectSpecToSelf(effectSpec);
                
                GASLog.Info($"[ArmorStack] Applied armor stack. View stacks in GAS Debugger.");
            }
            
            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_ArmorStack(this.armorStackEffect);
            
            ability.Initialize(
                this.Name,
                this.InstancingPolicy,
                this.NetExecutionPolicy,
                this.CostEffectDefinition,
                this.CooldownEffectDefinition,
                this.AbilityTags,
                this.ActivationBlockedTags,
                this.ActivationRequiredTags,
                this.CancelAbilitiesWithTag,
                this.BlockAbilitiesWithTag
            );
            return ability;
        }
    }
}
