using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Trigger timing of a GameplayCue event relative to its parent effect.
    /// 
    /// OnActive: fires when a duration/infinite cue is first witnessed as applied.
    /// WhileActive: initializes presentation first observed as active, including join-in-progress state.
    /// A normal OnActive dispatch invokes both presentation phases in order; WhileActive is not a per-frame tick.
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
        public readonly object SourceObject;
        public readonly object TargetObject;
        public readonly int EffectLevel;
        public readonly long EffectDurationRaw;
        public GASFixedValue EffectDuration => GASFixedValue.FromRaw(EffectDurationRaw);
        public readonly GASPredictionKey PredictionKey;

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object sourceObject,
            object targetObject,
            int effectLevel,
            long effectDurationRaw)
            : this(source, target, effectDefinition, sourceObject, targetObject, effectLevel, effectDurationRaw, default)
        {
        }

        public GameplayCueEventParams(
            object source,
            object target,
            object effectDefinition,
            object sourceObject,
            object targetObject,
            int effectLevel,
            long effectDurationRaw,
            GASPredictionKey predictionKey)
        {
            Source = source;
            Target = target;
            EffectDefinition = effectDefinition;
            SourceObject = sourceObject;
            TargetObject = targetObject;
            EffectLevel = effectLevel;
            EffectDurationRaw = effectDurationRaw;
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
        void CommitPredictedCues(object asc, GASPredictionKey predictionKey);
        void RollbackPredictedCues(object asc, GASPredictionKey predictionKey);
    }
}
