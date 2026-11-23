using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkVectorExtensions
    {
        public static NetworkVector3 ToNetworkVector3(this MovementVector3 value)
        {
            return new NetworkVector3(value.X, value.Y, value.Z);
        }

        public static MovementVector3 ToMovementVector3(this NetworkVector3 value)
        {
            return new MovementVector3(value.X, value.Y, value.Z);
        }

        public static MovementNetworkSnapshotMessage ToMovementNetworkSnapshot(
            this MovementSnapshot snapshot,
            ulong entityId,
            ulong epoch,
            long serverTick,
            ushort sequence = 0,
            byte flags = MovementNetworkSnapshotFlags.None)
        {
            byte wireFlags = MovementNetworkSnapshotFlags.Wire(flags);
            wireFlags = MovementNetworkSnapshotFlags.Set(
                wireFlags,
                MovementNetworkSnapshotFlags.Grounded,
                snapshot.IsGrounded);

            return new MovementNetworkSnapshotMessage(
                entityId,
                epoch,
                snapshot.NetworkTick,
                serverTick,
                sequence,
                snapshot.StateId,
                wireFlags,
                snapshot.JumpCount,
                snapshot.VerticalVelocity,
                snapshot.NetworkTimeSeconds,
                snapshot.Position.ToNetworkVector3(),
                snapshot.Velocity.ToNetworkVector3(),
                snapshot.WorldUp.ToNetworkVector3());
        }
    }
}
