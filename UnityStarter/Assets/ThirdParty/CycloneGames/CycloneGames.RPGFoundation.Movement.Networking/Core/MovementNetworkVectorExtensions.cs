using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkVectorExtensions
    {
        public static NetworkVector3 ToNetworkVector3(this float3 value)
        {
            return new NetworkVector3(value.x, value.y, value.z);
        }

        public static float3 ToFloat3(this NetworkVector3 value)
        {
            return new float3(value.X, value.Y, value.Z);
        }

        public static MovementNetworkSnapshotMessage ToMovementNetworkSnapshot(
            this MovementSnapshot snapshot,
            ulong entityId,
            int serverTick,
            ushort sequence = 0,
            byte flags = MovementNetworkSnapshotFlags.None)
        {
            byte snapshotFlags = MovementNetworkSnapshotFlags.Set(
                flags,
                MovementNetworkSnapshotFlags.Grounded,
                snapshot.IsGrounded);

            return new MovementNetworkSnapshotMessage(
                entityId,
                snapshot.Tick,
                serverTick,
                sequence,
                (ushort)snapshot.StateType,
                snapshotFlags,
                snapshot.JumpCount,
                snapshot.Timestamp,
                snapshot.VerticalVelocity,
                snapshot.Position.ToNetworkVector3(),
                snapshot.Velocity.ToNetworkVector3(),
                snapshot.WorldUp.ToNetworkVector3());
        }
    }
}
