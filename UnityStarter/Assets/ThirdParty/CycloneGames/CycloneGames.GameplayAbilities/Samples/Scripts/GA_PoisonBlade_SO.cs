using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_PoisonBlade : GameplayAbility
    {
        private readonly GameplayEffect poisonEffect;

        public GA_PoisonBlade(GameplayEffect poisonEffect)
        {
            this.poisonEffect = poisonEffect;
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (CommitAbility(actorInfo, spec))
            {
                var caster = actorInfo.AvatarActor as GameObject;
                // Similar targeting logic as Fireball
                var target = FindTarget(caster);

                if (target != null && target.TryGetComponent<AbilitySystemComponent>(out var targetASC))
                {
                    CLogger.LogInfo($"{caster.name} applies Poison to {target.name}");
                    var poisonSpec = GameplayEffectSpec.Create(poisonEffect, AbilitySystemComponent, spec.Level);
                    targetASC.ApplyGameplayEffectSpecToSelf(poisonSpec);
                }
            }
            EndAbility();
        }

        // You can share targeting logic in a base class or utility class
        private GameObject FindTarget(GameObject caster)
        {
            // Simple forward raycast
            if (Physics.Raycast(caster.transform.position, caster.transform.forward, out RaycastHit hit, 10f))
            {
                if (hit.collider.CompareTag("Enemy")) return hit.collider.gameObject;
            }
            return null;
        }

        public override GameplayAbility CreatePoolableInstance() => new GA_PoisonBlade(poisonEffect);
    }

    [CreateAssetMenu(fileName = "GA_PoisonBlade", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/PoisonBlade")]
    public class GA_PoisonBlade_SO : GameplayAbilitySO
    {
        public GameplayEffectSO PoisonEffect;
        public override GameplayAbility CreateAbility() => new GA_PoisonBlade(PoisonEffect.CreateGameplayEffect());
    }
}