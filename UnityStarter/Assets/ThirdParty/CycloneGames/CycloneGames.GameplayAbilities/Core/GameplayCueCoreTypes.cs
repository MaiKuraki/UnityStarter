using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Trigger timing of a GameplayCue event relative to its parent effect.
    /// 
    /// OnActive: fires once when a duration/infinite effect is first applied (buff glow, status icon).
    /// WhileActive: fires every frame or periodic tick while the effect remains active (looping particles).
    /// Removed: fires when the effect expires or is manually removed (fade-out VFX).
    /// Executed: fires for instant effects or each periodic tick (one-shot impact VFX, hit sound).
    /// </summary>
    public enum EGameplayCueEvent
    {
        OnActive,
        WhileActive,
        Removed,
        Executed
    }

    /// <summary>
    /// Parameters passed to GameplayCue handlers. Uses object types to avoid Unity dependencies.
    /// </summary>
    public readonly struct GameplayCueEventParams
    {
        public readonly object Source;
        public readonly object Target;
        public readonly object EffectDefinition;
        public readonly object EffectContext;
        public readonly object SourceObject;
        public readonly object TargetObject;
        public readonly int EffectLevel;
        public readonly float EffectDuration;
        public readonly GASPredictionKey PredictionKey;

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object effectContext,
            object sourceObject,
            object targetObject,
            int effectLevel,
            float effectDuration)
            : this(source, target, effectDefinition, effectContext, sourceObject, targetObject, effectLevel, effectDuration, default)
        {
        }

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object effectContext,
            object sourceObject,
            object targetObject,
            int effectLevel,
            float effectDuration,
            GASPredictionKey predictionKey)
        {
            Source = source;
            Target = target;
            EffectDefinition = effectDefinition;
            EffectContext = effectContext;
            SourceObject = sourceObject;
            TargetObject = targetObject;
            EffectLevel = effectLevel;
            EffectDuration = effectDuration;
            PredictionKey = predictionKey;
        }
    }

    /// <summary>
    /// Interface for GameplayCue management. Allows DI injection and server-side mocking.
    /// </summary>
    public interface IGameplayCueManager
    {
        void RegisterStaticCue(GameplayTag cueTag, string assetAddress);
        void HandleCue(object asc, GameplayTag cueTag, EGameplayCueEvent eventType, GameplayCueEventParams parameters);
        void RemoveAllCuesFor(object asc);
        void Initialize(object assetPackage);
    }
}
