using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Abstract base class for Gameplay Effect definitions.
    /// This defines the data structure for a gameplay effect but relies on a concrete implementation
    /// to be created as a ScriptableObject asset.
    /// </summary>
    public abstract class GameplayEffectSO : ScriptableObject
    {
        public string EffectName;
        public EDurationPolicy DurationPolicy;
        [Tooltip("Only used if DurationPolicy is HasDuration.")]
        public float Duration;
        public List<ModifierInfoSerializable> SerializableModifiers;
        public GameplayEffectExecutionCalculation Execution;
        public GameplayEffectStacking Stacking;
        public List<GameplayAbilitySO> GrantedAbilities;
        public GameplayTagContainer AssetTags;
        public GameplayTagContainer GrantedTags;
        public GameplayTagRequirements ApplicationTagRequirements;
        public GameplayTagRequirements OngoingTagRequirements;
        public GameplayTagContainer RemoveGameplayEffectsWithTags;
        public GameplayTagContainer GameplayCues;

        /// <summary>
        /// Creates a runtime instance of the GameplayEffect.
        /// Concrete implementations of this class will provide the logic.
        /// </summary>
        /// <returns>A new GameplayEffect instance.</returns>
        public abstract GameplayEffect CreateGameplayEffect();
    }
}