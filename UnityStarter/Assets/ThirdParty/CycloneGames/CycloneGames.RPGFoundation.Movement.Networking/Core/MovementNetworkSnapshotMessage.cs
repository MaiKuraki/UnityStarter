using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementNetworkSnapshotMessage
    {
        public ulong EntityId;
        public int Tick;
        public int ServerTick;
        public ushort Sequence;
        public ushort StateId;
        public byte Flags;
        public int JumpCount;
        public float Timestamp;
        public float VerticalVelocity;
        public NetworkVector3 Position;
        public NetworkVector3 Velocity;
        public NetworkVector3 WorldUp;

        public MovementNetworkSnapshotMessage(
            ulong entityId,
            int tick,
            int serverTick,
            ushort sequence,
            ushort stateId,
            byte flags,
            int jumpCount,
            float timestamp,
            float verticalVelocity,
            NetworkVector3 position,
            NetworkVector3 velocity,
            NetworkVector3 worldUp)
        {
            EntityId = entityId;
            Tick = tick;
            ServerTick = serverTick;
            Sequence = sequence;
            StateId = stateId;
            Flags = flags;
            JumpCount = jumpCount;
            Timestamp = timestamp;
            VerticalVelocity = verticalVelocity;
            Position = position;
            Velocity = velocity;
            WorldUp = worldUp;
        }

        public bool IsValid
        {
            get
            {
                return EntityId != 0UL
                       && Tick >= 0
                       && ServerTick >= 0
                       && JumpCount >= 0
                       && float.IsFinite(Timestamp)
                       && float.IsFinite(VerticalVelocity)
                       && Position.IsFinite()
                       && Velocity.IsFinite()
                       && WorldUp.IsFinite();
            }
        }

        public bool IsGrounded => MovementNetworkSnapshotFlags.Has(Flags, MovementNetworkSnapshotFlags.Grounded);

        public bool IsTeleport => MovementNetworkSnapshotFlags.Has(Flags, MovementNetworkSnapshotFlags.Teleport);

        public MovementSnapshot ToMovementSnapshot()
        {
            return new MovementSnapshot
            {
                Position = Position.ToFloat3(),
                Velocity = Velocity.ToFloat3(),
                WorldUp = WorldUp.ToFloat3(),
                StateType = (MovementStateType)StateId,
                VerticalVelocity = VerticalVelocity,
                IsGrounded = IsGrounded,
                JumpCount = JumpCount,
                Tick = Tick,
                Timestamp = Timestamp
            };
        }

        public static MovementNetworkSnapshotMessage FromMovementSnapshot(
            ulong entityId,
            in MovementSnapshot snapshot,
            int serverTick,
            ushort sequence = 0,
            byte flags = MovementNetworkSnapshotFlags.None)
        {
            return snapshot.ToMovementNetworkSnapshot(entityId, serverTick, sequence, flags);
        }
    }
}
