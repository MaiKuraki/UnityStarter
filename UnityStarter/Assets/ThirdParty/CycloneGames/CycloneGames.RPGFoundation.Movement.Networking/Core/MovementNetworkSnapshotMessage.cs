using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementNetworkSnapshotMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public long Tick;
        public long ServerTick;
        public ushort Sequence;
        public ushort StateId;
        public byte Flags;
        public byte JumpCount;
        public float VerticalVelocity;
        public double NetworkTimeSeconds;
        public NetworkVector3 Position;
        public NetworkVector3 Velocity;
        public NetworkVector3 WorldUp;

        public MovementNetworkSnapshotMessage(
            ulong entityId,
            ulong epoch,
            long tick,
            long serverTick,
            ushort sequence,
            ushort stateId,
            byte flags,
            byte jumpCount,
            float verticalVelocity,
            double networkTimeSeconds,
            NetworkVector3 position,
            NetworkVector3 velocity,
            NetworkVector3 worldUp)
        {
            EntityId = entityId;
            Epoch = epoch;
            Tick = tick;
            ServerTick = serverTick;
            Sequence = sequence;
            StateId = stateId;
            Flags = flags;
            JumpCount = jumpCount;
            VerticalVelocity = verticalVelocity;
            NetworkTimeSeconds = networkTimeSeconds;
            Position = position;
            Velocity = velocity;
            WorldUp = worldUp;
        }

        public bool IsValid
        {
            get
            {
                return IsWellFormed
                       && (Flags & MovementNetworkSnapshotFlags.WireMask) == Flags;
            }
        }

        public bool IsWellFormed
        {
            get
            {
                return EntityId != 0UL
                       && Epoch != MovementWireIds.InvalidEpoch
                       && Tick >= 0L
                       && ServerTick >= 0L
                       && MovementStateDomain.IsDefined(StateId)
                       && JumpCount <= MovementWireIds.MaximumJumpCount
                       && MovementNetworkSnapshotFlags.HasOnlyKnownBits(Flags)
                       && float.IsFinite(VerticalVelocity)
                       && MovementSnapshot.IsFiniteNonNegative(NetworkTimeSeconds)
                       && Position.IsFinite()
                       && Velocity.IsFinite()
                       && WorldUp.IsFinite()
                       && IsUnitWorldUp(WorldUp);
            }
        }

        public bool IsGrounded
        {
            get { return MovementNetworkSnapshotFlags.Has(Flags, MovementNetworkSnapshotFlags.Grounded); }
        }

        public bool IsTeleport
        {
            get { return MovementNetworkSnapshotFlags.Has(Flags, MovementNetworkSnapshotFlags.Teleport); }
        }

        public MovementSnapshot ToMovementSnapshot()
        {
            MovementSnapshot snapshot = default;
            snapshot.Position = Position.ToMovementVector3();
            snapshot.Velocity = Velocity.ToMovementVector3();
            snapshot.WorldUp = WorldUp.ToMovementVector3();
            snapshot.StateId = StateId;
            snapshot.Flags = MovementNetworkSnapshotFlags.Wire(Flags);
            snapshot.JumpCount = JumpCount;
            snapshot.VerticalVelocity = VerticalVelocity;
            snapshot.IsGrounded = IsGrounded;
            snapshot.NetworkTick = Tick;
            snapshot.NetworkTimeSeconds = NetworkTimeSeconds;
            return snapshot;
        }

        public static MovementNetworkSnapshotMessage FromMovementSnapshot(
            ulong entityId,
            ulong epoch,
            in MovementSnapshot snapshot,
            long serverTick,
            ushort sequence = 0,
            byte flags = MovementNetworkSnapshotFlags.None)
        {
            return snapshot.ToMovementNetworkSnapshot(entityId, epoch, serverTick, sequence, flags);
        }

        private static bool IsUnitWorldUp(NetworkVector3 value)
        {
            float sqr = value.SqrMagnitude;
            return sqr >= 0.95f && sqr <= 1.05f;
        }
    }
}
