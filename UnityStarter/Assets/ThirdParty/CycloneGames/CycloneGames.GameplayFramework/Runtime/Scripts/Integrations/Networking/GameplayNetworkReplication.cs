using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Integrations.Networking
{
    public enum GameplayNetworkAuthorityRole : byte
    {
        None,
        ServerAuthority,
        AutonomousProxy,
        SimulatedProxy
    }

    public enum GameplayReplicationVisibility : byte
    {
        None,
        OwnerOnly,
        Area,
        Team,
        TeamOrArea,
        All
    }

    public readonly struct GameplayNetworkAuthorityContext
    {
        public readonly bool IsServer;
        public readonly bool IsClient;
        public readonly int LocalConnectionId;

        public GameplayNetworkAuthorityContext(bool isServer, bool isClient, int localConnectionId)
        {
            IsServer = isServer;
            IsClient = isClient;
            LocalConnectionId = localConnectionId;
        }
    }

    public readonly struct GameplayReplicationPolicy
    {
        public readonly GameplayReplicationVisibility Visibility;
        public readonly NetworkChannel Channel;
        public readonly float MaxDistance;
        public readonly ushort MinTickInterval;
        public readonly byte Priority;
        public readonly uint LayerMask;
        public readonly bool IncludeOwner;
        public readonly bool RequireAuthenticated;

        public GameplayReplicationPolicy(
            GameplayReplicationVisibility visibility,
            NetworkChannel channel,
            float maxDistance,
            ushort minTickInterval,
            byte priority,
            uint layerMask,
            bool includeOwner,
            bool requireAuthenticated)
        {
            if (maxDistance < 0f)
                throw new ArgumentOutOfRangeException(nameof(maxDistance));

            Visibility = visibility;
            Channel = channel;
            MaxDistance = maxDistance;
            MinTickInterval = minTickInterval;
            Priority = priority;
            LayerMask = layerMask;
            IncludeOwner = includeOwner;
            RequireAuthenticated = requireAuthenticated;
        }

        public static GameplayReplicationPolicy OwnerReliable => new GameplayReplicationPolicy(
            GameplayReplicationVisibility.OwnerOnly,
            NetworkChannel.Reliable,
            0f,
            1,
            255,
            uint.MaxValue,
            true,
            true);

        public static GameplayReplicationPolicy AreaUnreliable(float maxDistance, byte priority = 128, uint layerMask = uint.MaxValue)
        {
            return new GameplayReplicationPolicy(
                GameplayReplicationVisibility.Area,
                NetworkChannel.Unreliable,
                maxDistance,
                1,
                priority,
                layerMask,
                false,
                true);
        }

        public static GameplayReplicationPolicy TeamReliable => new GameplayReplicationPolicy(
            GameplayReplicationVisibility.Team,
            NetworkChannel.Reliable,
            0f,
            1,
            192,
            uint.MaxValue,
            true,
            true);

        public static GameplayReplicationPolicy AlwaysRelevantReliable => new GameplayReplicationPolicy(
            GameplayReplicationVisibility.All,
            NetworkChannel.Reliable,
            0f,
            1,
            255,
            uint.MaxValue,
            true,
            true);
    }

    public readonly struct NetworkedGameplayActor
    {
        public readonly Actor Actor;
        public readonly uint NetworkId;
        public readonly int OwnerConnectionId;
        public readonly ulong OwnerPlayerId;
        public readonly int TeamId;
        public readonly uint InterestLayerMask;
        public readonly bool AlwaysRelevant;
        public readonly NetworkVector3 InterestPosition;

        public NetworkedGameplayActor(
            Actor actor,
            uint networkId,
            int ownerConnectionId,
            ulong ownerPlayerId,
            int teamId,
            uint interestLayerMask,
            bool alwaysRelevant,
            NetworkVector3 interestPosition)
        {
            Actor = actor;
            NetworkId = networkId;
            OwnerConnectionId = ownerConnectionId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            InterestLayerMask = interestLayerMask;
            AlwaysRelevant = alwaysRelevant;
            InterestPosition = interestPosition;
        }

        public bool IsValid => NetworkId != 0u && InterestPosition.IsFinite();

        public NetworkInterestTarget ToInterestTarget()
        {
            return new NetworkInterestTarget(NetworkId, InterestPosition, InterestLayerMask, OwnerPlayerId, TeamId);
        }

        public static NetworkedGameplayActor FromActor(
            Actor actor,
            uint networkId,
            int ownerConnectionId,
            ulong ownerPlayerId = 0UL,
            int teamId = 0,
            uint interestLayerMask = uint.MaxValue,
            bool alwaysRelevant = false)
        {
            if (actor == null)
                throw new ArgumentNullException(nameof(actor));

            Vector3 position = actor.GetActorLocation();
            return new NetworkedGameplayActor(
                actor,
                networkId,
                ownerConnectionId,
                ownerPlayerId,
                teamId,
                interestLayerMask,
                alwaysRelevant,
                new NetworkVector3(position.x, position.y, position.z));
        }
    }

    public readonly struct GameplayReplicationContext
    {
        public readonly NetworkedGameplayActor Target;
        public readonly GameplayReplicationPolicy Policy;

        public GameplayReplicationContext(in NetworkedGameplayActor target, in GameplayReplicationPolicy policy)
        {
            Target = target;
            Policy = policy;
        }
    }

    public interface IGameplayNetworkAuthorityResolver
    {
        GameplayNetworkAuthorityRole GetRole(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor);
        bool CanWriteAuthoritativeState(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor);
        bool CanSendOwnerInput(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor);
    }

    public interface IGameplayNetworkObserverSource
    {
        bool TryGetObserver(int connectionId, out NetworkInterestObserver observer);
    }

    public interface IGameplayNetworkObserverResolver
    {
        int ResolveObservers(
            in GameplayReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IGameplayNetworkObserverSource observerSource,
            IList<INetConnection> results);
    }

    public sealed class ServerAuthoritativeGameplayAuthorityResolver : IGameplayNetworkAuthorityResolver
    {
        public bool CanWriteAuthoritativeState(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor)
        {
            return context.IsServer && actor.IsValid;
        }

        public bool CanSendOwnerInput(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor)
        {
            return actor.IsValid && context.IsClient && context.LocalConnectionId == actor.OwnerConnectionId;
        }

        public GameplayNetworkAuthorityRole GetRole(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor)
        {
            if (!actor.IsValid)
                return GameplayNetworkAuthorityRole.None;

            if (context.IsServer)
                return GameplayNetworkAuthorityRole.ServerAuthority;

            if (context.IsClient && context.LocalConnectionId == actor.OwnerConnectionId)
                return GameplayNetworkAuthorityRole.AutonomousProxy;

            return GameplayNetworkAuthorityRole.SimulatedProxy;
        }
    }

    public sealed class GameplayNetworkObserverResolver : IGameplayNetworkObserverResolver
    {
        public int ResolveObservers(
            in GameplayReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IGameplayNetworkObserverSource observerSource,
            IList<INetConnection> results)
        {
            if (candidates == null)
                throw new ArgumentNullException(nameof(candidates));
            if (results == null)
                throw new ArgumentNullException(nameof(results));

            results.Clear();

            if (!context.Target.IsValid || context.Policy.Visibility == GameplayReplicationVisibility.None)
                return 0;

            for (int i = 0; i < candidates.Count; i++)
            {
                INetConnection connection = candidates[i];
                if (connection == null || !connection.IsConnected)
                    continue;

                if (context.Policy.RequireAuthenticated && !connection.IsAuthenticated)
                    continue;

                if (ShouldReplicateToConnection(context, connection, observerSource))
                    results.Add(connection);
            }

            return results.Count;
        }

        private static bool ShouldReplicateToConnection(
            in GameplayReplicationContext context,
            INetConnection connection,
            IGameplayNetworkObserverSource observerSource)
        {
            if (context.Target.AlwaysRelevant || context.Policy.Visibility == GameplayReplicationVisibility.All)
                return true;

            bool isOwner = context.Target.OwnerConnectionId == connection.ConnectionId;
            if (isOwner)
                return context.Policy.IncludeOwner || context.Policy.Visibility == GameplayReplicationVisibility.OwnerOnly;

            switch (context.Policy.Visibility)
            {
                case GameplayReplicationVisibility.OwnerOnly:
                    return false;
                case GameplayReplicationVisibility.Team:
                    return IsTeamObserver(context, connection, observerSource);
                case GameplayReplicationVisibility.Area:
                    return IsAreaObserver(context, connection, observerSource);
                case GameplayReplicationVisibility.TeamOrArea:
                    return IsTeamObserver(context, connection, observerSource) ||
                           IsAreaObserver(context, connection, observerSource);
                default:
                    return false;
            }
        }

        private static bool IsTeamObserver(
            in GameplayReplicationContext context,
            INetConnection connection,
            IGameplayNetworkObserverSource observerSource)
        {
            if (context.Target.TeamId == 0 || observerSource == null)
                return false;

            return observerSource.TryGetObserver(connection.ConnectionId, out NetworkInterestObserver observer) &&
                   observer.TeamId == context.Target.TeamId;
        }

        private static bool IsAreaObserver(
            in GameplayReplicationContext context,
            INetConnection connection,
            IGameplayNetworkObserverSource observerSource)
        {
            if (context.Policy.MaxDistance <= 0f || observerSource == null)
                return false;

            if (!observerSource.TryGetObserver(connection.ConnectionId, out NetworkInterestObserver observer))
                return false;

            if ((observer.LayerMask & context.Policy.LayerMask & context.Target.InterestLayerMask) == 0u)
                return false;

            float radius = context.Policy.MaxDistance;
            return NetworkVector3.SqrDistance(observer.Position, context.Target.InterestPosition) <= radius * radius;
        }
    }
}
