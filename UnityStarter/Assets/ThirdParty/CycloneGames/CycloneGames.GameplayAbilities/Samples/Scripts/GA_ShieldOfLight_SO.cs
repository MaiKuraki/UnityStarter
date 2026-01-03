using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Samples
{
    /// <summary>
    /// [ADVANCED] Demonstrates OngoingTagRequirements mechanics.
    /// This ability applies a conditional buff that only stays active when certain tags are present.
    /// 
    /// Key Concepts:
    /// - GameplayEffect.OngoingTagRequirements
    /// - Conditional effect activation
    /// - Effects that pause/resume based on state
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Shield of Light (Conditional)")]
    public class GA_ShieldOfLight_SO : GameplayAbilitySO
    {
        [Header("Shield Configuration")]
        [Tooltip("The conditional defense buff")]
        public GameplayEffectSO ShieldEffect;

        public override GameplayAbility CreateAbility()
        {
            var shieldEffect = ShieldEffect ? ShieldEffect.GetGameplayEffect() : null;
            var ability = new GA_ShieldOfLight(shieldEffect);
            
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

    public class GA_ShieldOfLight : GameplayAbility
    {
        private readonly GameplayEffect shieldEffect;

        public GA_ShieldOfLight(GameplayEffect effect)
        {
            this.shieldEffect = effect;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            CommitAbility(actorInfo, spec);
            
            var owner = spec.Owner;
            
            if (shieldEffect != null)
            {
                var effectSpec = GameplayEffectSpec.Create(shieldEffect, owner, spec.Level);
                owner.ApplyGameplayEffectSpecToSelf(effectSpec);
                
                GASLog.Info("[ShieldOfLight] Shield activated! Defense bonus active ONLY while not debuffed.");
            }
            
            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance()
        {
            var ability = new GA_ShieldOfLight(this.shieldEffect);
            
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
