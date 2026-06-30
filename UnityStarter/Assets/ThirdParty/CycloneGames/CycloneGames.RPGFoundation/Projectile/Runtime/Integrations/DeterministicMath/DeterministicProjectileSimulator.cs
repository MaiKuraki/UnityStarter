using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath
{
    public static class DeterministicProjectileSimulator
    {
        public static DeterministicProjectileState Step(
            in DeterministicProjectileState state,
            in DeterministicProjectileDefinition definition,
            in DeterministicProjectileInput input,
            int tick)
        {
            FPVector3 velocity = ResolveVelocity(in state, in definition, in input);
            FPVector3 position = state.Position + velocity * input.DeltaTime;

            return new DeterministicProjectileState(
                state.NetworkEntityId,
                state.OwnerEntityId,
                state.TargetEntityId,
                state.DefinitionId,
                state.LifecycleFlags,
                state.SpawnTick,
                tick,
                state.PredictionKey,
                state.Seed,
                state.Age + input.DeltaTime,
                state.Radius,
                position,
                state.Position,
                velocity);
        }

        private static FPVector3 ResolveVelocity(
            in DeterministicProjectileState state,
            in DeterministicProjectileDefinition definition,
            in DeterministicProjectileInput input)
        {
            FPVector3 velocity = state.Velocity;
            FPInt64 speed = velocity.Magnitude;
            FPVector3 direction = speed.RawValue > 0
                ? velocity / speed
                : FPVector3.Forward;

            if (input.HasTarget
                && (definition.GuidanceMode == ProjectileGuidanceMode.Homing
                    || definition.GuidanceMode == ProjectileGuidanceMode.LeadHoming))
            {
                FPVector3 aimPoint = input.TargetPosition;
                if (definition.GuidanceMode == ProjectileGuidanceMode.LeadHoming)
                {
                    aimPoint += input.TargetVelocity * definition.LeadPredictionTime;
                }

                FPVector3 desiredDirection = (aimPoint - state.Position).Normalized;
                direction = MoveTowardsDirection(
                    direction,
                    desiredDirection,
                    definition.TurnRate * input.DeltaTime);
            }

            if (definition.Acceleration.RawValue != 0)
            {
                speed += definition.Acceleration * input.DeltaTime;
            }

            if (definition.MaxSpeed.RawValue > 0 && speed > definition.MaxSpeed)
            {
                speed = definition.MaxSpeed;
            }

            return direction * speed + definition.Gravity * input.DeltaTime;
        }

        private static FPVector3 MoveTowardsDirection(
            FPVector3 current,
            FPVector3 target,
            FPInt64 maxDelta)
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
            return (current + delta * (maxDelta / distance)).Normalized;
        }
    }
}
