using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using CycloneGames.Networking.Replication;

namespace CycloneGames.AIPerception.Networking
{
    public enum AIPerceptionNetworkAuthorityRole : byte
    {
        None = 0,
        ServerAuthority = 1,
        AutonomousObserver = 2,
        SimulatedObserver = 3
    }

    public readonly struct AIPerceptionNetworkAuthorityContext
    {
        public readonly bool IsServer;
        public readonly bool IsClient;
        public readonly int LocalConnectionId;
        public readonly uint AuthorityGeneration;

        public AIPerceptionNetworkAuthorityContext(
            bool isServer,
            bool isClient,
            int localConnectionId,
            uint authorityGeneration)
        {
            IsServer = isServer;
            IsClient = isClient;
            LocalConnectionId = localConnectionId;
            AuthorityGeneration = authorityGeneration;
        }

        public bool IsValid => (IsServer || IsClient) && LocalConnectionId >= 0 && AuthorityGeneration != 0u;
    }

    public readonly struct AIPerceptionRemoteSnapshotContext
    {
        public readonly int SenderConnectionId;
        public readonly int AuthoritativeServerConnectionId;
        public readonly bool IsSenderAuthenticated;
        public readonly bool IsServerToClient;
        public readonly uint AuthorityGeneration;
        public readonly bool HasAppliedSnapshot;
        public readonly ushort LastAppliedSequence;
        public readonly int LastAppliedTick;

        public AIPerceptionRemoteSnapshotContext(
            int senderConnectionId,
            int authoritativeServerConnectionId,
            bool isSenderAuthenticated,
            bool isServerToClient,
            uint authorityGeneration,
            bool hasAppliedSnapshot = false,
            ushort lastAppliedSequence = 0,
            int lastAppliedTick = 0)
        {
            SenderConnectionId = senderConnectionId;
            AuthoritativeServerConnectionId = authoritativeServerConnectionId;
            IsSenderAuthenticated = isSenderAuthenticated;
            IsServerToClient = isServerToClient;
            AuthorityGeneration = authorityGeneration;
            HasAppliedSnapshot = hasAppliedSnapshot;
            LastAppliedSequence = lastAppliedSequence;
            LastAppliedTick = lastAppliedTick;
        }

        public bool IsValid =>
            SenderConnectionId >= 0 &&
            AuthoritativeServerConnectionId >= 0 &&
            AuthorityGeneration != 0u &&
            (!HasAppliedSnapshot || (LastAppliedSequence != 0 && LastAppliedTick >= 0));
    }

    public enum AIPerceptionRemoteSnapshotResult : byte
    {
        Invalid = 0,
        Allowed = 1,
        InvalidLocalContext = 2,
        InvalidObserver = 3,
        InvalidInboundContext = 4,
        UnauthenticatedSender = 5,
        InvalidDirection = 6,
        SenderIsNotAuthority = 7,
        AuthorityGenerationMismatch = 8,
        MalformedSnapshot = 9,
        StaleTick = 10,
        ReplayedOrOutOfOrderSequence = 11
    }

    public readonly struct NetworkedAIPerceptionObserver
    {
        public readonly uint ObserverNetworkId;
        public readonly int OwnerConnectionId;
        public readonly ulong OwnerPlayerId;
        public readonly int TeamId;
        public readonly uint InterestLayerMask;
        public readonly bool AlwaysRelevant;
        public readonly NetworkVector3 InterestPosition;
        public readonly uint AuthorityGeneration;

        public NetworkedAIPerceptionObserver(
            uint observerNetworkId,
            int ownerConnectionId,
            ulong ownerPlayerId,
            int teamId,
            uint interestLayerMask,
            bool alwaysRelevant,
            NetworkVector3 interestPosition,
            uint authorityGeneration)
        {
            ObserverNetworkId = observerNetworkId;
            OwnerConnectionId = ownerConnectionId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            InterestLayerMask = interestLayerMask;
            AlwaysRelevant = alwaysRelevant;
            InterestPosition = interestPosition;
            AuthorityGeneration = authorityGeneration;
        }

        public bool IsValid =>
            ObserverNetworkId != 0u &&
            OwnerConnectionId >= 0 &&
            AuthorityGeneration != 0u &&
            InterestPosition.IsFinite();
    }

