using CycloneGames.DeterministicMath;

namespace CycloneGames.RPGFoundation.Movement.Integrations.DeterministicMath
{
    /// <summary>
    /// Bit-deterministic kinematic movement integrator. Given a state, an input and a config it advances the
    /// state by one tick using only fixed-point arithmetic, so every platform computes identical results —
    /// the property lockstep and rollback netcode depend on.
    /// </summary>
    /// <remarks>
    /// <para>
    /// Scope: this core integrates horizontal acceleration toward a target velocity, gravity, jumping and
    /// vertical motion against a single deterministic flat ground plane at <see cref="DeterministicMovementConfig.GroundHeight"/>.
    /// It performs no collision queries against arbitrary geometry; deterministic world collision is a
    /// separate concern and intentionally out of scope here.
    /// </para>
    /// <para>
    /// The method is pure and allocation-free. World up is +Y by convention; the horizontal plane is XZ.
    /// </para>
    /// </remarks>
    public static class DeterministicMovementSimulator
    {
        public static DeterministicMovementState Step(
            in DeterministicMovementState state,
            in DeterministicMovementInput input,
            in DeterministicMovementConfig config)
        {
            FPInt64 dt = input.DeltaTime;

            FPVector3 horizontalVelocity = ResolveHorizontalVelocity(in state, in input, in config, dt);
            FPInt64 verticalVelocity = ResolveVerticalVelocity(in state, in input, in config, dt);

            FPVector3 newPosition = new FPVector3(
                state.Position.X + horizontalVelocity.X * dt,
                state.Position.Y + verticalVelocity * dt,
                state.Position.Z + horizontalVelocity.Z * dt);

            bool isGrounded;
            if (newPosition.Y <= config.GroundHeight)
            {
                newPosition = new FPVector3(newPosition.X, config.GroundHeight, newPosition.Z);
                if (verticalVelocity < FPInt64.Zero)
                {
                    verticalVelocity = FPInt64.Zero;
                }

                isGrounded = true;
            }
            else
            {
                isGrounded = false;
            }

            return new DeterministicMovementState(
                newPosition,
                horizontalVelocity,
                verticalVelocity,
                isGrounded,
                state.Tick + 1);
        }

        private static FPVector3 ResolveHorizontalVelocity(
            in DeterministicMovementState state,
            in DeterministicMovementInput input,
            in DeterministicMovementConfig config,
            FPInt64 dt)
        {
            FPVector3 moveDirection = Horizontalize(input.MoveDirection);

            if (moveDirection.SqrMagnitude.RawValue == 0)
            {
                // No horizontal intent: bleed off speed on the ground; preserve momentum in the air.
                if (state.IsGrounded)
                {
                    return MoveTowards(state.HorizontalVelocity, FPVector3.Zero, config.GroundDeceleration * dt);
                }

                return state.HorizontalVelocity;
            }

            FPVector3 targetVelocity = ClampMagnitude(moveDirection * config.MaxHorizontalSpeed, config.MaxHorizontalSpeed);
            FPInt64 acceleration = state.IsGrounded ? config.GroundAcceleration : config.AirAcceleration;
            return MoveTowards(state.HorizontalVelocity, targetVelocity, acceleration * dt);
        }

        private static FPInt64 ResolveVerticalVelocity(
            in DeterministicMovementState state,
            in DeterministicMovementInput input,
            in DeterministicMovementConfig config,
            FPInt64 dt)
        {
            if (input.JumpRequested && state.IsGrounded)
            {
                return config.JumpSpeed;
            }

            FPInt64 verticalVelocity = state.VerticalVelocity + config.Gravity * dt;
            if (verticalVelocity < config.MaxFallSpeed)
            {
                verticalVelocity = config.MaxFallSpeed;
            }

            return verticalVelocity;
        }

        private static FPVector3 Horizontalize(FPVector3 v)
        {
            return new FPVector3(v.X, FPInt64.Zero, v.Z);
        }

        private static FPVector3 MoveTowards(FPVector3 current, FPVector3 target, FPInt64 maxDelta)
        {
            if (maxDelta.RawValue <= 0)
            {
                return current;
            }

            FPVector3 delta = target - current;
            FPInt64 sqrMagnitude = delta.SqrMagnitude;
            if (sqrMagnitude.RawValue == 0)
            {
                return target;
            }

            if (sqrMagnitude <= maxDelta * maxDelta)
            {
                return target;
            }

            FPInt64 distance = FPInt64.Sqrt(sqrMagnitude);
            return current + delta * (maxDelta / distance);
        }

        private static FPVector3 ClampMagnitude(FPVector3 v, FPInt64 maxMagnitude)
        {
            FPInt64 sqrMagnitude = v.SqrMagnitude;
            FPInt64 maxSqr = maxMagnitude * maxMagnitude;
            if (sqrMagnitude <= maxSqr)
            {
                return v;
            }

            FPInt64 magnitude = FPInt64.Sqrt(sqrMagnitude);
            if (magnitude.RawValue == 0)
            {
                return FPVector3.Zero;
            }

            return v * (maxMagnitude / magnitude);
        }
    }
}
