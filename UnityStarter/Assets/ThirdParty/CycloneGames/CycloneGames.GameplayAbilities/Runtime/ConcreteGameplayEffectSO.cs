using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// A concrete, creatable ScriptableObject definition for a GameplayEffect.
    /// Use this to create new Gameplay Effect assets in the editor via 'Assets/Create/...'.
    /// </summary>
    [CreateAssetMenu(fileName = "GE_", menuName = GameplayAbilitiesAssetMenuPaths.GameplayEffectDefinition)]
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
                    if (abilitySO != null) grantedAbilities.Add(abilitySO.GetGameplayAbility());
                }
            }

            var runtimeModifiers = new List<ModifierInfo>();
            if (SerializableModifiers != null)
            {
                foreach (var serializableMod in SerializableModifiers)
                {
                    runtimeModifiers.Add(CreateRuntimeModifier(serializableMod));
                }
            }

            GameplayEffectExecutionCalculation runtimeExecution = null;

            if (ExecutionDefinition != null)
            {
                runtimeExecution = ExecutionDefinition.CreateExecution();
            }

            var runtimeOverflowEffects = new List<GameplayEffect>();
            if (OverflowEffects != null)
            {
                foreach (var overflowSO in OverflowEffects)
                {
                    if (overflowSO != null) runtimeOverflowEffects.Add(overflowSO.GetGameplayEffect());
                }
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
                GameplayCues,
                SuppressGameplayCues,
                RemoveGameplayEffectsAfterAbilityEnds,
                customApplicationRequirements: null,
                executePeriodicEffectOnApplication: ExecutePeriodicEffectOnApplication,
                overflowEffects: runtimeOverflowEffects,
                denyOverflowApplication: DenyOverflowApplication
            );
        }

        private static ModifierInfo CreateRuntimeModifier(ModifierInfoSerializable serializableMod)
        {
            switch (serializableMod.MagnitudeCalculationType)
            {
                case EGameplayEffectMagnitudeCalculation.AttributeBased:
                    return new ModifierInfo(
                        serializableMod.AttributeName,
                        serializableMod.Operation,
                        new AttributeBasedMagnitude(
                            serializableMod.BackingAttributeName,
                            serializableMod.AttributeCaptureSource,
                            serializableMod.AttributeCalculationType,
                            serializableMod.AttributeCoefficient,
                            serializableMod.AttributePreMultiplyAdditiveValue,
                            serializableMod.AttributePostMultiplyAdditiveValue,
                            serializableMod.AttributeSnapshotPolicy),
                        serializableMod.EvaluationChannel);
                case EGameplayEffectMagnitudeCalculation.SetByCaller:
                    return new ModifierInfo(
                        serializableMod.AttributeName,
                        serializableMod.Operation,
                        !serializableMod.SetByCallerDataTag.IsNone
                            ? new SetByCallerMagnitude(
                                serializableMod.SetByCallerDataTag,
                                serializableMod.SetByCallerDefaultValue,
                                serializableMod.WarnIfSetByCallerMissing)
                            : new SetByCallerMagnitude(
                                serializableMod.SetByCallerDataName,
                                serializableMod.SetByCallerDefaultValue,
                                serializableMod.WarnIfSetByCallerMissing),
                        serializableMod.EvaluationChannel);
                case EGameplayEffectMagnitudeCalculation.CustomCalculation:
                    GASLog.Warning(sb => sb.Append("Modifier on effect asset uses CustomCalculation magnitude, ")
                        .Append("but ScriptableObject modifiers cannot serialize custom calculation instances. ")
                        .Append("Falling back to ScalableFloat for attribute '")
                        .Append(serializableMod.AttributeName).Append("'."));
                    break;
            }

            return new ModifierInfo(
                serializableMod.AttributeName,
                serializableMod.Operation,
                serializableMod.Magnitude,
                serializableMod.EvaluationChannel);
        }
    }
}
