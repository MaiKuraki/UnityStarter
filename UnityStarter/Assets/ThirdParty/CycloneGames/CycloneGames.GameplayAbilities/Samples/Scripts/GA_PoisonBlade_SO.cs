using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_PoisonBlade : GameplayAbility
    {
        private readonly GameplayEffect impactDamageEffect;
        private readonly GameplayEffect poisonEffect;

        public GA_PoisonBlade(GameplayEffect impactDamage, GameplayEffect poison)
        {
            this.impactDamageEffect = impactDamage;
            this.poisonEffect = poison;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (!CommitAbility(actorInfo, spec).Succeeded)
            {
                EndAbility();
                return;
            }
            
            var caster = actorInfo.AvatarGameObject;
            
            var target = FindTarget(caster);

            if (target != null && target.TryGetComponent<AbilitySystemComponentHolder>(out var holder))
            {
                var targetASC = holder.AbilitySystemComponent;
                CLogger.LogInfo($"{caster.name} strikes {target.name} with Poison Blade.");

                // --- Apply Effects in Sequence ---

                // Apply the initial impact damage effect.
                if (impactDamageEffect != null)
                {
                    var impactSpec = GameplayEffectSpec.Create(impactDamageEffect, AbilitySystemComponent, spec.Level);
                    targetASC.ApplyGameplayEffectSpecToSelf(impactSpec);
                }

                // Apply the lingering poison DoT effect.
                if (poisonEffect != null)
                {
                    var poisonSpec = GameplayEffectSpec.Create(poisonEffect, AbilitySystemComponent, spec.Level);
                    targetASC.ApplyGameplayEffectSpecToSelf(poisonSpec);
                }
            }
            else
            {
                CLogger.LogWarning($"{caster.name}'s Poison Blade found no valid target.");
            }

            EndAbility();
        }

        // A placeholder for a real AI targeting system.
        private GameObject FindTarget(GameObject caster)
        {
            // For this example, we'll simply find the Player object by name.
            // A real game would use a more robust system (e.g., threat table, proximity checks).
            return GameObject.Find("Player");
        }

        public override GameplayAbility CreateRuntimeInstance()
        {
            return new GA_PoisonBlade(impactDamageEffect, poisonEffect);
        }
    }

    [CreateAssetMenu(fileName = "GA_PoisonBlade", menuName = "CycloneGames/GameplayAbilities/Samples/Ability/PoisonBlade")]
    public class GA_PoisonBlade_SO : GameplayAbilitySO
    {
        [Tooltip("The initial, one-time damage applied on hit.")]
        public GameplayEffectSO ImpactDamageEffect;
        
        [Tooltip("The lingering Damage-over-Time effect applied after the initial impact.")]
        public GameplayEffectSO PoisonEffect;

        protected override GameplayAbility CreateGameplayAbility()
        {
            var impactDamage = ImpactDamageEffect ? ImpactDamageEffect.GetGameplayEffect() : null;
            var poison = PoisonEffect ? PoisonEffect.GetGameplayEffect() : null;

            var ability = new GA_PoisonBlade(impactDamage, poison);
            InitializeAbility(ability);
            return ability;
        }
    }
}
