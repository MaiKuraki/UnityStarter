using System;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>Resolves the product-owned identity of a live AbilitySystemComponent in both directions.</summary>
    public interface IGASNetworkEntityResolver
    {
        bool TryGetNetworkEntityId(
            AbilitySystemComponent abilitySystem,
            out GASNetworkEntityId entity);

        bool TryResolveAbilitySystem(
            GASNetworkEntityId entity,
            out AbilitySystemComponent abilitySystem);
    }

    /// <summary>
    /// Resolves grant identities across AbilitySystemComponents without exposing process-local handles
    /// on the wire. Implementations are normally backed by the per-entity identity maps owned by the
    /// product composition root.
    /// </summary>
    public interface IGASNetworkGrantResolver
    {
        bool TryGetNetworkGrantId(
            AbilitySystemComponent abilitySystem,
            int abilitySpecHandle,
            out uint streamEpoch,
            out GASNetworkGrantId grant);

        bool TryResolveAbilitySpecHandle(
            GASNetworkEntityId entity,
            uint streamEpoch,
            GASNetworkGrantId grant,
            out int abilitySpecHandle);
    }

    /// <summary>Explicit fixed memory budget for one runtime state bridge.</summary>
    public readonly struct GASNetworkRuntimeStateCapacity
    {
        public static readonly GASNetworkRuntimeStateCapacity Default =
            new GASNetworkRuntimeStateCapacity(
                GASNetworkStateCapacity.Default,
                maxSetByCallerTagsPerEffect: 16,
                maxSetByCallerNamesPerEffect: 16,
                maxDynamicGrantedTagsPerEffect: 16,
                maxDynamicAssetTagsPerEffect: 16);

        public GASNetworkRuntimeStateCapacity(
            GASNetworkStateCapacity state,
            int maxSetByCallerTagsPerEffect,
            int maxSetByCallerNamesPerEffect,
            int maxDynamicGrantedTagsPerEffect,
            int maxDynamicAssetTagsPerEffect)
        {
            ValidateChildCapacity(
                maxSetByCallerTagsPerEffect,
                state.EffectMagnitudes,
                nameof(maxSetByCallerTagsPerEffect));
            ValidateChildCapacity(
                maxSetByCallerNamesPerEffect,
                state.EffectMagnitudes,
                nameof(maxSetByCallerNamesPerEffect));
            ValidateChildCapacity(
                maxDynamicGrantedTagsPerEffect,
                state.EffectTags,
                nameof(maxDynamicGrantedTagsPerEffect));
            ValidateChildCapacity(
                maxDynamicAssetTagsPerEffect,
                state.EffectTags,
                nameof(maxDynamicAssetTagsPerEffect));

            if ((long)maxSetByCallerTagsPerEffect + maxSetByCallerNamesPerEffect >
                state.EffectMagnitudes)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxSetByCallerNamesPerEffect),
                    "Per-effect SetByCaller capacities cannot exceed the full-state magnitude capacity.");
            }

            if ((long)maxDynamicGrantedTagsPerEffect + maxDynamicAssetTagsPerEffect >
                state.EffectTags)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDynamicAssetTagsPerEffect),
                    "Per-effect dynamic tag capacities cannot exceed the full-state effect-tag capacity.");
            }

            State = state;
            MaxSetByCallerTagsPerEffect = maxSetByCallerTagsPerEffect;
            MaxSetByCallerNamesPerEffect = maxSetByCallerNamesPerEffect;
            MaxDynamicGrantedTagsPerEffect = maxDynamicGrantedTagsPerEffect;
            MaxDynamicAssetTagsPerEffect = maxDynamicAssetTagsPerEffect;
        }

        public GASNetworkStateCapacity State { get; }
        public int MaxSetByCallerTagsPerEffect { get; }
        public int MaxSetByCallerNamesPerEffect { get; }
        public int MaxDynamicGrantedTagsPerEffect { get; }
        public int MaxDynamicAssetTagsPerEffect { get; }

        private static void ValidateChildCapacity(int value, int totalCapacity, string parameterName)
        {
            if (value < 0 || value > totalCapacity)
                throw new ArgumentOutOfRangeException(parameterName);
        }
    }

    /// <summary>
    /// Revalidates world-dependent targeting after peer authentication, product ownership,
    /// permission, replay, and rate gates have accepted the command envelope.
    /// </summary>
    public interface IGASNetworkTargetCommandHandler
    {
        GASCommandStatus HandleTargetCommand(
            AbilitySystemComponent abilitySystem,
            GameplayAbilitySpec abilitySpec,
            in GASAbilityCommand command,
            ReadOnlySpan<GASNetworkEntityId> actorTargets);
    }

    /// <summary>
    /// Atomically consumes one complete backend-neutral cue message without discarding spatial data.
    /// Returning <see langword="false"/> means that no externally visible cue work was committed and
    /// allows the owning receiver to retain its expected sequence.
    /// </summary>
    public interface IGASNetworkCueConsumer
    {
        bool TryConsume(in GASCueExecuted cue);
    }
}
