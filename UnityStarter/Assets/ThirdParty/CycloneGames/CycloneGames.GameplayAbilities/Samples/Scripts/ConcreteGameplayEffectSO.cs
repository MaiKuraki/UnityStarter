using System.Collections.Generic;
using CycloneGames.GameplayAbilities.Runtime;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Sample
{
    /// <summary>
    /// A concrete, creatable ScriptableObject definition for a GameplayEffect.
    /// Use this to create new Gameplay Effect assets in the editor via 'Assets/Create/...'.
    /// </summary>
    [CreateAssetMenu(fileName = "GE_", menuName = "CycloneGames/GameplayAbilitySystem/Samples/GameplayEffect Definition")]
    public class ConcreteGameplayEffectSO : GameplayEffectSO
    {
        /// <summary>
        /// Creates a runtime instance of the GameplayEffect based on the data defined in this ScriptableObject.
        /// </summary>
        public override GameplayEffect CreateGameplayEffect()
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

            return new GameplayEffect(
                EffectName,
                DurationPolicy,
                Duration,
                Period,
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