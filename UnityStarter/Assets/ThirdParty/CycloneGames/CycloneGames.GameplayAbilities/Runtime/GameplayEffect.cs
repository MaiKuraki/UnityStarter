using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A GameplayEffect Definition (Blueprint).
    /// This is an immutable data container that describes all potential effects of a GameplayEffect.
    /// It is stateless and can be shared and reused.
    /// </summary>
    public class GameplayEffect
    {
        public string Name { get; }
        public EDurationPolicy DurationPolicy { get; }
        public float Duration { get; }
        public List<ModifierInfo> Modifiers { get; }
        public GameplayEffectExecutionCalculation Execution { get; }
        public GameplayEffectStacking Stacking { get; }

        // --- Granted Abilities ---
        public List<GameplayAbility> GrantedAbilities { get; }

        // --- GameplayCues ---
        public List<GameplayTag> GameplayCues { get; }

        // --- Tag-related properties ---
        /// <summary>
        /// Descriptive tags for the effect itself. Does not grant tags to the owner. Used for identification.
        /// </summary>
        public GameplayTagContainer AssetTags { get; }
        /// <summary>
        /// Tags that are granted to the actor while the effect is active.
        /// </summary>
        public GameplayTagContainer GrantedTags { get; }
        /// <summary>
        /// This effect can only be applied if the target meets these tag requirements.
        /// </summary>
        public GameplayTagRequirements ApplicationTagRequirements { get; }
        /// <summary>
        /// Once applied, this effect is active only if the target meets these requirements.
        /// </summary>
        public GameplayTagRequirements OngoingTagRequirements { get; }
        /// <summary>
        /// GameplayEffects on the target that have any of these asset or granted tags will be removed.
        /// </summary>
        public GameplayTagContainer RemoveGameplayEffectsWithTags { get; }

        public GameplayEffect(
            string name,
            EDurationPolicy durationPolicy,
            float duration = 0,
            List<ModifierInfo> modifiers = null,
            GameplayEffectExecutionCalculation execution = null,
            GameplayEffectStacking stacking = default,
            List<GameplayAbility> grantedAbilities = null,
            GameplayTagContainer assetTags = null,
            GameplayTagContainer grantedTags = null,
            GameplayTagRequirements applicationTagRequirements = default,
            GameplayTagRequirements ongoingTagRequirements = default,
            GameplayTagContainer removeGameplayEffectsWithTags = null,
            List<GameplayTag> gameplayCues = null)
        {
            Name = name;
            DurationPolicy = durationPolicy;
            Duration = duration;
            Modifiers = modifiers ?? new List<ModifierInfo>();
            Execution = execution;
            Stacking = stacking;
            GrantedAbilities = grantedAbilities ?? new List<GameplayAbility>();
            AssetTags = assetTags ?? new GameplayTagContainer();
            GrantedTags = grantedTags ?? new GameplayTagContainer();
            ApplicationTagRequirements = applicationTagRequirements;
            OngoingTagRequirements = ongoingTagRequirements;
            RemoveGameplayEffectsWithTags = removeGameplayEffectsWithTags ?? new GameplayTagContainer();
            GameplayCues = gameplayCues ?? new List<GameplayTag>();
            
            if (DurationPolicy == EDurationPolicy.HasDuration && duration <= 0 && duration != GameplayEffectConstants.INFINITE_DURATION)
            {
                CLogger.LogWarning($"GameplayEffect '{name}' has 'HasDuration' policy but an invalid duration of {duration}.");
            }
        }
    }
}