using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Dispatches gameplay cues through the runtime context's local cue manager.
    /// </summary>
    public sealed class GameplayCueDispatcher
    {
        private readonly GASRuntimeContext runtimeContext;

        public GameplayCueDispatcher(GASRuntimeContext runtimeContext)
        {
            this.runtimeContext = runtimeContext ?? throw new System.ArgumentNullException(nameof(runtimeContext));
        }

        public void DispatchGameplayCues(GameplayEffectSpec spec, EGameplayCueEvent eventType)
        {
            if (spec == null || spec.Def == null || spec.Def.SuppressGameplayCues || spec.Def.GameplayCues.IsEmpty)
            {
                return;
            }

            if (spec.Target != null && spec.Target.SuppressLocalGameplayCueDispatch)
            {
                return;
            }

            var parameters = new GameplayCueParameters(spec);
            // A GameplayTagContainer also stores implicit parent tags for matching. Only authored
            // cue entries are dispatch events; GameplayCueManager owns hierarchical lookup.
            foreach (var cueTag in spec.Def.GameplayCues.GetExplicitTags())
            {
                DispatchCueTag(spec, cueTag, eventType, parameters);
            }
        }

        private void DispatchCueTag(
            GameplayEffectSpec spec,
            GameplayTag cueTag,
            EGameplayCueEvent eventType,
            GameplayCueParameters parameters)
        {
            if (cueTag.IsNone)
            {
                return;
            }

            var coreParameters = new GameplayCueEventParams(
                parameters.Source,
                parameters.Target,
                parameters.EffectDefinition,
                parameters.SourceObject,
                parameters.TargetObject,
                parameters.EffectLevel,
                parameters.EffectDurationRaw,
                parameters.PredictionKey);
            runtimeContext.CueManager.HandleCue(spec.Target, cueTag, eventType, coreParameters);
            IncrementPredictionCueCount(spec);
        }

        private static void IncrementPredictionCueCount(GameplayEffectSpec spec)
        {
            if (spec.Context != null && spec.Context.PredictionKey.IsValid && spec.Target != null)
            {
                spec.Target.IncrementPredictionWindowGameplayCueCount(spec.Context.PredictionKey);
            }
        }
    }
}
