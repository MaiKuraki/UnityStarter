using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementInputCommandMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public long ClientTick;
        public long LastReceivedServerTick;
        public ushort InputSequence;
        public int PredictionKey;
        public uint ButtonMask;
        public uint CustomFlags;
        public float DeltaTime;
        public NetworkVector3 MoveAxes;
        public NetworkVector3 AimDirection;

        public MovementInputCommandMessage(
            ulong entityId,
            ulong epoch,
            long clientTick,
            long lastReceivedServerTick,
            ushort inputSequence,
            uint buttonMask,
            uint customFlags,
            float deltaTime,
            NetworkVector3 moveAxes,
            NetworkVector3 aimDirection,
            int predictionKey = 0)
        {
            EntityId = entityId;
            Epoch = epoch;
            ClientTick = clientTick;
            LastReceivedServerTick = lastReceivedServerTick;
            InputSequence = inputSequence;
            PredictionKey = predictionKey;
            ButtonMask = buttonMask;
            CustomFlags = customFlags;
            DeltaTime = deltaTime;
            MoveAxes = moveAxes;
            AimDirection = aimDirection;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && ClientTick >= 0L
                       && LastReceivedServerTick >= 0L
                       && LastReceivedServerTick <= ClientTick
                       && float.IsFinite(DeltaTime)
                       && DeltaTime >= 0f
                       && DeltaTime <= MovementWireLimits.MaxInputDeltaTime
                       && MoveAxes.IsFinite()
                       && AimDirection.IsFinite()
                       && MoveAxes.SqrMagnitude <= 1.5625f
                       && AimDirection.SqrMagnitude <= 1.5625f;
            }
        }

        public bool HasButton(uint buttonMask)
        {
            return (ButtonMask & buttonMask) == buttonMask;
        }
    }

    public struct MovementInputBatchMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public byte Count;
        public MovementInputCommandMessage Command0;
        public MovementInputCommandMessage Command1;
        public MovementInputCommandMessage Command2;
        public MovementInputCommandMessage Command3;

        public MovementInputBatchMessage(
            ulong entityId,
            ulong epoch,
            byte count,
            in MovementInputCommandMessage command0,
            in MovementInputCommandMessage command1 = default,
            in MovementInputCommandMessage command2 = default,
            in MovementInputCommandMessage command3 = default)
        {
            EntityId = entityId;
            Epoch = epoch;
            Count = count;
            Command0 = command0;
            Command1 = command1;
            Command2 = command2;
            Command3 = command3;
        }

        public bool IsValid
        {
            get
            {
                if (EntityId == 0UL || Epoch == MovementWireIds.InvalidEpoch
                    || Count < 1 || Count > MovementWireLimits.MaxInputBatchCount)
                    return false;

                if (!ValidMember(Command0)) return false;
                if (Count > 1 && !ValidMember(Command1)) return false;
                if (Count > 2 && !ValidMember(Command2)) return false;
                if (Count > 3 && !ValidMember(Command3)) return false;

                if (Count > 1 && (Command1.ClientTick <= Command0.ClientTick || Command1.Epoch != Epoch)) return false;
                if (Count > 2 && (Command2.ClientTick <= Command1.ClientTick || Command2.Epoch != Epoch)) return false;
                if (Count > 3 && (Command3.ClientTick <= Command2.ClientTick || Command3.Epoch != Epoch)) return false;
                return true;
            }
        }

        private bool ValidMember(in MovementInputCommandMessage command)
        {
            return command.IsValid && command.EntityId == EntityId && command.Epoch == Epoch;
        }

        public MovementInputCommandMessage Newest
        {
            get
            {
                if (Count == 1) return Command0;
                if (Count == 2) return Command1;
                if (Count == 3) return Command2;
                return Command3;
            }
        }

        public MovementInputCommandMessage Oldest
        {
            get { return Command0; }
        }
    }

    public struct MovementInputAckMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public long ServerTick;
        public long AckedClientTick;
        public ushort AckedInputSequence;
        public bool GoodMove;

        public MovementInputAckMessage(
            ulong entityId,
            ulong epoch,
            long serverTick,
            long ackedClientTick,
            ushort ackedInputSequence,
            bool goodMove)
        {
            EntityId = entityId;
            Epoch = epoch;
            ServerTick = serverTick;
            AckedClientTick = ackedClientTick;
            AckedInputSequence = ackedInputSequence;
            GoodMove = goodMove;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && ServerTick >= 0L
                       && AckedClientTick >= 0L;
            }
        }
    }
}
