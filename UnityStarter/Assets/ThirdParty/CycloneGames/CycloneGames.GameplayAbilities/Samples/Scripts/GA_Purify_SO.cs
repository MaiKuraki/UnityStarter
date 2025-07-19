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
                // This ability targets the caster themselves.
                var targetASC = actorInfo.OwnerActor as AbilitySystemComponent;
                if (actorInfo.OwnerActor is Character character) targetASC = character.AbilitySystemComponent;
                if (actorInfo.OwnerActor is AbilitySystemComponentHolder holder) targetASC = holder.AbilitySystemComponent;

                if (targetASC != null)
                {
                    CLogger.LogInfo($"{actorInfo.AvatarActor.GetType().Name} casts Purify on themselves.");

                    // Create a tag container with the tag of the effect we want to remove.
                    var tagsToRemove = new GameplayTagContainer();
                    tagsToRemove.AddTag(GameplayTagManager.RequestTag(GASSampleTags.Debuff_Poison));

                    // This function removes all active effects that grant the specified tag.
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
        public override GameplayAbility CreateAbility()
        {
            var ability = new GA_Purify();
            ability.Initialize(
                AbilityName,
                InstancingPolicy,
                NetExecutionPolicy,
                CostEffect?.CreateGameplayEffect(),
                CooldownEffect?.CreateGameplayEffect(),
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