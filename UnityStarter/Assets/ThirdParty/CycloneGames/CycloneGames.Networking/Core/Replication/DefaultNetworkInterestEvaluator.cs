using System;

namespace CycloneGames.Networking.Replication
{
    public interface INetworkInterestEvaluator
    {
        bool IsInterested(
            in NetworkReplicationObserver observer,
            in NetworkReplicatedObject replicatedObject,
            out NetworkInterestReason reason);
    }

    public sealed class DefaultNetworkInterestEvaluator : INetworkInterestEvaluator
    {
        public static readonly DefaultNetworkInterestEvaluator Instance = new DefaultNetworkInterestEvaluator();

        public bool IsInterested(
            in NetworkReplicationObserver observer,
            in NetworkReplicatedObject replicatedObject,
            out NetworkInterestReason reason)
        {
            NetworkReplicationPolicy policy = replicatedObject.Policy;
            reason = NetworkInterestReason.None;

            if (policy.Interest == NetworkReplicationInterest.None)
            {
                return false;
            }

            if (policy.RequireAuthenticated && !observer.IsAuthenticated)
            {
                return false;
            }

            if ((observer.InterestLayerMask & replicatedObject.InterestLayerMask) == 0u)
            {
                return false;
            }

            bool isOwner = IsOwner(observer, replicatedObject);
            if (policy.HasInterest(NetworkReplicationInterest.Owner) && isOwner)
            {
                reason |= NetworkInterestReason.Owner;
            }

            if (policy.HasInterest(NetworkReplicationInterest.Always))
            {
                reason |= NetworkInterestReason.Always;
            }

            if (policy.HasInterest(NetworkReplicationInterest.Team)
                && observer.TeamId != 0
                && observer.TeamId == replicatedObject.TeamId)
            {
                reason |= NetworkInterestReason.Team;
            }

            if (policy.HasInterest(NetworkReplicationInterest.Area)
                && IsInsideArea(observer, replicatedObject, policy.MaxDistance))
            {
                reason |= NetworkInterestReason.Area;
            }

            if (reason == NetworkInterestReason.None)
            {
                return false;
            }

            if (isOwner && !policy.IncludeOwner && (reason & NetworkInterestReason.Owner) == 0)
            {
                reason = NetworkInterestReason.None;
                return false;
            }

            return true;
        }

        private static bool IsOwner(
            in NetworkReplicationObserver observer,
            in NetworkReplicatedObject replicatedObject)
        {
            if (!replicatedObject.HasOwner)
            {
                return false;
            }

            return (replicatedObject.OwnerConnectionId != 0 && replicatedObject.OwnerConnectionId == observer.ConnectionId)
                   || (replicatedObject.OwnerPlayerId != 0UL && replicatedObject.OwnerPlayerId == observer.PlayerId);
        }

        private static bool IsInsideArea(
            in NetworkReplicationObserver observer,
            in NetworkReplicatedObject replicatedObject,
            float maxDistance)
        {
            float radius = maxDistance;
            if (observer.ViewRadius > 0f)
            {
                radius = radius > 0f ? MathF.Min(radius, observer.ViewRadius) : observer.ViewRadius;
            }

            if (radius <= 0f)
            {
                return false;
            }

            return NetworkVector3.SqrDistance(observer.Position, replicatedObject.Position) <= radius * radius;
        }
    }
}
