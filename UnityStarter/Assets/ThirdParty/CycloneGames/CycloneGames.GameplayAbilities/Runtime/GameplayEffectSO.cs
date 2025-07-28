using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Abstract base class for a Gameplay Effect definition, designed to be created as a ScriptableObject asset.
    /// This class holds all the design-time data that defines what an effect does, from modifying attributes to applying tags and granting abilities.
    /// </summary>
    public abstract class GameplayEffectSO : ScriptableObject
    {
        [Tooltip("The unique name for this effect, used primarily for debugging and logging purposes.")]
        public string EffectName;

        [Tooltip("Defines the lifetime policy of the effect.\n- Instant: Applies immediately and does not persist (e.g., damage, cost).\n- HasDuration: Persists for a set time (e.g., buffs, debuffs).\n- Infinite: Persists until explicitly removed (e.g., passive auras).")]
        public EDurationPolicy DurationPolicy;

        [Tooltip("The total duration of the effect in seconds. This is only used if DurationPolicy is 'HasDuration'. Use -1 for an infinite duration.")]
        public float Duration;

        [Tooltip("The interval in seconds for periodic effects (like Damage over Time). Set to 0 or less to disable. Only applicable for 'HasDuration' or 'Infinite' effects.")]
        public float Period;

        [Tooltip("A list of attribute modifications this effect applies. This is the primary mechanism for predictable attribute changes.")]
        public List<ModifierInfoSerializable> SerializableModifiers;

        [Tooltip("(Advanced) A custom calculation class for complex, non-predictable logic, such as a final damage formula. This only executes for 'Instant' effects or for each tick of a periodic effect.")]
        public GameplayEffectExecutionCalculationSO ExecutionDefinition;

        [Tooltip("Defines how this effect interacts with other instances of the same effect on a target, including stacking rules and limits.")]
        public GameplayEffectStacking Stacking;

        [Tooltip("A list of abilities to temporarily grant to the target while this effect is active. Only applicable for 'HasDuration' or 'Infinite' effects.")]
        public List<GameplayAbilitySO> GrantedAbilities;

        [Header("Tag Relationships")]
        [Tooltip("Tags that DESCRIBE the effect itself (e.g., 'Damage.Type.Fire'). These are NOT granted to the target but are used to identify the effect for removal or other gameplay logic.")]
        public GameplayTagContainer AssetTags;

        [Tooltip("Tags that are GRANTED to the target while this effect is active. This is the primary way to apply temporary states like 'State.Stunned', 'Debuff.Burning', or 'Cooldown.Skill.Fireball'.")]
        public GameplayTagContainer GrantedTags;

        [Tooltip("The target MUST have all 'Required' tags and NONE of the 'Forbidden' tags for this effect to be successfully applied.")]
        public GameplayTagRequirements ApplicationTagRequirements;

        [Tooltip("Once applied, the effect will only be active (i.e., its modifiers will apply) as long as the target continues to meet these tag requirements.")]
        public GameplayTagRequirements OngoingTagRequirements;

        [Tooltip("Upon successful application, this effect will remove any other active effects on the target that have one of the specified tags (checks both their AssetTags and GrantedTags). Useful for dispel mechanics.")]
        public GameplayTagContainer RemoveGameplayEffectsWithTags;

        [Header("Cosmetics")]
        [Tooltip("A list of GameplayCue tags (e.g., 'GameplayCue.VFX.Fireball.Impact') to trigger for cosmetic effects like particles and sounds when the effect is applied, executed, or removed.")]
        public GameplayTagContainer GameplayCues;

        /// <summary>
        /// Creates a runtime, stateless instance of the GameplayEffect based on the data configured in this ScriptableObject.
        /// </summary>
        /// <returns>A new <see cref="GameplayEffect"/> instance.</returns>
        public abstract GameplayEffect CreateGameplayEffect();
    }
}