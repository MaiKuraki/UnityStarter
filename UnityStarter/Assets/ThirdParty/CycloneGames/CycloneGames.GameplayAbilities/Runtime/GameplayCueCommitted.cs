using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayTags.Core;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Immutable, post-commit description of one Gameplay Cue emitted by an AbilitySystemComponent.
    /// The payload contains no presentation objects and is valid only for the synchronous callback.
    /// </summary>
    public readonly struct GameplayCueCommitted
    {
        internal GameplayCueCommitted(
            GameplayTag cue,
            EGameplayCueEvent cueEvent,
            AbilitySystemComponent source,
            AbilitySystemComponent target,
            GameplayEffect effectDefinition,
            int effectLevel,
            long effectDurationRaw,
            GASPredictionKey predictionKey,
            int activeEffectReconciliationId,
            int sourceAbilitySpecHandle,
            EAbilityExecutionPolicy sourceAbilityExecutionPolicy,
            ulong stateVersion)
        {
            Cue = cue;
            Event = cueEvent;
            Source = source;
            Target = target;
            EffectDefinition = effectDefinition;
            EffectLevel = effectLevel;
            EffectDurationRaw = effectDurationRaw;
            PredictionKey = predictionKey;
            ActiveEffectReconciliationId = activeEffectReconciliationId;
            SourceAbilitySpecHandle = sourceAbilitySpecHandle;
            SourceAbilityExecutionPolicy = sourceAbilityExecutionPolicy;
            StateVersion = stateVersion;
        }

        public GameplayTag Cue { get; }
        public EGameplayCueEvent Event { get; }
        public AbilitySystemComponent Source { get; }
        public AbilitySystemComponent Target { get; }
        public GameplayEffect EffectDefinition { get; }
        public int EffectLevel { get; }
        public long EffectDurationRaw { get; }
        public GASPredictionKey PredictionKey { get; }

        /// <summary>
        /// Process-local active-effect identity for OnActive, Removed, and periodic Executed cues.
        /// Instant Executed cues use zero.
        /// </summary>
        public int ActiveEffectReconciliationId { get; }

        /// <summary>Process-local source ability-spec handle, or zero when no live source grant exists.</summary>
        public int SourceAbilitySpecHandle { get; }

        public EAbilityExecutionPolicy SourceAbilityExecutionPolicy { get; }

        /// <summary>The target ASC state version that contains the committed mutation.</summary>
        public ulong StateVersion { get; }
    }

    /// <summary>Synchronous owner-thread observer for committed Gameplay Cues.</summary>
    public delegate void GameplayCueCommittedDelegate(in GameplayCueCommitted cue);
}
