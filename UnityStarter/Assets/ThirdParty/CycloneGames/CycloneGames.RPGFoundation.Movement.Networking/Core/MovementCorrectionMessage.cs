using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementFullStateMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public long ServerTick;
        public ushort Sequence;
        public MovementFullState State;

        public MovementFullStateMessage(
            ulong entityId,
            ulong epoch,
            long serverTick,
            ushort sequence,
            in MovementFullState state)
        {
            EntityId = entityId;
            Epoch = epoch;
            ServerTick = serverTick;
            Sequence = sequence;
            State = state;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && ServerTick >= 0L
                       && State.IsValid;
            }
        }

        public MovementNetworkSnapshotMessage ToDelta(byte flags)
        {
            MovementSnapshot delta = State.ToDelta(flags);
            return MovementNetworkSnapshotMessage.FromMovementSnapshot(
                EntityId, Epoch, delta, ServerTick, Sequence, flags);
        }
    }

    public struct MovementCorrectionMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public long CorrectedClientTick;
        public long ServerTick;
        public ushort InputSequence;
        public float PositionErrorSqr;
        public MovementFullStateMessage FullState;

        public MovementCorrectionMessage(
            ulong entityId,
            ulong epoch,
            long correctedClientTick,
            long serverTick,
            ushort inputSequence,
            float positionErrorSqr,
            in MovementFullStateMessage fullState)
        {
            EntityId = entityId;
            Epoch = epoch;
            CorrectedClientTick = correctedClientTick;
            ServerTick = serverTick;
            InputSequence = inputSequence;
            PositionErrorSqr = positionErrorSqr;
            FullState = fullState;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && CorrectedClientTick >= 0L
                       && ServerTick >= 0L
                       && float.IsFinite(PositionErrorSqr)
                       && PositionErrorSqr >= 0f
                       && FullState.IsValid
                       && FullState.EntityId == EntityId
                       && FullState.Epoch == Epoch;
            }
        }
    }
}
