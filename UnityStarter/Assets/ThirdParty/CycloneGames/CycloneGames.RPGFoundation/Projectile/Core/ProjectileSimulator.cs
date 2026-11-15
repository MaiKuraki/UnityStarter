namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public static class ProjectileSimulator
    {
        private const float DIRECTION_EPSILON = 0.000001f;

        public static void Step(
            ref ProjectileState state,
            in ProjectileDefinition definition,
            in ProjectileSpaceProfile space,
            float deltaTime,
            int tick,
            bool hasTarget,
            in ProjectileVector3 targetPosition,
            in ProjectileVector3 targetVelocity)
        {
            state.PreviousPosition = state.Position;
            state.Velocity = ResolveVelocity(
                in state,
                in definition,
                in space,
                deltaTime,
                hasTarget,
                in targetPosition,
                in targetVelocity);
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
            in ProjectileVector3 targetPosition,
            in ProjectileVector3 targetVelocity)
        {
            ProjectileVector3 velocity = space.ProjectVector(state.Velocity);
            float speed = velocity.Length;
            ProjectileVector3 direction = speed > DIRECTION_EPSILON
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

        /// <summary>
        /// Bounded turn expressed as a chord-length budget between two unit directions.
        /// For small angles the chord length equals the angle in radians; clamping the
        /// budget to 2 makes a full reversal a single step. Avoids the transcendental
        /// call on every guided projectile every frame.
        /// </summary>
        private static ProjectileVector3 RotateTowards(
            ProjectileVector3 current,
            ProjectileVector3 target,
            float maxRadiansDelta)
        {
            current = current.NormalizedOrZero();
            target = target.NormalizedOrZero();
            if (current.LengthSquared <= DIRECTION_EPSILON || target.LengthSquared <= DIRECTION_EPSILON)
            {
                return current;
            }

            if (maxRadiansDelta <= 0f)
            {
                return current;
            }

            ProjectileVector3 delta = target - current;
            float chordSquared = delta.LengthSquared;
            float maxChord = maxRadiansDelta >= 2f ? 2f : maxRadiansDelta;
            if (chordSquared <= maxChord * maxChord)
            {
                return target;
            }

            float chord = (float)System.Math.Sqrt(chordSquared);
            return ProjectileVector3.Lerp(current, target, maxChord / chord).NormalizedOrFallback(current);
        }
    }
}
