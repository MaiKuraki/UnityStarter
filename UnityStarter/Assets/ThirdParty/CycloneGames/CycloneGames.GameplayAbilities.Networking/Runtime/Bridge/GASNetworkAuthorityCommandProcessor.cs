using System;
using System.Threading;
using CycloneGames.GameplayAbilities.Core;
using CycloneGames.GameplayAbilities.Runtime;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Executes commands only after the caller has authenticated the peer and passed product-owned
    /// entity ownership, permission, and rate gates. This processor owns replay protection and exact
    /// entity/grant resolution; it is not an authorization boundary.
    /// </summary>
    /// <remarks>
    /// No thread or lock is created. A backend must marshal authenticated commands to the GAS owner
    /// thread; this is also the direct execution path used on WebGL.
    /// </remarks>
    public sealed class GASNetworkAuthorityCommandProcessor
    {
        private readonly AbilitySystemComponent abilitySystem;
        private readonly IGASNetworkEntityResolver entityResolver;
        private readonly GASAuthorityIdentityMap identityMap;
        private readonly GASNetworkStateVersion stateVersion;
        private readonly IGASNetworkTargetCommandHandler targetHandler;
        private readonly GASAuthorityCommandReplayWindow replayWindow;
        private readonly int ownerThreadId;
        private ulong lastKnownWireStateVersion;

        public GASNetworkAuthorityCommandProcessor(
            AbilitySystemComponent abilitySystem,
            IGASNetworkEntityResolver entityResolver,
            GASAuthorityIdentityMap identityMap,
            GASNetworkStateVersion stateVersion,
            IGASNetworkTargetCommandHandler targetHandler = null,
            int replayCapacity = GASAuthorityCommandReplayWindow.DefaultCapacity)
        {
            this.abilitySystem = abilitySystem ?? throw new ArgumentNullException(nameof(abilitySystem));
            this.entityResolver = entityResolver ?? throw new ArgumentNullException(nameof(entityResolver));
            this.identityMap = identityMap ?? throw new ArgumentNullException(nameof(identityMap));
            this.stateVersion = stateVersion ?? throw new ArgumentNullException(nameof(stateVersion));
            this.targetHandler = targetHandler;
            if (!identityMap.IsOwnerThread)
                throw new InvalidOperationException("The authority identity map must be owned by the processor thread.");
            if (!stateVersion.IsOwnerThread)
                throw new InvalidOperationException("The network state version must be owned by the processor thread.");
            if (!ReferenceEquals(stateVersion.AbilitySystem, abilitySystem))
            {
                throw new ArgumentException(
                    "The network state version must own the supplied AbilitySystemComponent.",
                    nameof(stateVersion));
            }
            if (stateVersion.StreamEpoch != identityMap.StreamEpoch)
            {
                throw new ArgumentException(
                    "The network state version and authority identity map must use the same stream epoch.",
                    nameof(stateVersion));
            }
            if (!entityResolver.TryResolveAbilitySystem(
                    identityMap.Entity,
                    out AbilitySystemComponent resolved) ||
                !ReferenceEquals(resolved, abilitySystem))
            {
                throw new ArgumentException(
                    "The identity map entity must resolve to the supplied AbilitySystemComponent.",
                    nameof(identityMap));
            }

            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            replayWindow = new GASAuthorityCommandReplayWindow(
                identityMap.StreamEpoch,
                replayCapacity);
            if (!stateVersion.TryObserveCurrentState(out lastKnownWireStateVersion))
            {
                throw new InvalidOperationException(
                    "The GAS network state version cannot observe the current authority state.");
            }
        }

        public int OwnerThreadId => ownerThreadId;
        public uint StreamEpoch => replayWindow.StreamEpoch;
        public uint HighestCompletedSequence => replayWindow.HighestCompletedSequence;
        /// <summary>
        /// True after command execution crossed an unexpected failure boundary. The owner must
        /// publish canonical state as needed and start a new authenticated stream epoch before
        /// accepting more commands.
        /// </summary>
        public bool RequiresStreamReset { get; private set; }

        /// <summary>
        /// Runs the exactly-once gate and, for a new canonical command, executes one terminal GAS
        /// action. Execute and Duplicate decisions return a valid terminal result; other decisions
        /// leave <paramref name="result"/> at its default value.
        /// </summary>
        public GASCommandReplayDecision Process(
            in GASAbilityCommand command,
            ReadOnlySpan<GASNetworkEntityId> actorTargets,
            out GASCommandResult result)
        {
            AssertOwnerThread();
            result = default;
            ulong fingerprint = GASNetworkWireCodec.ComputeAbilityCommandFingerprint(
                in command,
                actorTargets);
            GASCommandReplayDecision decision = replayWindow.Evaluate(
                in command,
                fingerprint,
                out GASCommandResult cachedResult);
            if (decision == GASCommandReplayDecision.Duplicate)
            {
                result = cachedResult;
                return decision;
            }
            if (decision != GASCommandReplayDecision.Execute)
                return decision;

            GASCommandStatus status = GASCommandStatus.AuthorityUnavailable;
            bool versionEpochMatches =
                identityMap.StreamEpoch == replayWindow.StreamEpoch &&
                stateVersion.StreamEpoch == replayWindow.StreamEpoch;
            if (!versionEpochMatches)
                RequiresStreamReset = true;

            if (!RequiresStreamReset)
            {
                try
                {
                    status = ExecuteNewCommand(in command, actorTargets);
                }
                catch (Exception)
                {
                    RequiresStreamReset = true;
                }

                if (!abilitySystem.IsDisposed)
                {
                    if (stateVersion.TryObserveCurrentState(out ulong currentWireStateVersion))
                    {
                        lastKnownWireStateVersion = currentWireStateVersion;
                    }
                    else
                    {
                        status = GASCommandStatus.AuthorityUnavailable;
                        RequiresStreamReset = true;
                    }
                }
            }
            result = new GASCommandResult(
                command.StreamEpoch,
                command.CommandSequence,
                command.Entity,
                command.Grant,
                command.Kind,
                status,
                lastKnownWireStateVersion);
            replayWindow.Complete(in result);
            return decision;
        }

        public void ResetEpoch(uint newStreamEpoch)
        {
            AssertOwnerThread();
            if (identityMap.StreamEpoch != newStreamEpoch)
            {
                throw new InvalidOperationException(
                    "Reset the authority identity map to the new stream epoch before resetting command replay.");
            }
            if (stateVersion.StreamEpoch != newStreamEpoch)
            {
                throw new InvalidOperationException(
                    "Reset the shared GASNetworkStateVersion to the new epoch before resetting command replay.");
            }
            if (!stateVersion.TryObserveCurrentState(out ulong currentWireStateVersion))
            {
                throw new InvalidOperationException(
                    "The GAS network state version cannot observe the current authority state.");
            }
            replayWindow.Reset(newStreamEpoch);
            lastKnownWireStateVersion = currentWireStateVersion;
            RequiresStreamReset = false;
        }

        private GASCommandStatus ExecuteNewCommand(
            in GASAbilityCommand command,
            ReadOnlySpan<GASNetworkEntityId> actorTargets)
        {
            if (!entityResolver.TryResolveAbilitySystem(
                    command.Entity,
                    out AbilitySystemComponent resolved) ||
                !ReferenceEquals(resolved, abilitySystem) ||
                resolved.IsDisposed)
            {
                return GASCommandStatus.EntityUnavailable;
            }

            if (!identityMap.TryGetAbilitySpecHandle(command.Grant, out int specHandle) ||
                !abilitySystem.TryGetAbilitySpecByHandle(specHandle, out GameplayAbilitySpec spec))
            {
                return GASCommandStatus.GrantUnavailable;
            }

            switch (command.Kind)
            {
                case GASAbilityCommandKind.Activate:
                {
                    var correlationKey = new GASPredictionKey(
                        (int)command.CommandSequence,
                        abilitySystem.CoreEntity,
                        (int)command.CommandSequence);
                    GASAuthorityActivationResult activation =
                        abilitySystem.TryExecuteAuthorityAbility(spec, correlationKey);
                    return MapActivationStatus(activation.Status);
                }
                case GASAbilityCommandKind.Cancel:
                    return abilitySystem.TryCancelAbility(spec)
                        ? GASCommandStatus.Accepted
                        : GASCommandStatus.Rejected;
                case GASAbilityCommandKind.InputPressed:
                    return abilitySystem.TrySetAbilityInputPressed(spec, true)
                        ? GASCommandStatus.Accepted
                        : GASCommandStatus.Rejected;
                case GASAbilityCommandKind.InputReleased:
                    return abilitySystem.TrySetAbilityInputPressed(spec, false)
                        ? GASCommandStatus.Accepted
                        : GASCommandStatus.Rejected;
                case GASAbilityCommandKind.ConfirmTarget:
                case GASAbilityCommandKind.CancelTarget:
                    return ExecuteTargetCommand(spec, in command, actorTargets);
                default:
                    return GASCommandStatus.Rejected;
            }
        }

        private GASCommandStatus ExecuteTargetCommand(
            GameplayAbilitySpec spec,
            in GASAbilityCommand command,
            ReadOnlySpan<GASNetworkEntityId> actorTargets)
        {
            if (targetHandler == null)
                return GASCommandStatus.AuthorityUnavailable;

            GASCommandStatus status = targetHandler.HandleTargetCommand(
                abilitySystem,
                spec,
                in command,
                actorTargets);
            return status >= GASCommandStatus.Accepted &&
                   status <= GASCommandStatus.AuthorityUnavailable
                ? status
                : GASCommandStatus.InvalidTargetData;
        }

        private static GASCommandStatus MapActivationStatus(GASAuthorityActivationStatus status)
        {
            switch (status)
            {
                case GASAuthorityActivationStatus.Activated:
                    return GASCommandStatus.Accepted;
                case GASAuthorityActivationStatus.MissingOrStaleGrant:
                    return GASCommandStatus.GrantUnavailable;
                case GASAuthorityActivationStatus.RuntimeUnavailable:
                    return GASCommandStatus.AuthorityUnavailable;
                case GASAuthorityActivationStatus.WrongExecutionPolicy:
                case GASAuthorityActivationStatus.AbilityRejected:
                default:
                    return GASCommandStatus.Rejected;
            }
        }

        private void AssertOwnerThread()
        {
            int currentThreadId = Thread.CurrentThread.ManagedThreadId;
            if (currentThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"GAS authority command processor is owned by thread {ownerThreadId} and cannot be accessed from thread {currentThreadId}.");
            }
        }
    }
}
