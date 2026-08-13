using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Networking;

namespace CycloneGames.GameplayFramework.Networking
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
            if (maxDistance < 0f || float.IsNaN(maxDistance) || float.IsInfinity(maxDistance))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistance));
            }

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
        public readonly uint NetworkId;
        public readonly int OwnerConnectionId;
        public readonly ulong OwnerPlayerId;
        public readonly int TeamId;
        public readonly uint InterestLayerMask;
        public readonly bool AlwaysRelevant;
        public readonly NetworkVector3 InterestPosition;

        public NetworkedGameplayActor(
            uint networkId,
            int ownerConnectionId,
            ulong ownerPlayerId,
            int teamId,
            uint interestLayerMask,
            bool alwaysRelevant,
            NetworkVector3 interestPosition)
        {
            NetworkId = networkId;
            OwnerConnectionId = ownerConnectionId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            InterestLayerMask = interestLayerMask;
            AlwaysRelevant = alwaysRelevant;
            InterestPosition = interestPosition;
        }

        public bool HasOwner => OwnerConnectionId > 0;
        public bool IsValid => NetworkId != 0u && OwnerConnectionId >= 0 && InterestPosition.IsFinite();

        public NetworkInterestTarget ToInterestTarget()
        {
            return new NetworkInterestTarget(NetworkId, InterestPosition, InterestLayerMask, OwnerPlayerId, TeamId);
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
            return actor.IsValid &&
                   actor.HasOwner &&
                   context.IsClient &&
                   context.LocalConnectionId > 0 &&
                   context.LocalConnectionId == actor.OwnerConnectionId;
        }

        public GameplayNetworkAuthorityRole GetRole(in GameplayNetworkAuthorityContext context, in NetworkedGameplayActor actor)
        {
            if (!actor.IsValid)
            {
                return GameplayNetworkAuthorityRole.None;
            }

            if (context.IsServer)
            {
                return GameplayNetworkAuthorityRole.ServerAuthority;
            }

            if (context.IsClient &&
                context.LocalConnectionId > 0 &&
                actor.HasOwner &&
                context.LocalConnectionId == actor.OwnerConnectionId)
            {
                return GameplayNetworkAuthorityRole.AutonomousProxy;
            }

            return context.IsClient
                ? GameplayNetworkAuthorityRole.SimulatedProxy
                : GameplayNetworkAuthorityRole.None;
        }
    }

    public sealed class GameplayNetworkObserverResolver : IGameplayNetworkObserverResolver
    {
        public const int MaximumSupportedResultCount = GameplayNetworkObserverRegistry.MaximumSupportedObserverCount;

        private readonly HashSet<int> selectedConnectionIds;
        private readonly int maximumResultCount;
        private readonly int ownerThreadId;

        public GameplayNetworkObserverResolver(
            int initialCapacity = 16,
            int maximumResultCount = MaximumSupportedResultCount)
        {
            if (maximumResultCount < 0 || maximumResultCount > MaximumSupportedResultCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumResultCount));
            }

            if (initialCapacity < 0 || initialCapacity > maximumResultCount)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            this.maximumResultCount = maximumResultCount;
            ownerThreadId = Thread.CurrentThread.ManagedThreadId;
            selectedConnectionIds = new HashSet<int>();
            selectedConnectionIds.EnsureCapacity(initialCapacity);
        }

        public int MaximumResultCount => maximumResultCount;

        public int ResolveObservers(
            in GameplayReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IGameplayNetworkObserverSource observerSource,
            IList<INetConnection> results)
        {
            AssertOwnerThread();
            if (candidates == null)
            {
                throw new ArgumentNullException(nameof(candidates));
            }

            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            results.Clear();
            selectedConnectionIds.Clear();

            if (maximumResultCount == 0 ||
                !context.Target.IsValid ||
                context.Policy.Visibility == GameplayReplicationVisibility.None)
            {
                return 0;
            }

            for (int i = 0; i < candidates.Count; i++)
            {
                if (results.Count >= maximumResultCount)
                {
                    break;
                }

                INetConnection connection = candidates[i];
                if (connection == null)
                {
                    continue;
                }

                int connectionId = connection.ConnectionId;
                if (connectionId <= 0 || !connection.IsConnected)
                {
                    continue;
                }

                if (context.Policy.RequireAuthenticated && !connection.IsAuthenticated)
                {
                    continue;
                }

                if (ShouldReplicateToConnection(
                        context,
                        connection,
                        connectionId,
                        observerSource) &&
                    selectedConnectionIds.Add(connectionId))
                {
                    results.Add(connection);
                }
            }

            return results.Count;
        }

        private void AssertOwnerThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != ownerThreadId)
            {
                throw new InvalidOperationException(
                    "GameplayNetworkObserverResolver must be accessed on its owning thread.");
            }
        }

        private static bool ShouldReplicateToConnection(
            in GameplayReplicationContext context,
            INetConnection connection,
            int connectionId,
            IGameplayNetworkObserverSource observerSource)
        {
            if (context.Target.AlwaysRelevant || context.Policy.Visibility == GameplayReplicationVisibility.All)
            {
                return true;
            }

            bool isOwner = context.Target.HasOwner &&
                           context.Target.OwnerConnectionId == connectionId;
            if (isOwner)
            {
                return context.Policy.IncludeOwner || context.Policy.Visibility == GameplayReplicationVisibility.OwnerOnly;
            }

            switch (context.Policy.Visibility)
            {
                case GameplayReplicationVisibility.OwnerOnly:
                    return false;
                case GameplayReplicationVisibility.Team:
                    return IsTeamObserver(context, connectionId, observerSource);
                case GameplayReplicationVisibility.Area:
                    return IsAreaObserver(context, connectionId, observerSource);
                case GameplayReplicationVisibility.TeamOrArea:
                    return IsTeamObserver(context, connectionId, observerSource) ||
                           IsAreaObserver(context, connectionId, observerSource);
                default:
                    return false;
            }
        }

        private static bool IsTeamObserver(
            in GameplayReplicationContext context,
            int connectionId,
            IGameplayNetworkObserverSource observerSource)
        {
            if (context.Target.TeamId == 0 || observerSource == null)
            {
                return false;
            }

            return observerSource.TryGetObserver(connectionId, out NetworkInterestObserver observer) &&
                   observer.TeamId == context.Target.TeamId;
        }

        private static bool IsAreaObserver(
            in GameplayReplicationContext context,
            int connectionId,
            IGameplayNetworkObserverSource observerSource)
        {
            if (context.Policy.MaxDistance <= 0f || observerSource == null)
            {
                return false;
            }

            if (!observerSource.TryGetObserver(connectionId, out NetworkInterestObserver observer))
            {
                return false;
            }

            if (!observer.Position.IsFinite() ||
                observer.Radius <= 0f ||
                float.IsNaN(observer.Radius) ||
                float.IsInfinity(observer.Radius))
            {
                return false;
            }

            if ((observer.LayerMask & context.Policy.LayerMask & context.Target.InterestLayerMask) == 0u)
            {
                return false;
            }

            float radius = Math.Min(context.Policy.MaxDistance, observer.Radius);
            return NetworkVector3.SqrDistance(observer.Position, context.Target.InterestPosition) <= radius * radius;
        }
    }
}
