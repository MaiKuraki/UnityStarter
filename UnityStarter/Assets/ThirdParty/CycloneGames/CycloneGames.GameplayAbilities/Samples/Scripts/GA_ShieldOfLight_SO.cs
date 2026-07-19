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
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Shield of Light (Conditional)")]
    public class GA_ShieldOfLight_SO : GameplayAbilitySO
    {
        [Header("Shield Configuration")]
        [Tooltip("The conditional defense buff")]
        public GameplayEffectSO ShieldEffect;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var shieldEffect = ShieldEffect ? ShieldEffect.GetGameplayEffect() : null;
            var ability = new GA_ShieldOfLight(shieldEffect);
            InitializeAbility(ability);
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
            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }
            
            var owner = spec.Owner;
            
            if (shieldEffect != null)
            {
                var effectSpec = GameplayEffectSpec.Create(shieldEffect, owner, spec.Level);
                owner.ApplyGameplayEffectSpecToSelf(effectSpec);
                
                GASLog.Info("[ShieldOfLight] Shield activated! Defense bonus active ONLY while not debuffed.");
            }
            
            EndAbility();
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_ShieldOfLight(shieldEffect);
        }
    }
}
