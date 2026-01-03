using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Samples
{
    /// <summary>
    /// [ADVANCED] Demonstrates GrantedAbility mechanics.
    /// This ability applies a buff that grants a temporary ability.
    /// 
    /// Key Concepts:
    /// - GameplayEffect.GrantedAbilities
    /// - Temporary ability granting
    /// - Effect-linked ability lifetime
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Berserk (Grants Execute)")]
    public class GA_Berserk_SO : GameplayAbilitySO
    {
        [Header("Berserk Configuration")]
        [Tooltip("The berserk buff that grants the Execute ability")]
        public GameplayEffectSO BerserkBuffEffect;

        public override GameplayAbility CreateAbility()
        {
            var berserkEffect = BerserkBuffEffect ? BerserkBuffEffect.GetGameplayEffect() : null;
            var ability = new GA_Berserk(berserkEffect);
            
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

    public class GA_Berserk : GameplayAbility
    {
        private readonly GameplayEffect berserkBuffEffect;

        public GA_Berserk(GameplayEffect buffEffect)
        {
            this.berserkBuffEffect = buffEffect;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            CommitAbility(actorInfo, spec);
            
            var owner = spec.Owner;
            
            if (berserkBuffEffect != null)
            {
                var effectSpec = GameplayEffectSpec.Create(berserkBuffEffect, owner, spec.Level);
                owner.ApplyGameplayEffectSpecToSelf(effectSpec);
                
                GASLog.Info($"[Berserk] Activated! Execute ability granted.");
            }
            
            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_Berserk(this.berserkBuffEffect);
            
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
