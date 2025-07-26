using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A ScriptableObject that serves as a data asset for defining a GameplayAbility's properties in the Unity Editor.
    /// This allows designers to configure abilities without modifying code.
    /// </summary>
    public abstract class GameplayAbilitySO : ScriptableObject
    {
        [Tooltip("The display name of the ability, primarily used for debugging and logging.")]
        public string AbilityName;

        [Tooltip("Defines how this ability is instantiated upon activation.")]
        public EGameplayAbilityInstancingPolicy InstancingPolicy;

        [Tooltip("Defines where the ability's logic executes in a networked game.")]
        public ENetExecutionPolicy NetExecutionPolicy;

        [Tooltip("The GameplayEffect asset that defines the resource cost (e.g., mana, stamina) to activate this ability.")]
        public GameplayEffectSO CostEffect;

        [Tooltip("The GameplayEffect asset that puts the ability on cooldown.")]
        public GameplayEffectSO CooldownEffect;

        [Tooltip("Tags that describe the ability itself (e.g., 'Ability.Damage.Fire').")]
        public GameplayTagContainer AbilityTags;

        [Tooltip("This ability is blocked from activating if the owner has ANY of these tags.")]
        public GameplayTagContainer ActivationBlockedTags;

        [Tooltip("The owner must have ALL of these tags for the ability to be activatable.")]
        public GameplayTagContainer ActivationRequiredTags;

        [Tooltip("When this ability is activated, it will cancel any other active abilities that have ANY of these tags.")]
        public GameplayTagContainer CancelAbilitiesWithTag;

        [Tooltip("While this ability is active, other abilities that have ANY of these tags are blocked from activating.")]
        public GameplayTagContainer BlockAbilitiesWithTag;

        /// <summary>
        /// Factory method that creates a runtime instance of the GameplayAbility based on the data in this asset.
        /// </summary>
        /// <returns>A new, initialized GameplayAbility instance.</returns>
        public abstract GameplayAbility CreateAbility();
    }
}
