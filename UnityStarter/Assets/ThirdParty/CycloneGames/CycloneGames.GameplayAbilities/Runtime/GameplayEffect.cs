using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Defines the immutable data for a gameplay effect. This class is a runtime representation of a GameplayEffectSO.
    /// It is a stateless data container that describes all properties and potential outcomes of an effect,
    /// designed to be shared and reused. An instance of this class is often referred to as a 'GE Definition' or 'CDO'.
    /// </summary>
    public class GameplayEffect
    {
        /// <summary>
        /// The unique name used to identify this effect, primarily for logging and debugging purposes.
        /// </summary>
        public string Name { get; }

        /// <summary>
        /// Defines the lifetime policy of the effect (Instant, HasDuration, Infinite).
        /// </summary>
        /// <remarks>
        /// - <c>Instant</c>: The effect is applied and immediately resolved. It does not persist on the target. Ideal for damage, healing, or resource costs.
        /// - <c>HasDuration</c>: The effect persists on the target for a specified duration. Ideal for buffs, debuffs, and damage-over-time effects.
        /// - <c>Infinite</c>: The effect persists on the target indefinitely until explicitly removed. Ideal for passive effects, stances, or auras.
        /// </remarks>
        public EDurationPolicy DurationPolicy { get; }

        /// <summary>
        /// The total duration of the effect in seconds. This is only used if DurationPolicy is <c>HasDuration</c>.
        /// A value of -1 indicates an infinite duration, though using the <c>Infinite</c> policy is preferred for clarity.
        /// </summary>
        public float Duration { get; }

        /// <summary>
        /// The interval in seconds at which the effect's instant components (Modifiers, Executions) are re-applied.
        /// This is only used for <c>HasDuration</c> and <c>Infinite</c> effects to create periodic behaviors like damage-over-time. A value of 0 or less disables periodic application.
        /// </summary>
        public float Period { get; }

        /// <summary>
        /// A list of attribute modifications to apply to the target. Modifiers are the primary mechanism for predictable attribute changes.
        /// </summary>
        public IReadOnlyList<ModifierInfo> Modifiers { get; }

        /// <summary>
        /// A custom, non-predictable calculation class that can perform complex, multi-attribute logic.
        /// Only executes for <c>Instant</c> and periodic effects. Ideal for complex damage formulas.
        /// </summary>
        public GameplayEffectExecutionCalculation Execution { get; }

        /// <summary>
        /// Defines how this effect interacts with other instances of the same effect on a target, including stacking rules and limits.
        /// </summary>
        public GameplayEffectStacking Stacking { get; }
        
        /// <summary>
        /// A list of abilities to grant to the target for the duration of this effect.
        /// Only applicable to <c>HasDuration</c> and <c>Infinite</c> effects.
        /// </summary>
        public IReadOnlyList<GameplayAbility> GrantedAbilities { get; }

        /// <summary>
        /// A list of GameplayCue tags to trigger when this effect is applied, removed, or executed.
        /// Cues are responsible for non-gameplay visuals and sounds (VFX, SFX).
        /// </summary>
        public GameplayTagContainer GameplayCues { get; }
        
        /// <summary>
        /// Tags that describe the effect itself. These are NOT granted to the target.
        /// They serve as metadata for identifying the effect, e.g., for removal by other systems.
        /// </summary>
        /// <remarks>
        /// Example: An effect might have an AssetTag of 'Damage.Type.Fire'. Another ability could then be designed to remove all effects with this tag.
        /// </remarks>
        public GameplayTagContainer AssetTags { get; }

        /// <summary>
        /// Tags that are temporarily granted to the target's AbilitySystemComponent for the duration of this effect.
        /// This is the primary mechanism for applying temporary states like stuns, buffs, or cooldowns.
        /// </summary>
        /// <remarks>
        /// Example: A cooldown effect grants the 'Cooldown.Skill.Fireball' tag. The Fireball ability's 'CanActivate' check will fail if the caster has this tag.
        /// </remarks>
        public GameplayTagContainer GrantedTags { get; }

        /// <summary>
        /// Defines the tag requirements on a target for this effect to be successfully applied.
        /// If the target does not meet these requirements, the effect application fails.
        /// </summary>
        public GameplayTagRequirements ApplicationTagRequirements { get; }

        /// <summary>
        /// Once applied, the effect will only be active (i.e., its modifiers will apply) if the target continues to meet these tag requirements.
        /// If the requirements are no longer met, the effect is temporarily disabled without being removed.
        /// </summary>
        public GameplayTagRequirements OngoingTagRequirements { get; }

        /// <summary>
        /// Upon successful application of this effect, any active effects on the target that have matching tags in their <c>AssetTags</c> or <c>GrantedTags</c> will be removed.
        /// Ideal for creating dispel effects or effect upgrades.
        /// </summary>
        public GameplayTagContainer RemoveGameplayEffectsWithTags { get; }

        public GameplayEffect(
            string name,
            EDurationPolicy durationPolicy,
            float duration = 0,
            float period = 0,
            List<ModifierInfo> modifiers = null,
            GameplayEffectExecutionCalculation execution = null,
            GameplayEffectStacking stacking = default,
            List<GameplayAbility> grantedAbilities = null,
            GameplayTagContainer assetTags = null,
            GameplayTagContainer grantedTags = null,
            GameplayTagRequirements applicationTagRequirements = default,
            GameplayTagRequirements ongoingTagRequirements = default,
            GameplayTagContainer removeGameplayEffectsWithTags = null,
            GameplayTagContainer gameplayCues = null)
        {
            Name = name;
            DurationPolicy = durationPolicy;
            Duration = duration;
            Period = period;
            Modifiers = modifiers ?? new List<ModifierInfo>();
            Execution = execution;
            Stacking = stacking;
            GrantedAbilities = grantedAbilities ?? new List<GameplayAbility>();
            AssetTags = assetTags ?? new GameplayTagContainer();
            GrantedTags = grantedTags ?? new GameplayTagContainer();
            ApplicationTagRequirements = applicationTagRequirements;
            OngoingTagRequirements = ongoingTagRequirements;
            RemoveGameplayEffectsWithTags = removeGameplayEffectsWithTags ?? new GameplayTagContainer();
            GameplayCues = gameplayCues ?? new GameplayTagContainer();
            
            if (DurationPolicy == EDurationPolicy.HasDuration && duration <= 0 && duration != GameplayEffectConstants.INFINITE_DURATION)
            {
                CLogger.LogWarning($"GameplayEffect '{name}' has 'HasDuration' policy but an invalid duration of {duration}.");
            }
        }
    }
}