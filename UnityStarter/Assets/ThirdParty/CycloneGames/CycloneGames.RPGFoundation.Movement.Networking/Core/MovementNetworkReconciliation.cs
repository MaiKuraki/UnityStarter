using CycloneGames.Networking;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public static class MovementNetworkReconciliation
    {
        public static bool RequiresCorrection(
            in MovementNetworkSnapshotMessage predicted,
            in MovementNetworkSnapshotMessage authoritative,
            in MovementNetworkCorrectionPolicy policy,
            out float positionErrorSqr)
        {
            positionErrorSqr = NetworkVector3.SqrDistance(predicted.Position, authoritative.Position);
            if (positionErrorSqr > policy.PositionCorrectionThresholdSqr)
            {
                return true;
            }

            float velocityErrorSqr = NetworkVector3.SqrDistance(predicted.Velocity, authoritative.Velocity);
            return velocityErrorSqr > policy.VelocityCorrectionThresholdSqr
                   || predicted.StateId != authoritative.StateId
                   || predicted.JumpCount != authoritative.JumpCount
                   || predicted.Flags != authoritative.Flags;
        }

        public static bool TryCreateCorrection(
            in MovementNetworkSnapshotMessage predicted,
            in MovementNetworkSnapshotMessage authoritative,
            in MovementNetworkCorrectionPolicy policy,
            out MovementCorrectionMessage correction)
        {
            if (!predicted.IsValid
                || !authoritative.IsValid
                || predicted.EntityId != authoritative.EntityId)
            {
                correction = default;
                return false;
            }

            if (!RequiresCorrection(predicted, authoritative, policy, out float positionErrorSqr))
            {
                correction = default;
                return false;
            }

            MovementNetworkSnapshotMessage correctionSnapshot = authoritative;
            if (positionErrorSqr > policy.PositionHardSnapThresholdSqr)
            {
                correctionSnapshot.Flags = MovementNetworkSnapshotFlags.Set(
                    correctionSnapshot.Flags,
                    MovementNetworkSnapshotFlags.Teleport,
                    true);
            }

            correction = new MovementCorrectionMessage(
                authoritative.EntityId,
                predicted.Tick,
                authoritative.ServerTick,
                predicted.Sequence,
                positionErrorSqr,
                correctionSnapshot);
            return true;
        }
    }
}
