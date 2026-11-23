using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementRemoteSampleAdapter
    {
        public static bool TryConvert(in MovementNetworkSnapshotMessage message,
            out MovementRemoteSample sample)
        {
            sample = default;
            if (message.ServerTick <= 0L) return false;
            if (double.IsNaN(message.NetworkTimeSeconds)
                || double.IsInfinity(message.NetworkTimeSeconds)
                || message.NetworkTimeSeconds < 0.0) return false;
            if (message.StateId > (ushort)MovementStateType.Fly) return false;

            MovementVector3 position = message.Position.ToMovementVector3();
            MovementVector3 velocity = message.Velocity.ToMovementVector3();
            MovementVector3 worldUp = message.WorldUp.ToMovementVector3();
            if (!position.IsFinite || !velocity.IsFinite || !worldUp.IsFinite) return false;

            MovementAnimationFlags flags = MovementAnimationFlags.None;
            if ((message.Flags & MovementNetworkSnapshotFlags.Grounded) != 0)
                flags |= MovementAnimationFlags.Grounded;

            MovementVector3 facing = velocity;
            float planarLength = new MovementVector3(facing.X, 0f, facing.Z).Length;
            facing = planarLength > 0.0001f
                ? facing * (1f / planarLength)
                : MovementVector3.Zero;

            sample = new MovementRemoteSample(
                message.ServerTick,
                message.NetworkTimeSeconds,
                position,
                velocity,
                facing,
                (MovementStateType)message.StateId,
                flags);
            return sample.IsValid;
        }
    }
}