    public interface IAIPerceptionNetworkAuthorityResolver
    {
        AIPerceptionNetworkAuthorityRole GetRole(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer);

        bool CanProduceAuthoritativePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer);

        AIPerceptionRemoteSnapshotResult ValidateRemotePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in AIPerceptionRemoteSnapshotContext inbound,
            in NetworkedAIPerceptionObserver observer,
            in AIPerceptionDetectionSnapshotMessage snapshot,
            ReadOnlySpan<AIPerceptionDetectionEntry> entries);
    }

    public sealed class ServerAuthoritativeAIPerceptionAuthorityResolver : IAIPerceptionNetworkAuthorityResolver
    {
        public AIPerceptionNetworkAuthorityRole GetRole(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer)
        {
            if (!context.IsValid || !observer.IsValid ||
                context.AuthorityGeneration != observer.AuthorityGeneration)
            {
                return AIPerceptionNetworkAuthorityRole.None;
            }

            if (context.IsServer)
            {
                return AIPerceptionNetworkAuthorityRole.ServerAuthority;
            }

            return context.IsClient && context.LocalConnectionId == observer.OwnerConnectionId
                ? AIPerceptionNetworkAuthorityRole.AutonomousObserver
                : AIPerceptionNetworkAuthorityRole.SimulatedObserver;
        }

        public bool CanProduceAuthoritativePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer)
        {
            return context.IsValid && context.IsServer && observer.IsValid &&
                   context.AuthorityGeneration == observer.AuthorityGeneration;
        }

        public AIPerceptionRemoteSnapshotResult ValidateRemotePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in AIPerceptionRemoteSnapshotContext inbound,
            in NetworkedAIPerceptionObserver observer,
            in AIPerceptionDetectionSnapshotMessage snapshot,
            ReadOnlySpan<AIPerceptionDetectionEntry> entries)
        {
            if (!context.IsValid || !context.IsClient || context.IsServer)
            {
                return AIPerceptionRemoteSnapshotResult.InvalidLocalContext;
            }

            if (!observer.IsValid || snapshot.ObserverNetworkId != observer.ObserverNetworkId)
            {
                return AIPerceptionRemoteSnapshotResult.InvalidObserver;
            }

            if (!inbound.IsValid)
            {
                return AIPerceptionRemoteSnapshotResult.InvalidInboundContext;
            }

            if (!inbound.IsSenderAuthenticated)
            {
                return AIPerceptionRemoteSnapshotResult.UnauthenticatedSender;
            }

            if (!inbound.IsServerToClient)
            {
                return AIPerceptionRemoteSnapshotResult.InvalidDirection;
            }

            if (inbound.SenderConnectionId != inbound.AuthoritativeServerConnectionId)
            {
                return AIPerceptionRemoteSnapshotResult.SenderIsNotAuthority;
            }

            if (inbound.AuthorityGeneration != observer.AuthorityGeneration ||
                inbound.AuthorityGeneration != snapshot.AuthorityGeneration ||
                inbound.AuthorityGeneration != context.AuthorityGeneration)
            {
                return AIPerceptionRemoteSnapshotResult.AuthorityGenerationMismatch;
            }

            if (AIPerceptionNetworkMessageValidator.Validate(in snapshot, entries) !=
                AIPerceptionNetworkMessageValidationResult.Valid)
            {
                return AIPerceptionRemoteSnapshotResult.MalformedSnapshot;
            }

            if (!inbound.HasAppliedSnapshot)
            {
                return AIPerceptionRemoteSnapshotResult.Allowed;
            }

            if (snapshot.Tick < inbound.LastAppliedTick)
            {
                return AIPerceptionRemoteSnapshotResult.StaleTick;
            }

            return IsNewerSequence(snapshot.Sequence, inbound.LastAppliedSequence)
                ? AIPerceptionRemoteSnapshotResult.Allowed
                : AIPerceptionRemoteSnapshotResult.ReplayedOrOutOfOrderSequence;
        }

        private static bool IsNewerSequence(ushort candidate, ushort previous)
        {
            ushort delta = unchecked((ushort)(candidate - previous));
            return delta != 0 && delta < 0x8000;
        }
    }

    public readonly struct AIPerceptionReplicationContext
    {
        public readonly NetworkedAIPerceptionObserver Observer;
        public readonly NetworkReplicationPolicy Policy;

        public AIPerceptionReplicationContext(
            in NetworkedAIPerceptionObserver observer,
            in NetworkReplicationPolicy policy)
        {
            Observer = observer;
            Policy = policy;
        }
    }

    public interface IAIPerceptionNetworkObserverSource
    {
        bool TryGetObserver(int connectionId, out NetworkInterestObserver observer);
    }

    public interface IAIPerceptionNetworkObserverResolver
    {
        int ResolveObservers(
            in AIPerceptionReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IAIPerceptionNetworkObserverSource observerSource,
            IList<INetConnection> results);
    }

    /// <summary>Adapts AIPerception observers to the shared Networking interest evaluator.</summary>
    public sealed class AIPerceptionNetworkObserverResolver : IAIPerceptionNetworkObserverResolver
    {
        private readonly INetworkInterestEvaluator _interestEvaluator;

        public AIPerceptionNetworkObserverResolver(INetworkInterestEvaluator interestEvaluator = null)
        {
            _interestEvaluator = interestEvaluator ?? DefaultNetworkInterestEvaluator.Instance;
        }

        public int ResolveObservers(
            in AIPerceptionReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IAIPerceptionNetworkObserverSource observerSource,
            IList<INetConnection> results)
        {
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            if (!context.Observer.IsValid)
            {
                return 0;
            }

            NetworkReplicationPolicy effectivePolicy = CreateEffectivePolicy(in context);
            var replicatedObject = new NetworkReplicatedObject(
                context.Observer.ObserverNetworkId,
                effectivePolicy,
                context.Observer.InterestPosition,
                context.Observer.OwnerConnectionId,
                context.Observer.OwnerPlayerId,
                context.Observer.TeamId,
                context.Observer.InterestLayerMask);

            for (int i = 0; i < candidates.Count; i++)
            {
                INetConnection connection = candidates[i];
                if (connection == null || !connection.IsConnected || ContainsConnection(results, connection.ConnectionId))
                {
                    continue;
                }

                NetworkInterestObserver sourceObserver = default;
                bool hasSourceObserver = observerSource != null &&
                                         observerSource.TryGetObserver(
                                             connection.ConnectionId,
                                             out sourceObserver);
                if (hasSourceObserver && !sourceObserver.IsValid)
                {
                    continue;
                }

                var observer = new NetworkReplicationObserver(
                    connection.ConnectionId,
                    hasSourceObserver && sourceObserver.PlayerId != 0UL
                        ? sourceObserver.PlayerId
                        : connection.PlayerId,
                    hasSourceObserver ? sourceObserver.TeamId : 0,
                    hasSourceObserver ? sourceObserver.Position : NetworkVector3.Zero,
                    hasSourceObserver ? sourceObserver.Radius : 0f,
                    hasSourceObserver ? sourceObserver.LayerMask : NetworkReplicationObserver.ALL_LAYERS,
                    connection.IsAuthenticated,
                    connection.Quality);

                if (_interestEvaluator.IsInterested(in observer, in replicatedObject, out _))
                {
                    results.Add(connection);
                }
            }

            return results.Count;
        }

        private static NetworkReplicationPolicy CreateEffectivePolicy(in AIPerceptionReplicationContext context)
        {
            if (!context.Observer.AlwaysRelevant ||
                context.Policy.HasInterest(NetworkReplicationInterest.Always))
            {
                return context.Policy;
            }

            return new NetworkReplicationPolicy(
                context.Policy.Interest | NetworkReplicationInterest.Always,
                context.Policy.Channel,
                context.Policy.MaxDistance,
                context.Policy.MinIntervalTicks,
                context.Policy.Priority,
                context.Policy.IncludeOwner,
                context.Policy.RequireAuthenticated,
                context.Policy.SendUnchanged);
        }

        private static bool ContainsConnection(IList<INetConnection> results, int connectionId)
        {
            for (int i = 0; i < results.Count; i++)
            {
                if (results[i] != null && results[i].ConnectionId == connectionId)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
