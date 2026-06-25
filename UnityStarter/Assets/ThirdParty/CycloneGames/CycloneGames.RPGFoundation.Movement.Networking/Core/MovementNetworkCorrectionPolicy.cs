using System;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public readonly struct MovementNetworkCorrectionPolicy
    {
        public readonly float PositionCorrectionThresholdSqr;
        public readonly float PositionHardSnapThresholdSqr;
        public readonly float VelocityCorrectionThresholdSqr;

        public MovementNetworkCorrectionPolicy(
            float positionCorrectionThreshold = 0.05f,
            float positionHardSnapThreshold = 2f,
            float velocityCorrectionThreshold = 0.5f)
        {
            if (!float.IsFinite(positionCorrectionThreshold) || positionCorrectionThreshold < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(positionCorrectionThreshold));
            }

            if (!float.IsFinite(positionHardSnapThreshold) || positionHardSnapThreshold < positionCorrectionThreshold)
            {
                throw new ArgumentOutOfRangeException(nameof(positionHardSnapThreshold));
            }

            if (!float.IsFinite(velocityCorrectionThreshold) || velocityCorrectionThreshold < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(velocityCorrectionThreshold));
            }

            PositionCorrectionThresholdSqr = positionCorrectionThreshold * positionCorrectionThreshold;
            PositionHardSnapThresholdSqr = positionHardSnapThreshold * positionHardSnapThreshold;
            VelocityCorrectionThresholdSqr = velocityCorrectionThreshold * velocityCorrectionThreshold;
        }

        public static MovementNetworkCorrectionPolicy Default
        {
            get
            {
                return new MovementNetworkCorrectionPolicy();
            }
        }
    }
}
