namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementFullStateRequestMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public int RequesterConnectionId;
        public ulong RequesterPlayerId;
        public long LastKnownTick;
        public ushort RequestSequence;
        public uint ReasonCode;

        public MovementFullStateRequestMessage(
            ulong entityId,
            ulong epoch,
            int requesterConnectionId,
            ulong requesterPlayerId,
            long lastKnownTick,
            ushort requestSequence,
            uint reasonCode)
        {
            EntityId = entityId;
            Epoch = epoch;
            RequesterConnectionId = requesterConnectionId;
            RequesterPlayerId = requesterPlayerId;
            LastKnownTick = lastKnownTick;
            RequestSequence = requestSequence;
            ReasonCode = reasonCode;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && RequesterConnectionId > 0
                       && RequesterPlayerId != 0UL
                       && LastKnownTick >= 0L;
            }
        }
    }
}
