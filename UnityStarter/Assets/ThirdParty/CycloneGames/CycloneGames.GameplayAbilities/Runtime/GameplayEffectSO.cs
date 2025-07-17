using System.Collections.Generic;
using CycloneGames.GameplayTags.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    [CreateAssetMenu(fileName = "GE_", menuName = "CycloneGames/GameplayAbilitySystem/GameplayEffect")]
    public class GameplayEffectSO : ScriptableObject
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
        public CycloneGames.GameplayAbilities.Runtime.GameplayTagRequirements ApplicationTagRequirements;
        public CycloneGames.GameplayAbilities.Runtime.GameplayTagRequirements OngoingTagRequirements;
        public GameplayTagContainer RemoveGameplayEffectsWithTags;
        public GameplayTagContainer GameplayCues;

        public GameplayEffect CreateGameplayEffect()
        {
            var grantedAbilities = new List<GameplayAbility>();
            if (GrantedAbilities != null)
            {
                foreach (var abilitySO in GrantedAbilities)
                {
                    grantedAbilities.Add(abilitySO.CreateAbility());
                }
            }

            // Convert the serializable modifier data into runtime ModifierInfo instances.
            var runtimeModifiers = new List<ModifierInfo>();
            if (SerializableModifiers != null)
            {
                foreach (var serializableMod in SerializableModifiers)
                {
                    runtimeModifiers.Add(new ModifierInfo(serializableMod.AttributeName, serializableMod.Operation, serializableMod.Magnitude));
                }
            }

            return new GameplayEffect(
                EffectName,
                DurationPolicy,
                Duration,
                runtimeModifiers,
                Execution,
                Stacking,
                grantedAbilities,
                AssetTags,
                GrantedTags,
                ApplicationTagRequirements,
                OngoingTagRequirements,
                RemoveGameplayEffectsWithTags,
                GameplayCues
            );
        }
    }
}