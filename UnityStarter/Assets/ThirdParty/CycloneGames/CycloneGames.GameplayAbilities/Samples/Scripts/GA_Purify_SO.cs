using CycloneGames.GameplayAbilities.Runtime;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class GA_Purify : GameplayAbility
    {
        // The level required to successfully dispel the poison.
        private const int DISPEL_LEVEL_REQUIREMENT = 3;

        public override bool CanActivate(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec)
        {
            if (spec.Level < DISPEL_LEVEL_REQUIREMENT)
            {
                CLogger.LogWarning($"Purify failed: Caster level ({spec.Level}) is below required level ({DISPEL_LEVEL_REQUIREMENT}).");
                return false;
            }
            return base.CanActivate(actorInfo, spec);
        }

        public override void ActivateAbility(GameplayAbilityActorInfo actorInfo, GameplayAbilitySpec spec, GameplayAbilityActivationInfo activationInfo)
        {
            if (CommitAbility(actorInfo, spec))
            {
                var caster = actorInfo.AvatarActor as GameObject;
                // Target self or friendly
                var target = caster;

                if (target != null && target.TryGetComponent<AbilitySystemComponent>(out var targetASC))
                {
                    CLogger.LogInfo($"{caster.name} casts Purify on {target.name}.");

                    // Create a tag container with the tag of the effect we want to remove.
                    var tagsToRemove = new GameplayTagContainer();
                    tagsToRemove.AddTag(GameplayTagManager.RequestTag(ProjectGameplayTags.Debuff_Poison));

                    // This function needs to be implemented in your AbilitySystemComponent.
                    targetASC.RemoveActiveEffectsWithGrantedTags(tagsToRemove);
                }
            }
            EndAbility();
        }

        public override GameplayAbility CreatePoolableInstance() => new GA_Purify();
    }


    [CreateAssetMenu(fileName = "GA_Purify", menuName = "CycloneGames/GameplayAbilitySystem/Samples/Ability/Purify")]
    public class GA_Purify_SO : GameplayAbilitySO
    {
        public override GameplayAbility CreateAbility() => new GA_Purify();
    }
}