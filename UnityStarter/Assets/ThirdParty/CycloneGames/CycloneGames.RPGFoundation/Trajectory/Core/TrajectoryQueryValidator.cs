namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public static class TrajectoryQueryValidator
    {
        public const int RECOMMENDED_ISSUE_CAPACITY = 16;
        private const float DIRECTION_EPSILON = 0.000001f;

        public static int Validate(
            in TrajectoryQuery query,
            TrajectoryQueryValidationIssue[] issues)
        {
            int count = 0;

            if (query.CollisionLayerMask == 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.EmptyCollisionLayerMask,
                    "Collision Layer Mask is empty; the Core query is invalid.");
            }

            if (!query.Origin.IsFinite)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.NonFiniteOrigin,
                    "Origin must be finite.");
            }

            if (!query.Direction.IsFinite)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.NonFiniteDirection,
                    "Direction must be finite.");
            }

            if (query.Direction.LengthSquared <= DIRECTION_EPSILON)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.DegenerateDirection,
                    "Direction must not be zero.");
            }

            if (!IsFinite(query.MaxDistance) || query.MaxDistance <= 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidMaxDistance,
                    "Max Distance must be finite and greater than zero.");
            }

            if (!IsFinite(query.Radius) || query.Radius < 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidRadius,
                    "Radius must be finite and must not be negative.");
            }

            if (query.MaxReflectionCount < 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidReflectionCount,
                    "Max Reflection Count must not be negative.");
            }

            if (query.MaxPierceCount < 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidPierceCount,
                    "Max Pierce Count must not be negative.");
            }

            if (query.MaxHitCount <= 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidHitCount,
                    "Max Hit Count must be greater than zero.");
            }

            if (query.MaxIterationCount <= 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidIterationCount,
                    "Max Iteration Count must be greater than zero.");
            }

            if (!IsFinite(query.SurfaceOffset) || query.SurfaceOffset < 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.InvalidSurfaceOffset,
                    "Surface Offset must be finite and must not be negative.");
            }

            if (query.MaxIterationCount > 0
                && query.MaxIterationCount < query.MaxReflectionCount + query.MaxPierceCount + 1)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Info,
                    TrajectoryQueryValidationCode.IterationBudgetMayEndEarly,
                    "Max Iteration Count may end before all reflection or pierce continuations are consumed.");
            }

            return count;
        }

        public static TrajectoryValidationSeverity GetWorstSeverity(
            TrajectoryQueryValidationIssue[] issues,
            int count)
        {
            TrajectoryValidationSeverity worst = TrajectoryValidationSeverity.Info;
            int max = issues == null || count <= 0 ? 0 : count < issues.Length ? count : issues.Length;
            for (int i = 0; i < max; i++)
            {
                if (issues[i].Severity > worst)
                {
                    worst = issues[i].Severity;
                }
            }

            return worst;
        }

        private static void AddIssue(
            TrajectoryQueryValidationIssue[] issues,
            ref int count,
            TrajectoryValidationSeverity severity,
            TrajectoryQueryValidationCode code,
            string message)
        {
            if (issues != null && count < issues.Length)
            {
                issues[count] = new TrajectoryQueryValidationIssue(severity, code, message);
            }

            count++;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
