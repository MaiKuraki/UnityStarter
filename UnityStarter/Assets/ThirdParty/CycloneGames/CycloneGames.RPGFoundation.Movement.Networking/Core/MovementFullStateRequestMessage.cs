namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementFullStateRequestMessage
    {
        public ulong EntityId;
        public int LastKnownTick;
        public ushort RequestSequence;
        public uint ReasonCode;

        public MovementFullStateRequestMessage(
            ulong entityId,
            int lastKnownTick,
            ushort requestSequence,
            uint reasonCode)
        {
            EntityId = entityId;
            LastKnownTick = lastKnownTick;
            RequestSequence = requestSequence;
            ReasonCode = reasonCode;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL && LastKnownTick >= 0;
            }
        }
    }
}
