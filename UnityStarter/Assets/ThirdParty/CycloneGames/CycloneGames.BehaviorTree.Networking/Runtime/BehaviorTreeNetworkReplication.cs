using System;
using System.Collections.Generic;
using CycloneGames.Networking;
using CycloneGames.Networking.Replication;

namespace CycloneGames.BehaviorTree.Networking
{
    public readonly struct BehaviorTreeReplicationContext
    {
        public readonly NetworkedBehaviorTreeAgent Agent;
        public readonly NetworkReplicationPolicy Policy;

        public BehaviorTreeReplicationContext(
            in NetworkedBehaviorTreeAgent agent,
            in NetworkReplicationPolicy policy)
        {
            Agent = agent;
            Policy = policy;
        }
    }

    public interface IBehaviorTreeNetworkObserverSource
    {
        bool TryGetObserver(int connectionId, out NetworkInterestObserver observer);
    }

    public interface IBehaviorTreeNetworkObserverResolver
    {
        int ResolveObservers(
            in BehaviorTreeReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IBehaviorTreeNetworkObserverSource observerSource,
            IList<INetConnection> results);
    }

    public sealed class BehaviorTreeNetworkObserverResolver : IBehaviorTreeNetworkObserverResolver
    {
        public int ResolveObservers(
            in BehaviorTreeReplicationContext context,
            IReadOnlyList<INetConnection> candidates,
            IBehaviorTreeNetworkObserverSource observerSource,
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

            if (!context.Agent.IsValid || context.Policy.Interest == NetworkReplicationInterest.None)
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
            in BehaviorTreeReplicationContext context,
            INetConnection connection,
            IBehaviorTreeNetworkObserverSource observerSource)
        {
            if (context.Agent.AlwaysRelevant || context.Policy.HasInterest(NetworkReplicationInterest.Always))
            {
                return true;
            }

            bool isOwner = context.Agent.OwnerConnectionId == connection.ConnectionId;
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
            in BehaviorTreeReplicationContext context,
            INetConnection connection,
            IBehaviorTreeNetworkObserverSource observerSource)
        {
            if (context.Agent.TeamId == 0 || observerSource == null)
            {
                return false;
            }

            return observerSource.TryGetObserver(connection.ConnectionId, out NetworkInterestObserver observer) &&
                   observer.TeamId == context.Agent.TeamId;
        }

        private static bool IsAreaObserver(
            in BehaviorTreeReplicationContext context,
            INetConnection connection,
            IBehaviorTreeNetworkObserverSource observerSource)
        {
            if (context.Policy.MaxDistance <= 0f || observerSource == null)
            {
                return false;
            }

            if (!observerSource.TryGetObserver(connection.ConnectionId, out NetworkInterestObserver observer))
            {
                return false;
            }

            if ((observer.LayerMask & context.Agent.InterestLayerMask) == 0u)
            {
                return false;
            }

            float radius = context.Policy.MaxDistance;
            return NetworkVector3.SqrDistance(observer.Position, context.Agent.InterestPosition) <= radius * radius;
        }
    }
}
