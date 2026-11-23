using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath
{
    public static class DeterministicMovementConversions
    {
        public static MovementVector3 ToMovementVector3(FPVector3 value)
        {
            return new MovementVector3(value.X.ToFloat(), value.Y.ToFloat(), value.Z.ToFloat());
        }

        public static FPVector3 ToFPVector3(MovementVector3 value)
        {
            return new FPVector3(
                FPInt64.FromFloat(value.X),
                FPInt64.FromFloat(value.Y),
                FPInt64.FromFloat(value.Z));
        }

        public static MovementSnapshot ToSnapshot(
            in DeterministicMovementState state,
            MovementStateType stateType = MovementStateType.Idle,
            float timestamp = 0f)
        {
            MovementVector3 horizontal = ToMovementVector3(state.HorizontalVelocity);
            float verticalVelocity = state.VerticalVelocity.ToFloat();

            MovementSnapshot snapshot = default;
            snapshot.Position = ToMovementVector3(state.Position);
            snapshot.Velocity = new MovementVector3(horizontal.X, verticalVelocity, horizontal.Z);
            snapshot.WorldUp = new MovementVector3(0f, 1f, 0f);
            snapshot.StateId = (ushort)stateType;
            snapshot.Flags = 0;
            snapshot.VerticalVelocity = verticalVelocity;
            snapshot.IsGrounded = state.IsGrounded;
            snapshot.JumpCount = 0;
            snapshot.NetworkTick = state.Tick;
            snapshot.NetworkTimeSeconds = timestamp;
            return snapshot;
        }

        public static DeterministicMovementState ToDeterministicState(in MovementSnapshot snapshot)
        {
            var horizontalVelocity = new FPVector3(
                FPInt64.FromFloat(snapshot.Velocity.X),
                FPInt64.Zero,
                FPInt64.FromFloat(snapshot.Velocity.Z));

            long tick = snapshot.NetworkTick;
            if (tick > int.MaxValue) tick = int.MaxValue;
            if (tick < int.MinValue) tick = int.MinValue;

            return new DeterministicMovementState(
                ToFPVector3(snapshot.Position),
                horizontalVelocity,
                FPInt64.FromFloat(snapshot.VerticalVelocity),
                snapshot.IsGrounded,
                (int)tick);
        }
    }
}
