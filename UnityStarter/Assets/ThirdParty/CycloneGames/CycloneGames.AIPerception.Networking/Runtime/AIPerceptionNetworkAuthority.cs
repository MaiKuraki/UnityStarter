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
            uint authorityGeneration = 0u)
        {
            IsServer = isServer;
            IsClient = isClient;
            LocalConnectionId = localConnectionId;
            AuthorityGeneration = authorityGeneration;
        }
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
            uint authorityGeneration = 0u)
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

        public bool IsValid => ObserverNetworkId != 0u && InterestPosition.IsFinite();
    }

    public interface IAIPerceptionNetworkAuthorityResolver
    {
        AIPerceptionNetworkAuthorityRole GetRole(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer);

        bool CanProduceAuthoritativePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer);

        bool CanApplyRemotePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer,
            in AIPerceptionDetectionSnapshotMessage snapshot);
    }

    public sealed class ServerAuthoritativeAIPerceptionAuthorityResolver : IAIPerceptionNetworkAuthorityResolver
    {
        public AIPerceptionNetworkAuthorityRole GetRole(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer)
        {
            if (!observer.IsValid)
            {
                return AIPerceptionNetworkAuthorityRole.None;
            }

            if (context.IsServer)
            {
                return AIPerceptionNetworkAuthorityRole.ServerAuthority;
            }

            if (context.IsClient && context.LocalConnectionId == observer.OwnerConnectionId)
            {
                return AIPerceptionNetworkAuthorityRole.AutonomousObserver;
            }

            return AIPerceptionNetworkAuthorityRole.SimulatedObserver;
        }

        public bool CanProduceAuthoritativePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer)
        {
            return context.IsServer && observer.IsValid;
        }

        public bool CanApplyRemotePerception(
            in AIPerceptionNetworkAuthorityContext context,
            in NetworkedAIPerceptionObserver observer,
            in AIPerceptionDetectionSnapshotMessage snapshot)
        {
            if (!observer.IsValid || !snapshot.IsValid || snapshot.ObserverNetworkId != observer.ObserverNetworkId)
            {
                return false;
            }

            return context.IsClient && !context.IsServer;
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

    public sealed class AIPerceptionNetworkObserverResolver : IAIPerceptionNetworkObserverResolver
    {
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

            if (!context.Observer.IsValid || context.Policy.Interest == NetworkReplicationInterest.None)
            {
                return 0;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                INetConnection connection = candidates[i];
                if (connection == null || !connection.IsConnected)
                {
                    continue;
                }

                if (context.Policy.RequireAuthenticated && !connection.IsAuthenticated)
                {
                    continue;
                }

                if (ShouldReplicateToConnection(context, connection, observerSource))
                {
                    results.Add(connection);
                }
            }

            return results.Count;
        }

        private static bool ShouldReplicateToConnection(
            in AIPerceptionReplicationContext context,
            INetConnection connection,
            IAIPerceptionNetworkObserverSource observerSource)
        {
            if (context.Observer.AlwaysRelevant || context.Policy.HasInterest(NetworkReplicationInterest.Always))
            {
                return true;
            }

            bool isOwner = context.Observer.OwnerConnectionId == connection.ConnectionId;
            if (isOwner)
            {
                return context.Policy.IncludeOwner || context.Policy.HasInterest(NetworkReplicationInterest.Owner);
            }

            if (context.Policy.HasInterest(NetworkReplicationInterest.Team) &&
                IsTeamObserver(context, connection, observerSource))
            {
                return true;
            }

            if (context.Policy.HasInterest(NetworkReplicationInterest.Area) &&
                IsAreaObserver(context, connection, observerSource))
            {
                return true;
            }

            return false;
        }

        private static bool IsTeamObserver(
            in AIPerceptionReplicationContext context,
            INetConnection connection,
            IAIPerceptionNetworkObserverSource observerSource)
        {
            if (context.Observer.TeamId == 0 || observerSource == null)
            {
                return false;
            }

            return observerSource.TryGetObserver(connection.ConnectionId, out NetworkInterestObserver observer) &&
                   observer.TeamId == context.Observer.TeamId;
        }

        private static bool IsAreaObserver(
            in AIPerceptionReplicationContext context,
            INetConnection connection,
            IAIPerceptionNetworkObserverSource observerSource)
        {
            if (context.Policy.MaxDistance <= 0f || observerSource == null)
            {
                return false;
            }

            if (!observerSource.TryGetObserver(connection.ConnectionId, out NetworkInterestObserver observer))
            {
                return false;
            }

            if ((observer.LayerMask & context.Observer.InterestLayerMask) == 0u)
            {
                return false;
            }

            float radius = context.Policy.MaxDistance;
            return NetworkVector3.SqrDistance(observer.Position, context.Observer.InterestPosition) <= radius * radius;
        }
    }
}

