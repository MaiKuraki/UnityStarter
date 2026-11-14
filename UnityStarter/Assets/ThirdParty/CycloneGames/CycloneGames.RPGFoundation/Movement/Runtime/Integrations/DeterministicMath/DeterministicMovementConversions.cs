using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath
{
    /// <summary>
    /// Conversions between the engine-independent float movement representation and the fixed-point one.
    /// </summary>
    /// <remarks>
    /// These helpers cross the float/fixed-point boundary and therefore are NOT part of the deterministic
    /// path. Use them only at handoff points (spawn, teleport, authority transfer, rendering interpolation),
    /// never between lockstep ticks: converting float to fixed-point quantizes the value and would diverge if
    /// done mid-simulation. World up is assumed +Y, matching <see cref="DeterministicMovementSimulator"/>.
    /// </remarks>
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

        /// <summary>
        /// Projects a deterministic state into the engine-facing <see cref="MovementSnapshot"/>. The combined
        /// <see cref="MovementSnapshot.Velocity"/> is reconstructed from horizontal velocity plus vertical
        /// velocity along +Y.
        /// </summary>
        public static MovementSnapshot ToSnapshot(
            in DeterministicMovementState state,
            MovementStateType stateType = MovementStateType.Idle,
            float timestamp = 0f)
        {
            MovementVector3 horizontal = ToMovementVector3(state.HorizontalVelocity);
            float verticalVelocity = state.VerticalVelocity.ToFloat();

            return new MovementSnapshot
            {
                Position = ToMovementVector3(state.Position),
                Velocity = new MovementVector3(horizontal.X, verticalVelocity, horizontal.Z),
                WorldUp = new MovementVector3(0f, 1f, 0f),
                StateType = stateType,
                VerticalVelocity = verticalVelocity,
                IsGrounded = state.IsGrounded,
                JumpCount = 0,
                Tick = state.Tick,
                Timestamp = timestamp
            };
        }

        /// <summary>
        /// Builds a deterministic state from an engine-facing snapshot. The horizontal velocity is taken from
        /// the snapshot's XZ velocity; vertical velocity is taken from <see cref="MovementSnapshot.VerticalVelocity"/>.
        /// </summary>
        public static DeterministicMovementState ToDeterministicState(in MovementSnapshot snapshot)
        {
            var horizontalVelocity = new FPVector3(
                FPInt64.FromFloat(snapshot.Velocity.X),
                FPInt64.Zero,
                FPInt64.FromFloat(snapshot.Velocity.Z));

            return new DeterministicMovementState(
                ToFPVector3(snapshot.Position),
                horizontalVelocity,
                FPInt64.FromFloat(snapshot.VerticalVelocity),
                snapshot.IsGrounded,
                snapshot.Tick);
        }
    }
}
