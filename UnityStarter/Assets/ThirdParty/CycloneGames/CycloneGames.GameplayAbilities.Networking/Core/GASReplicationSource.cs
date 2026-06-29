using System;
using CycloneGames.Networking;
using CycloneGames.Networking.Replication;

namespace CycloneGames.GameplayAbilities.Networking
{
    /// <summary>
    /// Network planning projection for one Ability System Component.
    /// </summary>
    public readonly struct GASReplicationSource
    {
        public const int DEFAULT_DELTA_PAYLOAD_BYTES = 96;
        public const int DEFAULT_FULL_STATE_PAYLOAD_BYTES = 512;

        public readonly uint NetworkId;
        public readonly NetworkVector3 Position;
        public readonly int OwnerConnectionId;
        public readonly ulong OwnerPlayerId;
        public readonly int TeamId;
        public readonly uint InterestLayerMask;
        public readonly NetworkReplicationPolicy Policy;
        public readonly uint ChangeMask;
        public readonly bool RequiresFullState;
        public readonly int EstimatedPayloadBytes;
        public readonly int LastSentTick;
        public readonly ulong StateVersion;
        public readonly ulong StateChecksum;

        public GASReplicationSource(
            uint networkId,
            NetworkVector3 position,
            NetworkReplicationPolicy policy,
            uint changeMask,
            int ownerConnectionId = 0,
            ulong ownerPlayerId = 0UL,
            int teamId = 0,
            uint interestLayerMask = NetworkReplicationObserver.ALL_LAYERS,
            bool requiresFullState = false,
            int estimatedPayloadBytes = DEFAULT_DELTA_PAYLOAD_BYTES,
            int lastSentTick = NetworkReplicatedObject.NEVER_SENT,
            ulong stateVersion = 0UL,
            ulong stateChecksum = 0UL)
        {
            if (networkId == 0u)
            {
                throw new ArgumentOutOfRangeException(nameof(networkId));
            }

            if (!position.IsFinite())
            {
                throw new ArgumentOutOfRangeException(nameof(position));
            }

            if (estimatedPayloadBytes < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(estimatedPayloadBytes));
            }

            NetworkId = networkId;
            Position = position;
            OwnerConnectionId = ownerConnectionId;
            OwnerPlayerId = ownerPlayerId;
            TeamId = teamId;
            InterestLayerMask = interestLayerMask;
            Policy = policy;
            ChangeMask = changeMask;
            RequiresFullState = requiresFullState;
            EstimatedPayloadBytes = estimatedPayloadBytes;
            LastSentTick = lastSentTick;
            StateVersion = stateVersion;
            StateChecksum = stateChecksum;
        }

        public bool IsDirty
        {
            get
            {
                return ChangeMask != GASReplicationChangeMask.None;
            }
        }

        public NetworkReplicatedObject ToReplicatedObject()
        {
            int payloadBytes = EstimatedPayloadBytes;
            if (RequiresFullState && payloadBytes < DEFAULT_FULL_STATE_PAYLOAD_BYTES)
            {
                payloadBytes = DEFAULT_FULL_STATE_PAYLOAD_BYTES;
            }

            return new NetworkReplicatedObject(
                NetworkId,
                Policy,
                Position,
                OwnerConnectionId,
                OwnerPlayerId,
                TeamId,
                InterestLayerMask,
                IsDirty,
                RequiresFullState,
                LastSentTick,
                payloadBytes);
        }
    }
}
