using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.GameplayAbilities.Core;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A ScriptableObject that serves as a data asset for defining a GameplayAbility's properties in the Unity Editor.
    /// This allows designers to configure abilities without modifying code.
    /// </summary>
    public abstract class GameplayAbilitySO : ScriptableObject, IGASAbilityDefinition
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

        [Header("Ability Tags")]
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

        [Tooltip("Tags that are granted to the owner while this ability is active. Removed when the ability ends.")]
        public GameplayTagContainer ActivationOwnedTags;

        [Header("Source / Target Tags (UE5 Parity)")]
        [Tooltip("The source (owner) must have ALL of these tags for the ability to activate. UE5: SourceRequiredTags.")]
        public GameplayTagContainer SourceRequiredTags;

        [Tooltip("The ability is blocked from activating if the source (owner) has ANY of these tags. UE5: SourceBlockedTags.")]
        public GameplayTagContainer SourceBlockedTags;

        [Tooltip("The target must have ALL of these tags for the ability's effects to be applied. UE5: TargetRequiredTags.")]
        public GameplayTagContainer TargetRequiredTags;

        [Tooltip("The ability's effects are blocked from applying if the target has ANY of these tags. UE5: TargetBlockedTags.")]
        public GameplayTagContainer TargetBlockedTags;

        [Header("Activation")]
        [Tooltip("If true, this ability is automatically activated when granted and deactivated when removed. Used for passive abilities like auras and buffs. UE5: bActivateAbilityOnGranted.")]
        public bool ActivateAbilityOnGranted;

        [Tooltip("Defines triggers that can automatically activate this ability (e.g., on gameplay event or tag change). UE5: FAbilityTriggerData.")]
        public List<AbilityTriggerData> AbilityTriggerDataList;

        /// <summary>
        /// Factory method that creates a runtime instance of the GameplayAbility based on the data in this asset.
        /// </summary>
        /// <returns>A new, initialized GameplayAbility instance.</returns>
        public abstract GameplayAbility CreateAbility();

        /// <summary>
        /// Convenience method for initializing a GameplayAbility with all the fields from this SO.
        /// Subclasses can call this in their CreateAbility() implementation to avoid duplicating initialization code.
        /// </summary>
        protected void InitializeAbility(GameplayAbility ability)
        {
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
                BlockAbilitiesWithTag,
                ActivationOwnedTags,
                ActivateAbilityOnGranted,
                SourceRequiredTags,
                SourceBlockedTags,
                TargetRequiredTags,
                TargetBlockedTags,
                AbilityTriggerDataList
            );
        }
    }
}
