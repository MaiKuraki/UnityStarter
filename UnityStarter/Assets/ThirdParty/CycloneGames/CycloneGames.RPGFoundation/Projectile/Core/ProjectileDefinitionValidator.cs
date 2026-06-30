namespace CycloneGames.RPGFoundation.Projectile.Core
{
    public static class ProjectileDefinitionValidator
    {
        public const int RECOMMENDED_ISSUE_CAPACITY = 16;

        public static int Validate(
            in ProjectileDefinition definition,
            ProjectileDefinitionValidationIssue[] issues)
        {
            int count = 0;

            if (!definition.DefinitionId.IsValid)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.InvalidDefinitionId,
                    "Definition Id must be a positive stable id.");
            }

            if (!AreKinematicValuesFinite(in definition))
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NonFiniteKinematicValue,
                    "Kinematic values must be finite.");
            }

            if (definition.InitialSpeed < 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NegativeInitialSpeed,
                    "Initial Speed must not be negative.");
            }

            if (definition.MaxSpeed < 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NegativeMaxSpeed,
                    "Max Speed must not be negative.");
            }

            if (definition.MaxSpeed > 0f && definition.MaxSpeed < definition.InitialSpeed)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Warning,
                    ProjectileDefinitionValidationCode.MaxSpeedBelowInitialSpeed,
                    "Max Speed is lower than Initial Speed; the projectile may clamp immediately after spawn.");
            }

            if (definition.Radius < 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NegativeRadius,
                    "Radius must not be negative.");
            }

            if (definition.MaxLifetime <= 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.InvalidMaxLifetime,
                    "Max Lifetime must be greater than zero.");
            }

            if ((definition.GuidanceMode == ProjectileGuidanceMode.Homing
                    || definition.GuidanceMode == ProjectileGuidanceMode.LeadHoming)
                && definition.TurnRateRadiansPerSecond <= 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.InvalidTurnRate,
                    "Homing projectiles require a positive Turn Rate.");
            }

            if (definition.GuidanceMode == ProjectileGuidanceMode.LeadHoming
                && definition.LeadPredictionTime <= 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Info,
                    ProjectileDefinitionValidationCode.LeadHomingWithoutLeadTime,
                    "Lead Homing with zero lead time behaves like regular Homing.");
            }

            if (definition.LeadPredictionTime < 0f)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NegativeLeadPredictionTime,
                    "Lead Prediction Time must not be negative.");
            }

            if (definition.PierceCount < 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NegativePierceCount,
                    "Pierce Count must not be negative.");
            }

            if (definition.BounceCount < 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Error,
                    ProjectileDefinitionValidationCode.NegativeBounceCount,
                    "Bounce Count must not be negative.");
            }

            if (definition.CollisionLayerMask == 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Warning,
                    ProjectileDefinitionValidationCode.EmptyCollisionLayerMask,
                    "Collision Layer Mask is empty; default adapters will not report collision hits.");
            }

            if (definition.BounceCount > 0 && definition.PierceCount > 0)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Info,
                    ProjectileDefinitionValidationCode.BounceBeforePierce,
                    "Bounce and Pierce are both enabled. Runtime resolves Bounce before Pierce.");
            }

            bool predicted = HasFlag(definition.LifecycleFlags, ProjectileLifecycleFlags.Predicted);
            bool authoritative = HasFlag(definition.LifecycleFlags, ProjectileLifecycleFlags.Authoritative);
            if (predicted && authoritative)
            {
                AddIssue(
                    issues,
                    ref count,
                    ProjectileValidationSeverity.Info,
                    ProjectileDefinitionValidationCode.PredictedAndAuthoritative,
                    "Predicted and Authoritative are both set. This is valid for shared definitions, but runtime ownership should choose one authority path.");
            }

            return count;
        }

        public static ProjectileValidationSeverity GetWorstSeverity(
            ProjectileDefinitionValidationIssue[] issues,
            int count)
        {
            ProjectileValidationSeverity worst = ProjectileValidationSeverity.Info;
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
            ProjectileDefinitionValidationIssue[] issues,
            ref int count,
            ProjectileValidationSeverity severity,
            ProjectileDefinitionValidationCode code,
            string message)
        {
            if (issues != null && count < issues.Length)
            {
                issues[count] = new ProjectileDefinitionValidationIssue(severity, code, message);
            }

            count++;
        }

        private static bool AreKinematicValuesFinite(in ProjectileDefinition definition)
        {
            return IsFinite(definition.InitialSpeed)
                   && IsFinite(definition.MaxSpeed)
                   && IsFinite(definition.Acceleration)
                   && IsFinite(definition.GravityScale)
                   && IsFinite(definition.Radius)
                   && IsFinite(definition.MaxLifetime)
                   && IsFinite(definition.TurnRateRadiansPerSecond)
                   && IsFinite(definition.LeadPredictionTime);
        }

        private static bool HasFlag(
            ProjectileLifecycleFlags flags,
            ProjectileLifecycleFlags flag)
        {
            return (flags & flag) == flag;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
