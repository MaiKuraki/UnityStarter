using Cysharp.Threading.Tasks;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Dispatches gameplay cues locally and through the configured GAS network bridge.
    /// </summary>
    public sealed class GameplayCueDispatcher
    {
        public static readonly GameplayCueDispatcher Default = new GameplayCueDispatcher();

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
            foreach (var cueTag in spec.Def.GameplayCues)
            {
                DispatchCueTag(spec, cueTag, eventType, parameters);
            }
        }

        private static void DispatchCueTag(
            GameplayEffectSpec spec,
            GameplayTag cueTag,
            EGameplayCueEvent eventType,
            GameplayCueParameters parameters)
        {
            if (cueTag.IsNone)
            {
                return;
            }

            GameplayCueManager.Default.HandleCue(cueTag, eventType, parameters).Forget();
            IncrementPredictionCueCount(spec);

            if (eventType == EGameplayCueEvent.OnActive)
            {
                GameplayCueManager.Default.HandleCue(cueTag, EGameplayCueEvent.WhileActive, parameters).Forget();
                IncrementPredictionCueCount(spec);
            }

            var bridge = GASServices.NetworkBridge;
            if (!bridge.IsServer)
            {
                return;
            }

            var resolver = GASServices.ReplicationResolver;
            var cueParams = new GASCueNetParams(
                sourceAscNetId: spec.Source != null ? resolver.GetAbilitySystemNetworkId(spec.Source) : 0,
                targetAscNetId: spec.Target != null ? resolver.GetAbilitySystemNetworkId(spec.Target) : 0,
                magnitudeRaw: 0L,
                normalizedMagnitudeRaw: 0L,
                predictionKey: spec.Context?.PredictionKey ?? default);
            bridge.ServerBroadcastGameplayCue(spec.Target, cueTag, eventType, cueParams);
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
