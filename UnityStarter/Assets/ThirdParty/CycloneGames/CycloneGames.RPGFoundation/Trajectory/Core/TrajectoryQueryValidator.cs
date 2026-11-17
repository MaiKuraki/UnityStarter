namespace CycloneGames.RPGFoundation.Trajectory.Core
{
    public static class TrajectoryQueryValidator
    {
        /// <summary>
        /// Suggested capacity for a reusable issue array. Static readonly rather than const so the
        /// value is read at run time instead of being inlined into consumer assemblies.
        /// </summary>
        public static readonly int RECOMMENDED_ISSUE_CAPACITY = 16;

        /// <summary>
        /// Iteration budgets above this are reported as a warning. Not a hard limit: long ricochet or
        /// pierce chains are legitimate, but a four-digit budget over a fixed-size buffer is almost
        /// always a configuration mistake.
        /// </summary>
        public static readonly int RECOMMENDED_MAX_ITERATION_COUNT = 256;

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

            float directionLengthSquared = query.Direction.LengthSquared;
            if (!IsFinite(directionLengthSquared))
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Error,
                    TrajectoryQueryValidationCode.NonFiniteDirectionMagnitude,
                    "Direction magnitude overflows to a non-finite value; normalize the direction before building the query.");
            }
            else if (directionLengthSquared <= DIRECTION_EPSILON)
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

            // Compare in 64-bit: MaxReflectionCount + MaxPierceCount + 1 overflows int near int.MaxValue.
            if (query.MaxIterationCount > 0
                && query.MaxIterationCount < (long)query.MaxReflectionCount + query.MaxPierceCount + 1L)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Info,
                    TrajectoryQueryValidationCode.IterationBudgetMayEndEarly,
                    "Max Iteration Count may end before all reflection or pierce continuations are consumed.");
            }

            if (query.MaxIterationCount > RECOMMENDED_MAX_ITERATION_COUNT)
            {
                AddIssue(
                    issues,
                    ref count,
                    TrajectoryValidationSeverity.Warning,
                    TrajectoryQueryValidationCode.IterationBudgetExcessive,
                    "Max Iteration Count exceeds the recommended ceiling; the fixed-capacity buffers bound the real cost, so a larger budget rarely changes the result.");
            }

            // Shape checks share the exact helpers used by TrajectoryQuery.IsValid and
            // TrajectoryPolylineSolver, so an Inspector that reports no error can never disagree
            // with the runtime gate.
            switch (query.Shape)
            {
                case TrajectoryShape.Ray:
                    break;

                case TrajectoryShape.Sphere:
                    if (!TrajectoryQuery.IsSphereShapeValid(query.Radius))
                    {
                        AddIssue(
                            issues,
                            ref count,
                            TrajectoryValidationSeverity.Error,
                            TrajectoryQueryValidationCode.InvalidSphereRadius,
                            "Sphere radius must be finite and greater than zero.");
                    }

                    break;

                case TrajectoryShape.Capsule:
                    if (!TrajectoryQuery.IsCapsuleShapeValid(query.Radius, query.Height))
                    {
                        AddIssue(
                            issues,
                            ref count,
                            TrajectoryValidationSeverity.Error,
                            TrajectoryQueryValidationCode.InvalidCapsuleHeight,
                            "Capsule requires a finite radius greater than zero and a height of at least 2 * radius.");
                    }

                    break;

                case TrajectoryShape.Box:
                    if (!TrajectoryQuery.IsBoxShapeValid(query.HalfExtents))
                    {
                        AddIssue(
                            issues,
                            ref count,
                            TrajectoryValidationSeverity.Error,
                            TrajectoryQueryValidationCode.InvalidBoxHalfExtents,
                            "Box half extents must be finite and greater than zero on every axis.");
                    }

                    break;

                default:
                    AddIssue(
                        issues,
                        ref count,
                        TrajectoryValidationSeverity.Error,
                        TrajectoryQueryValidationCode.InvalidShape,
                        "Sweep shape is not a supported value.");
                    break;
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
