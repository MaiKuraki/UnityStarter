using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public struct MovementTeleportMessage
    {
        public ulong EntityId;
        public ulong Epoch;
        public long ServerTick;
        public ushort TeleportSequence;
        public ushort StateId;
        public byte Flags;
        public uint ReasonCode;
        public NetworkVector3 Position;
        public NetworkVector3 Velocity;
        public NetworkVector3 WorldUp;

        public MovementTeleportMessage(
            ulong entityId,
            ulong epoch,
            long serverTick,
            ushort teleportSequence,
            ushort stateId,
            byte flags,
            uint reasonCode,
            NetworkVector3 position,
            NetworkVector3 velocity,
            NetworkVector3 worldUp)
        {
            EntityId = entityId;
            Epoch = epoch;
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
                       && Epoch != MovementWireIds.InvalidEpoch
                       && ServerTick >= 0L
                       && MovementStateDomain.IsDefined(StateId)
                       && MovementNetworkSnapshotFlags.Has(Flags, MovementNetworkSnapshotFlags.Teleport)
                       && MovementNetworkSnapshotFlags.HasOnlyKnownBits(Flags)
                       && Position.IsFinite()
                       && Velocity.IsFinite()
                       && WorldUp.IsFinite()
                       && WorldUp.SqrMagnitude >= 0.95f
                       && WorldUp.SqrMagnitude <= 1.05f;
            }
        }

        public float VerticalVelocity
        {
            get { return NetworkVector3.Dot(Velocity, WorldUp.Normalized); }
        }

        public MovementFullStateMessage ToFullStateMessage(long movementTick, double networkTimeSeconds)
        {
            MovementFullState state = default;
            state.NetworkTick = movementTick;
            state.NetworkTimeSeconds = networkTimeSeconds;
            state.Position = Position.ToMovementVector3();
            state.Velocity = Velocity.ToMovementVector3();
            state.WorldUp = WorldUp.ToMovementVector3();
            state.GroundNormal = state.WorldUp;
            state.StateId = StateId;
            state.VerticalVelocity = VerticalVelocity;
            state.JumpCount = 0;
            state.IsGrounded = false;
            state.WasGrounded = false;
            return new MovementFullStateMessage(EntityId, Epoch, ServerTick, TeleportSequence, state);
        }
    }
}
