using System;

namespace CycloneGames.Networking.Replication
{
    [Flags]
    public enum NetworkReplicationInterest : byte
    {
        None = 0,
        Always = 1 << 0,
        Owner = 1 << 1,
        Team = 1 << 2,
        Area = 1 << 3,
        Manual = 1 << 4
    }

    [Flags]
    public enum NetworkInterestReason : byte
    {
        None = 0,
        Always = 1 << 0,
        Owner = 1 << 1,
        Team = 1 << 2,
        Area = 1 << 3,
        Manual = 1 << 4
    }

    public readonly struct NetworkReplicationPolicy : IEquatable<NetworkReplicationPolicy>
    {
        public static readonly NetworkReplicationPolicy Never = new NetworkReplicationPolicy(
            NetworkReplicationInterest.None,
            NetworkChannel.Reliable,
            maxDistance: 0f,
            minIntervalTicks: 0,
            priority: 0f,
            includeOwner: false,
            requireAuthenticated: true,
            sendUnchanged: false);

        public readonly NetworkReplicationInterest Interest;
        public readonly NetworkChannel Channel;
        public readonly float MaxDistance;
        public readonly int MinIntervalTicks;
        public readonly float Priority;
        public readonly bool IncludeOwner;
        public readonly bool RequireAuthenticated;
        public readonly bool SendUnchanged;

        public NetworkReplicationPolicy(
            NetworkReplicationInterest interest,
            NetworkChannel channel = NetworkChannel.Reliable,
            float maxDistance = 0f,
            int minIntervalTicks = 1,
            float priority = 1f,
            bool includeOwner = true,
            bool requireAuthenticated = true,
            bool sendUnchanged = false)
        {
            if (maxDistance < 0f || float.IsNaN(maxDistance))
            {
                throw new ArgumentOutOfRangeException(nameof(maxDistance));
            }

            if (minIntervalTicks < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(minIntervalTicks));
            }

            if (priority < 0f || float.IsNaN(priority))
            {
                throw new ArgumentOutOfRangeException(nameof(priority));
            }

            Interest = interest;
            Channel = channel;
            MaxDistance = maxDistance;
            MinIntervalTicks = minIntervalTicks;
            Priority = priority;
            IncludeOwner = includeOwner;
            RequireAuthenticated = requireAuthenticated;
            SendUnchanged = sendUnchanged;
        }

        public static NetworkReplicationPolicy Always(
            NetworkChannel channel = NetworkChannel.Reliable,
            int minIntervalTicks = 1,
            float priority = 1f,
            bool requireAuthenticated = true,
            bool sendUnchanged = false)
        {
            return new NetworkReplicationPolicy(
                NetworkReplicationInterest.Always,
                channel,
                maxDistance: 0f,
                minIntervalTicks,
                priority,
                includeOwner: true,
                requireAuthenticated,
                sendUnchanged);
        }

        public static NetworkReplicationPolicy OwnerOnly(
            NetworkChannel channel = NetworkChannel.Reliable,
            int minIntervalTicks = 1,
            float priority = 1f,
            bool requireAuthenticated = true,
            bool sendUnchanged = false)
        {
            return new NetworkReplicationPolicy(
                NetworkReplicationInterest.Owner,
                channel,
                maxDistance: 0f,
                minIntervalTicks,
                priority,
                includeOwner: true,
                requireAuthenticated,
                sendUnchanged);
        }

        public static NetworkReplicationPolicy Area(
            float maxDistance,
            NetworkChannel channel = NetworkChannel.UnreliableSequenced,
            int minIntervalTicks = 1,
            float priority = 1f,
            bool includeOwner = true,
            bool requireAuthenticated = true,
            bool sendUnchanged = false)
        {
            return new NetworkReplicationPolicy(
                NetworkReplicationInterest.Area,
                channel,
                maxDistance,
                minIntervalTicks,
                priority,
                includeOwner,
                requireAuthenticated,
                sendUnchanged);
        }

        public static NetworkReplicationPolicy OwnerOrArea(
            float maxDistance,
            NetworkChannel channel = NetworkChannel.UnreliableSequenced,
            int minIntervalTicks = 1,
            float priority = 1f,
            bool requireAuthenticated = true,
            bool sendUnchanged = false)
        {
            return new NetworkReplicationPolicy(
                NetworkReplicationInterest.Owner | NetworkReplicationInterest.Area,
                channel,
                maxDistance,
                minIntervalTicks,
                priority,
                includeOwner: true,
                requireAuthenticated,
                sendUnchanged);
        }

        public static NetworkReplicationPolicy TeamOrArea(
            float maxDistance,
            NetworkChannel channel = NetworkChannel.UnreliableSequenced,
            int minIntervalTicks = 1,
            float priority = 1f,
            bool includeOwner = true,
            bool requireAuthenticated = true,
            bool sendUnchanged = false)
        {
            return new NetworkReplicationPolicy(
                NetworkReplicationInterest.Team | NetworkReplicationInterest.Area,
                channel,
                maxDistance,
                minIntervalTicks,
                priority,
                includeOwner,
                requireAuthenticated,
                sendUnchanged);
        }

        public bool HasInterest(NetworkReplicationInterest interest)
        {
            return (Interest & interest) != 0;
        }

        public bool Equals(NetworkReplicationPolicy other)
        {
            return Interest == other.Interest
                   && Channel == other.Channel
                   && MaxDistance.Equals(other.MaxDistance)
                   && MinIntervalTicks == other.MinIntervalTicks
                   && Priority.Equals(other.Priority)
                   && IncludeOwner == other.IncludeOwner
                   && RequireAuthenticated == other.RequireAuthenticated
                   && SendUnchanged == other.SendUnchanged;
        }

        public override bool Equals(object obj)
        {
            return obj is NetworkReplicationPolicy other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = (int)Interest;
                hash = (hash * 397) ^ (int)Channel;
                hash = (hash * 397) ^ MaxDistance.GetHashCode();
                hash = (hash * 397) ^ MinIntervalTicks;
                hash = (hash * 397) ^ Priority.GetHashCode();
                hash = (hash * 397) ^ IncludeOwner.GetHashCode();
                hash = (hash * 397) ^ RequireAuthenticated.GetHashCode();
                hash = (hash * 397) ^ SendUnchanged.GetHashCode();
                return hash;
            }
        }
    }
}
