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
            positionErrorSqr = 0f;

            if (!predicted.IsWellFormed || !authoritative.IsWellFormed) return false;
            if (predicted.EntityId != authoritative.EntityId) return false;
            if (predicted.Epoch != authoritative.Epoch) return false;
            if (authoritative.ServerTick < predicted.ServerTick) return false;

            float positionError = NetworkVector3.SqrDistance(predicted.Position, authoritative.Position);
            if (!float.IsFinite(positionError))
            {
                positionErrorSqr = float.MaxValue;
                return true;
            }

            positionErrorSqr = positionError;
            if (positionError > policy.PositionCorrectionThresholdSqr) return true;

            float velocityError = NetworkVector3.SqrDistance(predicted.Velocity, authoritative.Velocity);
            if (!float.IsFinite(velocityError)) return true;

            return velocityError > policy.VelocityCorrectionThresholdSqr
                   || predicted.StateId != authoritative.StateId
                   || predicted.JumpCount != authoritative.JumpCount
                   || !MovementNetworkSnapshotFlags.WireEquals(predicted.Flags, authoritative.Flags);
        }

        public static bool TryCreateCorrection(
            in MovementNetworkSnapshotMessage predicted,
            in MovementFullStateMessage authoritative,
            in MovementNetworkCorrectionPolicy policy,
            out MovementCorrectionMessage correction)
        {
            correction = default;
            if (!predicted.IsWellFormed || !authoritative.IsValid) return false;
            if (predicted.EntityId != authoritative.EntityId) return false;
            if (predicted.Epoch != authoritative.Epoch) return false;

            byte flags = MovementNetworkSnapshotFlags.None;
            MovementNetworkSnapshotMessage delta = authoritative.ToDelta(flags);
            if (!RequiresCorrection(predicted, delta, policy, out float positionErrorSqr)) return false;

            if (positionErrorSqr > policy.PositionHardSnapThresholdSqr)
            {
                flags = MovementNetworkSnapshotFlags.Set(
                    flags, MovementNetworkSnapshotFlags.Teleport, true);
            }

            MovementFullStateMessage payload = authoritative;
            payload.State = authoritative.State;
            correction = new MovementCorrectionMessage(
                predicted.EntityId,
                predicted.Epoch,
                predicted.Tick,
                authoritative.ServerTick,
                predicted.Sequence,
                positionErrorSqr,
                payload);
            return true;
        }
    }
}
