using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Movement.Core;
using Unity.Mathematics;

namespace CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath
{
    /// <summary>
    /// Conversions between the float3 movement representation and the fixed-point deterministic one.
    /// </summary>
    /// <remarks>
    /// These helpers cross the float/fixed-point boundary and therefore are NOT part of the deterministic
    /// path. Use them only at handoff points (spawn, teleport, authority transfer, rendering interpolation),
    /// never between lockstep ticks: converting float to fixed-point quantizes the value and would diverge if
    /// done mid-simulation. World up is assumed +Y, matching <see cref="DeterministicMovementSimulator"/>.
    /// </remarks>
    public static class DeterministicMovementConversions
    {
        public static float3 ToFloat3(FPVector3 value)
        {
            return new float3(value.X.ToFloat(), value.Y.ToFloat(), value.Z.ToFloat());
        }

        public static FPVector3 ToFPVector3(float3 value)
        {
            return new FPVector3(
                FPInt64.FromFloat(value.x),
                FPInt64.FromFloat(value.y),
                FPInt64.FromFloat(value.z));
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
            float3 horizontal = ToFloat3(state.HorizontalVelocity);
            float verticalVelocity = state.VerticalVelocity.ToFloat();

            return new MovementSnapshot
            {
                Position = ToFloat3(state.Position),
                Velocity = new float3(horizontal.x, verticalVelocity, horizontal.z),
                WorldUp = new float3(0f, 1f, 0f),
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
                FPInt64.FromFloat(snapshot.Velocity.x),
                FPInt64.Zero,
                FPInt64.FromFloat(snapshot.Velocity.z));

            return new DeterministicMovementState(
                ToFPVector3(snapshot.Position),
                horizontalVelocity,
                FPInt64.FromFloat(snapshot.VerticalVelocity),
                snapshot.IsGrounded,
                snapshot.Tick);
        }
    }
}
