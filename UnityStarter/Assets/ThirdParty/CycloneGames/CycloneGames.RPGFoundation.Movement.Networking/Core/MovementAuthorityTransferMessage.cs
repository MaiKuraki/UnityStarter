namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementAuthorityTransferMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public ulong ProtocolFingerprint;
        public uint TransferSequence;
        public long ServerTick;
        public int PreviousAuthorityConnectionId;
        public int NextAuthorityConnectionId;
        public ulong PreviousAuthorityPlayerId;
        public ulong NextAuthorityPlayerId;
        public uint ReasonCode;

        public MovementAuthorityTransferMessage(
            ulong entityId,
            ulong epoch,
            ulong protocolFingerprint,
            uint transferSequence,
            long serverTick,
            int previousAuthorityConnectionId,
            int nextAuthorityConnectionId,
            ulong previousAuthorityPlayerId,
            ulong nextAuthorityPlayerId,
            uint reasonCode)
        {
            EntityId = entityId;
            Epoch = epoch;
            ProtocolFingerprint = protocolFingerprint;
            TransferSequence = transferSequence;
            ServerTick = serverTick;
            PreviousAuthorityConnectionId = previousAuthorityConnectionId;
            NextAuthorityConnectionId = nextAuthorityConnectionId;
            PreviousAuthorityPlayerId = previousAuthorityPlayerId;
            NextAuthorityPlayerId = nextAuthorityPlayerId;
            ReasonCode = reasonCode;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && ProtocolFingerprint != MovementWireIds.InvalidFingerprint
                       && TransferSequence != 0U
                       && ServerTick >= 0L
                       && NextAuthorityConnectionId >= 0
                       && NextAuthorityPlayerId != 0UL;
            }
        }

        public bool IsAcceptableFor(ulong localEpoch, ulong localFingerprint, uint lastAppliedSequence)
        {
            if (!IsValid) return false;
            if (Epoch != localEpoch || ProtocolFingerprint != localFingerprint) return false;
            uint delta = unchecked(TransferSequence - lastAppliedSequence);
            return delta != 0U && delta < 0x80000000U;
        }
    }
}
