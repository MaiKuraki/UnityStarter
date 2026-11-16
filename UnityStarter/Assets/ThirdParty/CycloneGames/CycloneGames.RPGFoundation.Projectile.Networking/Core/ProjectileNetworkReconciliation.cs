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

            // NaN makes every threshold comparison below evaluate to false, so a corrupt
            // prediction would be reported as "no divergence" and the client would keep a
            // state it can never reconcile. Detect it first and demand a hard reset.
            if (!IsFinite(in predicted) || !IsFinite(in authoritative))
            {
                correctionFlags = ProjectileNetworkCorrectionFlags.Transform
                                  | ProjectileNetworkCorrectionFlags.Velocity
                                  | ProjectileNetworkCorrectionFlags.Timeline
                                  | ProjectileNetworkCorrectionFlags.HardSnap;
                positionErrorSqr = float.PositiveInfinity;
                return true;
            }

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
            if (!authoritative.IsValid
                || predicted.ProjectileEntityId == 0UL
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

        private static bool IsFinite(in ProjectileSnapshotMessage message)
        {
            return IsFinite(message.Age)
                   && IsFinite(message.Radius)
                   && message.Position.IsFinite()
                   && message.PreviousPosition.IsFinite()
                   && message.Velocity.IsFinite();
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
        }
    }
}
