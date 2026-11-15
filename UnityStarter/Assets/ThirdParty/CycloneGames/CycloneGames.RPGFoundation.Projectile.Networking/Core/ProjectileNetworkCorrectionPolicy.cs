using System;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public readonly struct ProjectileNetworkCorrectionPolicy
    {
        // A struct's implicit parameterless constructor zero-initializes every field;
        // the tuning constructor must be invoked explicitly to obtain real thresholds.
        public static readonly ProjectileNetworkCorrectionPolicy Default = new ProjectileNetworkCorrectionPolicy(
            positionCorrectionThreshold: 0.05f,
            positionHardSnapThreshold: 2f,
            velocityCorrectionThreshold: 0.5f,
            ageCorrectionThreshold: 0.05f,
            correctLifecycleFlags: true,
            correctTarget: true);

        public readonly float PositionCorrectionThresholdSqr;
        public readonly float PositionHardSnapThresholdSqr;
        public readonly float VelocityCorrectionThresholdSqr;
        public readonly float AgeCorrectionThreshold;
        public readonly bool CorrectLifecycleFlags;
        public readonly bool CorrectTarget;

        public ProjectileNetworkCorrectionPolicy(
            float positionCorrectionThreshold = 0.05f,
            float positionHardSnapThreshold = 2f,
            float velocityCorrectionThreshold = 0.5f,
            float ageCorrectionThreshold = 0.05f,
            bool correctLifecycleFlags = true,
            bool correctTarget = true)
        {
            if (!IsFinite(positionCorrectionThreshold) || positionCorrectionThreshold < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(positionCorrectionThreshold));
            }

            if (!IsFinite(positionHardSnapThreshold) || positionHardSnapThreshold < positionCorrectionThreshold)
            {
                throw new ArgumentOutOfRangeException(nameof(positionHardSnapThreshold));
            }

            if (!IsFinite(velocityCorrectionThreshold) || velocityCorrectionThreshold < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(velocityCorrectionThreshold));
            }

            if (!IsFinite(ageCorrectionThreshold) || ageCorrectionThreshold < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(ageCorrectionThreshold));
            }

            PositionCorrectionThresholdSqr = positionCorrectionThreshold * positionCorrectionThreshold;
            PositionHardSnapThresholdSqr = positionHardSnapThreshold * positionHardSnapThreshold;
            VelocityCorrectionThresholdSqr = velocityCorrectionThreshold * velocityCorrectionThreshold;
            AgeCorrectionThreshold = ageCorrectionThreshold;
            CorrectLifecycleFlags = correctLifecycleFlags;
            CorrectTarget = correctTarget;
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
