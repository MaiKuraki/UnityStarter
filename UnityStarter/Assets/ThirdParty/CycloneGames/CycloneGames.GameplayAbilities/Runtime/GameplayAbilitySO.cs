using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    // [CreateAssetMenu(fileName = "GA_", menuName = "CycloneGames/GameplayAbilitySystem/GameplayAbility")]
    public abstract class GameplayAbilitySO : ScriptableObject
    {
        public string AbilityName;
        public EGameplayAbilityInstancingPolicy InstancingPolicy;
        public ENetExecutionPolicy NetExecutionPolicy;
        public GameplayEffectSO CostEffect;
        public GameplayEffectSO CooldownEffect;
        public GameplayTagContainer AbilityTags;
        public GameplayTagContainer ActivationBlockedTags;
        public GameplayTagContainer ActivationRequiredTags;
        public GameplayTagContainer CancelAbilitiesWithTag;
        public GameplayTagContainer BlockAbilitiesWithTag;

        public abstract GameplayAbility CreateAbility();
    }
}