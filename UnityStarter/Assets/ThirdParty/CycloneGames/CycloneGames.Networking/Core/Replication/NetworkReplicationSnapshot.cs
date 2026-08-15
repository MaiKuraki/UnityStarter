using System;

namespace CycloneGames.Networking.Replication
{
    public readonly struct NetworkReplicationObserver
    {
        public const uint ALL_LAYERS = uint.MaxValue;

        public readonly int ConnectionId;
        public readonly ulong PlayerId;
        public readonly int TeamId;
        public readonly NetworkVector3 Position;
        public readonly float ViewRadius;
        public readonly uint InterestLayerMask;
        public readonly bool IsAuthenticated;
        public readonly ConnectionQuality Quality;

        public NetworkReplicationObserver(
            int connectionId,
            ulong playerId,
            int teamId,
            NetworkVector3 position,
            float viewRadius,
            uint interestLayerMask = ALL_LAYERS,
            bool isAuthenticated = true,
            ConnectionQuality quality = ConnectionQuality.Good)
        {
            if (connectionId <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(connectionId));
            }

            if (teamId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(teamId));
            }

            if (!position.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (viewRadius < 0f || !float.IsFinite(viewRadius))
            {
                throw new ArgumentOutOfRangeException(nameof(viewRadius));
            }

            if (quality < ConnectionQuality.Excellent || quality > ConnectionQuality.Disconnected)
            {
                throw new ArgumentOutOfRangeException(nameof(quality));
            }

            ConnectionId = connectionId;
            PlayerId = playerId;
            TeamId = teamId;
            Position = position;
            ViewRadius = viewRadius;
            InterestLayerMask = interestLayerMask;
            IsAuthenticated = isAuthenticated;
            Quality = quality;
        }

        public bool IsValid =>
            ConnectionId > 0
            && TeamId >= 0
            && Position.IsFinite()
            && ViewRadius >= 0f
            && float.IsFinite(ViewRadius)
            && Quality >= ConnectionQuality.Excellent
            && Quality <= ConnectionQuality.Disconnected;
    }

    public readonly struct NetworkReplicatedObject
    {
        public const int NEVER_SENT = -1;

        public readonly ulong ObjectId;
        public readonly NetworkReplicationPolicy Policy;
        public readonly NetworkVector3 Position;
        public readonly int OwnerConnectionId;
        public readonly ulong OwnerPlayerId;
        public readonly int TeamId;
        public readonly uint InterestLayerMask;
        public readonly bool IsDirty;
        public readonly bool RequiresFullState;
        public readonly int LastSentTick;
        public readonly int EstimatedPayloadBytes;

        public NetworkReplicatedObject(
            ulong objectId,
            NetworkReplicationPolicy policy,
            NetworkVector3 position,
            int ownerConnectionId = 0,
            ulong ownerPlayerId = 0UL,
            int teamId = 0,
            uint interestLayerMask = NetworkReplicationObserver.ALL_LAYERS,
            bool isDirty = true,
            bool requiresFullState = false,
            int lastSentTick = NEVER_SENT,
            int estimatedPayloadBytes = 64)
        {
            if (objectId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(objectId));
            }

            if (!position.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (ownerConnectionId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(ownerConnectionId));
            }

            if (teamId < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(teamId));
            }

            if (lastSentTick < NEVER_SENT)
            {
                throw new ArgumentOutOfRangeException(nameof(lastSentTick));
            }

            if (estimatedPayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(estimatedPayloadBytes));
            }

            ObjectId = objectId;
            Policy = policy;
            Position = position;
            OwnerConnectionId = ownerConnectionId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            InterestLayerMask = interestLayerMask;
            IsDirty = isDirty;
            RequiresFullState = requiresFullState;
            LastSentTick = lastSentTick;
            EstimatedPayloadBytes = estimatedPayloadBytes;
        }

        public bool HasOwner
        {
            get
            {
                return OwnerConnectionId > 0 || OwnerPlayerId != 0UL;
            }
        }

        public bool IsValid =>
            ObjectId != 0UL
            && Position.IsFinite()
            && OwnerConnectionId >= 0
            && TeamId >= 0
            && LastSentTick >= NEVER_SENT
            && EstimatedPayloadBytes >= 0;
    }
}
