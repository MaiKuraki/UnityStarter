using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public static class ProjectileSimulator
    {
        public static void Step(
            ref ProjectileState state,
            in ProjectileDefinition definition,
            in ProjectileSpaceProfile space,
            float deltaTime,
            int tick,
            bool hasTarget,
            ProjectileVector3 targetPosition,
            ProjectileVector3 targetVelocity)
        {
            state.PreviousPosition = state.Position;
            state.Velocity = ResolveVelocity(
                in state,
                in definition,
                in space,
                deltaTime,
                hasTarget,
                targetPosition,
                targetVelocity);
            state.Position = space.ProjectPosition(state.Position + state.Velocity * deltaTime);
            state.Age += deltaTime;
            state.CurrentTick = tick;
        }

        private static ProjectileVector3 ResolveVelocity(
            in ProjectileState state,
            in ProjectileDefinition definition,
            in ProjectileSpaceProfile space,
            float deltaTime,
            bool hasTarget,
            ProjectileVector3 targetPosition,
            ProjectileVector3 targetVelocity)
        {
            ProjectileVector3 velocity = space.ProjectVector(state.Velocity);
            float speed = velocity.Length;
            ProjectileVector3 direction = speed > 0.000001f
                ? velocity / speed
                : ProjectileVector3.Forward;

            if (hasTarget
                && (definition.GuidanceMode == ProjectileGuidanceMode.Homing
                    || definition.GuidanceMode == ProjectileGuidanceMode.LeadHoming))
            {
                ProjectileVector3 aimPoint = targetPosition;
                if (definition.GuidanceMode == ProjectileGuidanceMode.LeadHoming)
                {
                    aimPoint += targetVelocity * definition.LeadPredictionTime;
                }

                ProjectileVector3 desiredDirection = space.ProjectDirection(aimPoint - state.Position);
                direction = RotateTowards(direction, desiredDirection, definition.TurnRateRadiansPerSecond * deltaTime);
            }

            if (definition.Acceleration != 0f)
            {
                speed += definition.Acceleration * deltaTime;
            }

            if (definition.MaxSpeed > 0f && speed > definition.MaxSpeed)
            {
                speed = definition.MaxSpeed;
            }

            velocity = direction * speed;
            if (definition.GravityScale != 0f)
            {
                velocity += space.ProjectVector(space.Gravity) * definition.GravityScale * deltaTime;
            }

            return space.ProjectVector(velocity);
        }

        private static ProjectileVector3 RotateTowards(
            ProjectileVector3 current,
            ProjectileVector3 target,
            float maxRadiansDelta)
        {
            current = current.NormalizedOrZero();
            target = target.NormalizedOrZero();
            if (current.LengthSquared <= 0.000001f || target.LengthSquared <= 0.000001f)
            {
                return current;
            }

            float dot = Clamp(ProjectileVector3.Dot(current, target), -1f, 1f);
            float angle = (float)Math.Acos(dot);
            if (angle <= 0.000001f || maxRadiansDelta >= angle)
            {
                return target;
            }

            float t = maxRadiansDelta / angle;
            return ProjectileVector3.Lerp(current, target, t).NormalizedOrFallback(current);
        }

        private static float Clamp(float value, float min, float max)
        {
            if (value < min)
            {
                return min;
            }

            return value > max ? max : value;
        }
    }
}
