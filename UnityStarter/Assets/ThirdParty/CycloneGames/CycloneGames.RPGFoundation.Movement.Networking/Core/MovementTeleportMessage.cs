using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementTeleportMessage
    {
        public ulong EntityId;
        public int ServerTick;
        public ushort TeleportSequence;
        public ushort StateId;
        public byte Flags;
        public uint ReasonCode;
        public NetworkVector3 Position;
        public NetworkVector3 Velocity;
        public NetworkVector3 WorldUp;

        public MovementTeleportMessage(
            ulong entityId,
            int serverTick,
            ushort teleportSequence,
            ushort stateId,
            byte flags,
            uint reasonCode,
            NetworkVector3 position,
            NetworkVector3 velocity,
            NetworkVector3 worldUp)
        {
            EntityId = entityId;
            ServerTick = serverTick;
            TeleportSequence = teleportSequence;
            StateId = stateId;
            Flags = MovementNetworkSnapshotFlags.Set(flags, MovementNetworkSnapshotFlags.Teleport, true);
            ReasonCode = reasonCode;
            Position = position;
            Velocity = velocity;
            WorldUp = worldUp;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && ServerTick >= 0
                       && Position.IsFinite()
                       && Velocity.IsFinite()
                       && WorldUp.IsFinite()
                       && MovementNetworkSnapshotFlags.Has(Flags, MovementNetworkSnapshotFlags.Teleport);
            }
        }

        public MovementNetworkSnapshotMessage ToSnapshotMessage(int movementTick, float timestamp = 0f)
        {
            return new MovementNetworkSnapshotMessage(
                EntityId,
                movementTick,
                ServerTick,
                TeleportSequence,
                StateId,
                Flags,
                0,
                timestamp,
                Velocity.Y,
                Position,
                Velocity,
                WorldUp);
        }
    }
}
