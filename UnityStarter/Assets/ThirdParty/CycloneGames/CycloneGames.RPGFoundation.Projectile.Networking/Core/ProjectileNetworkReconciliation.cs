using System;
using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public static class ProjectileNetworkReconciliation
    {
        public static bool RequiresCorrection(
            in ProjectileSnapshotMessage predicted,
            in ProjectileSnapshotMessage authoritative,
            in ProjectileNetworkCorrectionPolicy policy,
            out ProjectileNetworkCorrectionFlags correctionFlags,
            out float positionErrorSqr)
        {
            correctionFlags = ProjectileNetworkCorrectionFlags.None;
            positionErrorSqr = NetworkVector3.SqrDistance(predicted.Position, authoritative.Position);
            if (positionErrorSqr > policy.PositionCorrectionThresholdSqr)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.Transform;
            }

            float velocityErrorSqr = NetworkVector3.SqrDistance(predicted.Velocity, authoritative.Velocity);
            if (velocityErrorSqr > policy.VelocityCorrectionThresholdSqr)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.Velocity;
            }

            if (Math.Abs(predicted.Age - authoritative.Age) > policy.AgeCorrectionThreshold
                || predicted.ServerTick != authoritative.ServerTick)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.Timeline;
            }

            if (policy.CorrectLifecycleFlags && predicted.LifecycleFlags != authoritative.LifecycleFlags)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.Lifecycle;
            }

            if (policy.CorrectTarget && predicted.TargetEntityId != authoritative.TargetEntityId)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.Target;
            }

            if (predicted.DefinitionId != authoritative.DefinitionId)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.FullReset;
            }

            if (positionErrorSqr > policy.PositionHardSnapThresholdSqr)
            {
                correctionFlags |= ProjectileNetworkCorrectionFlags.HardSnap;
            }

            return correctionFlags != ProjectileNetworkCorrectionFlags.None;
        }

        public static bool TryCreateCorrection(
            in ProjectileSnapshotMessage predicted,
            in ProjectileSnapshotMessage authoritative,
            in ProjectileNetworkCorrectionPolicy policy,
            out ProjectileCorrectionMessage correction)
        {
            if (!predicted.IsValid
                || !authoritative.IsValid
                || predicted.ProjectileEntityId != authoritative.ProjectileEntityId)
            {
                correction = default;
                return false;
            }

            if (!RequiresCorrection(
                    predicted,
                    authoritative,
                    policy,
                    out ProjectileNetworkCorrectionFlags correctionFlags,
                    out _))
            {
                correction = default;
                return false;
            }

            correction = new ProjectileCorrectionMessage(
                authoritative.ProjectileEntityId,
                authoritative.ServerTick,
                authoritative.Sequence,
                (uint)correctionFlags,
                authoritative);
            return true;
        }
    }
}
