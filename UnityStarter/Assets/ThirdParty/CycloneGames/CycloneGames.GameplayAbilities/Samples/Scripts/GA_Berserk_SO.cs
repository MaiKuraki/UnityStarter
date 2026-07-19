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
    [CreateAssetMenu(menuName = "CycloneGames/GameplayAbilities/Samples/Ability/Berserk (Grants Execute)")]
    public class GA_Berserk_SO : GameplayAbilitySO
    {
        [Header("Berserk Configuration")]
        [Tooltip("The berserk buff that grants the Execute ability")]
        public GameplayEffectSO BerserkBuffEffect;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var berserkEffect = BerserkBuffEffect ? BerserkBuffEffect.GetGameplayEffect() : null;
            var ability = new GA_Berserk(berserkEffect);
            InitializeAbility(ability);
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
            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }
            
            var owner = spec.Owner;
            
            if (berserkBuffEffect != null)
            {
                var effectSpec = GameplayEffectSpec.Create(berserkBuffEffect, owner, spec.Level);
                owner.ApplyGameplayEffectSpecToSelf(effectSpec);
                
                GASLog.Info($"[Berserk] Activated! Execute ability granted.");
            }
            
            EndAbility();
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_Berserk(berserkBuffEffect);
        }
    }
}
