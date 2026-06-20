namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementCorrectionMessage
    {
        public ulong EntityId;
        public int CorrectedClientTick;
        public int ServerTick;
        public ushort InputSequence;
        public float PositionErrorSqr;
        public MovementNetworkSnapshotMessage Snapshot;

        public MovementCorrectionMessage(
            ulong entityId,
            int correctedClientTick,
            int serverTick,
            ushort inputSequence,
            float positionErrorSqr,
            MovementNetworkSnapshotMessage snapshot)
        {
            EntityId = entityId;
            CorrectedClientTick = correctedClientTick;
            ServerTick = serverTick;
            InputSequence = inputSequence;
            PositionErrorSqr = positionErrorSqr;
            Snapshot = snapshot;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && CorrectedClientTick >= 0
                       && ServerTick >= 0
                       && PositionErrorSqr >= 0f
                       && float.IsFinite(PositionErrorSqr)
                       && Snapshot.IsValid
                       && Snapshot.EntityId == EntityId;
            }
        }
    }
}
