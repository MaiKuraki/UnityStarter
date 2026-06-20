namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementAuthorityTransferMessage
    {
        public ulong EntityId;
        public int PreviousAuthorityConnectionId;
        public int NextAuthorityConnectionId;
        public ulong PreviousAuthorityPlayerId;
        public ulong NextAuthorityPlayerId;
        public int ServerTick;
        public uint ReasonCode;
        public ulong ProtocolFingerprint;

        public MovementAuthorityTransferMessage(
            ulong entityId,
            int previousAuthorityConnectionId,
            int nextAuthorityConnectionId,
            ulong previousAuthorityPlayerId,
            ulong nextAuthorityPlayerId,
            int serverTick,
            uint reasonCode,
            ulong protocolFingerprint)
        {
            EntityId = entityId;
            PreviousAuthorityConnectionId = previousAuthorityConnectionId;
            NextAuthorityConnectionId = nextAuthorityConnectionId;
            PreviousAuthorityPlayerId = previousAuthorityPlayerId;
            NextAuthorityPlayerId = nextAuthorityPlayerId;
            ServerTick = serverTick;
            ReasonCode = reasonCode;
            ProtocolFingerprint = protocolFingerprint;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && NextAuthorityConnectionId >= 0
                       && ServerTick >= 0
                       && ProtocolFingerprint != 0UL;
            }
        }
    }
}
