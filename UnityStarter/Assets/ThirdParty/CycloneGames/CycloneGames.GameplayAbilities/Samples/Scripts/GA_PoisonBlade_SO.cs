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
            // This ability is now cast by an Enemy, so we commit it first.
            CommitAbility(actorInfo, spec);
            
            var caster = actorInfo.AvatarActor as GameObject;
            
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

        public override GameplayAbility CreatePoolableInstance()
        {
            // CHANGED: Pass both effects to the new instance.
            return new GA_PoisonBlade(this.impactDamageEffect, this.poisonEffect);
        }
    }

    [CreateAssetMenu(fileName = "GA_PoisonBlade", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/PoisonBlade")]
    public class GA_PoisonBlade_SO : GameplayAbilitySO
    {
        [Tooltip("The initial, one-time damage applied on hit.")]
        public GameplayEffectSO ImpactDamageEffect;
        
        [Tooltip("The lingering Damage-over-Time effect applied after the initial impact.")]
        public GameplayEffectSO PoisonEffect;

        public override GameplayAbility CreateAbility()
        {
            var impactDamage = ImpactDamageEffect ? ImpactDamageEffect.GetGameplayEffect() : null;
            var poison = PoisonEffect ? PoisonEffect.GetGameplayEffect() : null;
            
            var ability = new GA_PoisonBlade(impactDamage, poison);
            
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
}