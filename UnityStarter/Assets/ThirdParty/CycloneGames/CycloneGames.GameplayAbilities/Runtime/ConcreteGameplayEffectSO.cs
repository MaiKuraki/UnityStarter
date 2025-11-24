using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A concrete, creatable ScriptableObject definition for a GameplayEffect.
    /// Use this to create new Gameplay Effect assets in the editor via 'Assets/Create/...'.
    /// </summary>
    [CreateAssetMenu(fileName = "GE_", menuName = "CycloneGames/GameplayAbilitySystem/GameplayEffect Definition")]
    public class ConcreteGameplayEffectSO : GameplayEffectSO
    {
        /// <summary>
        /// Creates a runtime instance of the GameplayEffect based on the data defined in this ScriptableObject.
        /// </summary>
        protected override GameplayEffect CreateGameplayEffect()
        {
            var grantedAbilities = new List<GameplayAbility>();
            if (GrantedAbilities != null)
            {
                foreach (var abilitySO in GrantedAbilities)
                {
                    if (abilitySO != null) grantedAbilities.Add(abilitySO.CreateAbility());
                }
            }

            var runtimeModifiers = new List<ModifierInfo>();
            if (SerializableModifiers != null)
            {
                foreach (var serializableMod in SerializableModifiers)
                {
                    runtimeModifiers.Add(new ModifierInfo(serializableMod.AttributeName, serializableMod.Operation, serializableMod.Magnitude));
                }
            }

            GameplayEffectExecutionCalculation runtimeExecution = null;

            if (ExecutionDefinition != null)
            {
                runtimeExecution = ExecutionDefinition.CreateExecution();
            }

            return new GameplayEffect(
                EffectName,
                DurationPolicy,
                Duration,
                Period,
                runtimeModifiers,
                runtimeExecution,
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