using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Samples
{
    /// <summary>
    /// [ADVANCED] The "Execute" ability granted by the Berserk buff.
    /// This ability is only usable while the Berserk effect is active.
    /// 
    /// Key Concepts:
    /// - Abilities granted temporarily by effects
    /// - High damage finisher ability
    /// </summary>
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Execute (Granted)")]
    public class GA_Execute_SO : GameplayAbilitySO
    {
        [Header("Execute Configuration")]
        [Tooltip("Base damage multiplier")]
        public float DamageMultiplier = 3f;
        
        [Tooltip("The damage effect to apply")]
        public GameplayEffectSO ExecuteDamageEffect;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var damageEffect = ExecuteDamageEffect ? ExecuteDamageEffect.GetGameplayEffect() : null;
            var ability = new GA_Execute(damageEffect, DamageMultiplier);
            InitializeAbility(ability);
            return ability;
        }
    }

    public class GA_Execute : GameplayAbility
    {
        private readonly GameplayEffect executeDamageEffect;
        private readonly float damageMultiplier;

        public GA_Execute(GameplayEffect damageEffect, float multiplier)
        {
            this.executeDamageEffect = damageEffect;
            this.damageMultiplier = multiplier;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }
            
            GASLog.Info($"[Execute] EXECUTE! Dealing {damageMultiplier}x damage!");
            
            // In production, you would get the target from a targeting system
            // and apply the damage effect to them
            if (executeDamageEffect != null)
            {
                var owner = spec.Owner;
                var effectSpec = GameplayEffectSpec.Create(executeDamageEffect, owner, spec.Level);
                effectSpec.SetSetByCallerMagnitude("Damage.Multiplier", damageMultiplier);
                
                // Apply to target (demo: self)
                // targetASC.ApplyGameplayEffectSpec(effectSpec);
            }
            
            EndAbility();
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_Execute(executeDamageEffect, damageMultiplier);
        }
    }
}
