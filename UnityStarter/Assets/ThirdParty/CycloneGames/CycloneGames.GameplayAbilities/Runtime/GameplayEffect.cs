using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;

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
        public EDurationPolicy DurationPolicy { get; }

        /// <summary>
        /// The total duration of the effect in seconds. This is only used if DurationPolicy is <c>HasDuration</c>.
        /// </summary>
        public float Duration { get; }

        /// <summary>
        /// The interval in seconds at which the effect's instant components are re-applied.
        /// </summary>
        public float Period { get; }

        /// <summary>
        /// A list of attribute modifications to apply to the target.
        /// </summary>
        public IReadOnlyList<ModifierInfo> Modifiers { get; }

        /// <summary>
        /// A custom, non-predictable calculation class that can perform complex, multi-attribute logic.
        /// </summary>
        public GameplayEffectExecutionCalculation Execution { get; }

        /// <summary>
        /// Defines how this effect interacts with other instances of the same effect on a target.
        /// </summary>
        public GameplayEffectStacking Stacking { get; }

        /// <summary>
        /// A list of abilities to grant to the target for the duration of this effect.
        /// </summary>
        public IReadOnlyList<GameplayAbility> GrantedAbilities { get; }

        /// <summary>
        /// A list of GameplayCue tags to trigger when this effect is applied, removed, or executed.
        /// </summary>
        public GameplayTagContainer GameplayCues { get; }

        /// <summary>
        /// Tags that describe the effect itself. These are NOT granted to the target.
        /// </summary>
        public GameplayTagContainer AssetTags { get; }

        /// <summary>
        /// Tags that are temporarily granted to the target's AbilitySystemComponent for the duration of this effect.
        /// </summary>
        public GameplayTagContainer GrantedTags { get; }

        /// <summary>
        /// Defines the tag requirements on a target for this effect to be successfully applied.
        /// </summary>
        public GameplayTagRequirements ApplicationTagRequirements { get; }

        /// <summary>
        /// Once applied, the effect will only be active if the target continues to meet these tag requirements.
        /// </summary>
        public GameplayTagRequirements OngoingTagRequirements { get; }

        /// <summary>
        /// Upon successful application, any active effects on the target matching these tags will be removed.
        /// </summary>
        public GameplayTagContainer RemoveGameplayEffectsWithTags { get; }

        /// <summary>
        /// If true, gameplay cues (VFX/SFX) are suppressed for this effect.
        /// UE5: bSuppressGameplayCues on UGameplayEffect.
        /// Useful for silent/debug application without visual feedback.
        /// </summary>
        public bool SuppressGameplayCues { get; }

        /// <summary>
        /// If true, gameplay effects applied by the granting ability are automatically removed when the ability ends.
        /// UE5: RemoveGameplayEffectContainerOnAbilityEnd / bRemoveGameplayEffectsAfterAbilityEnds.
        /// </summary>
        public bool RemoveGameplayEffectsAfterAbilityEnds { get; }

        /// <summary>
        /// Optional custom application requirement. If set, CanApplyGameplayEffect is called before application.
        /// UE5: TArray&lt;TSubclassOf&lt;UGameplayEffectCustomApplicationRequirement&gt;&gt;.
        /// </summary>
        public IReadOnlyList<ICustomApplicationRequirement> CustomApplicationRequirements { get; }

        /// <summary>
        /// If true, periodic effects execute their first tick immediately upon application.
        /// If false, the first execution waits for the full period interval.
        /// UE5: bExecutePeriodicEffectOnApplication. Default is true (UE5 default).
        /// </summary>
        public bool ExecutePeriodicEffectOnApplication { get; }

        /// <summary>
        /// Effects to apply when a stacking application attempt occurs while at the stack limit.
        /// UE5: OverflowEffects.
        /// </summary>
        public IReadOnlyList<GameplayEffect> OverflowEffects { get; }

        /// <summary>
        /// If true, the original effect application (duration refresh, etc.) is denied when overflow occurs.
        /// UE5: bDenyOverflowApplication.
        /// </summary>
        public bool DenyOverflowApplication { get; }

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
            GameplayTagContainer gameplayCues = null,
            bool suppressGameplayCues = false,
            bool removeGameplayEffectsAfterAbilityEnds = false,
            List<ICustomApplicationRequirement> customApplicationRequirements = null,
            bool executePeriodicEffectOnApplication = true,
            List<GameplayEffect> overflowEffects = null,
            bool denyOverflowApplication = false)
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
            SuppressGameplayCues = suppressGameplayCues;
            RemoveGameplayEffectsAfterAbilityEnds = removeGameplayEffectsAfterAbilityEnds;
            CustomApplicationRequirements = customApplicationRequirements ?? (IReadOnlyList<ICustomApplicationRequirement>)System.Array.Empty<ICustomApplicationRequirement>();
            ExecutePeriodicEffectOnApplication = executePeriodicEffectOnApplication;
            OverflowEffects = overflowEffects ?? (IReadOnlyList<GameplayEffect>)System.Array.Empty<GameplayEffect>();
            DenyOverflowApplication = denyOverflowApplication;

            if (DurationPolicy == EDurationPolicy.HasDuration && duration <= 0 && duration != GameplayEffectConstants.INFINITE_DURATION)
            {
                GASLog.Warning($"GameplayEffect '{name}' has 'HasDuration' policy but an invalid duration of {duration}.");
            }
        }
    }
}