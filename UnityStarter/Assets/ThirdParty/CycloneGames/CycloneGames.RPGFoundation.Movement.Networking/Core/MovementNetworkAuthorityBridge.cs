using System;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementDefaultValidator : IMovementValidator
    {
        private readonly float _maxSpeed;

        public MovementDefaultValidator(float maxSpeed = 20f)
        {
            if (!float.IsFinite(maxSpeed) || maxSpeed <= 0f)
                throw new ArgumentOutOfRangeException(nameof(maxSpeed));
            _maxSpeed = maxSpeed;
        }

        public bool ValidatePosition(MovementVector3 from, MovementVector3 to, float deltaTime)
        {
            if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return false;
            if (!from.IsFinite || !to.IsFinite) return false;
            float speedSqr = MovementVector3.SqrDistance(from, to) / (deltaTime * deltaTime);
            return float.IsFinite(speedSqr) && speedSqr <= _maxSpeed * _maxSpeed;
        }

        public bool ValidateStateTransition(MovementStateType from, MovementStateType to)
        {
            return MovementStateDomain.IsDefined((int)from)
                   && MovementStateDomain.IsDefined((int)to);
        }
    }

    public sealed class MovementNetworkAuthorityBridge
    {
        private readonly IMovementSnapshotProvider _snapshotProvider;
        private readonly IMovementValidator _validator;

        public MovementNetworkAuthorityBridge(
            IMovementSnapshotProvider snapshotProvider,
            IMovementValidator validator)
        {
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _validator = validator ?? throw new ArgumentNullException(nameof(validator));
        }

        public MovementNetworkSnapshotMessage CaptureSnapshot(
            ulong entityId,
            ulong epoch,
            long serverTick,
            ushort sequence = 0,
            byte flags = MovementNetworkSnapshotFlags.None)
        {
            MovementSnapshot snapshot = _snapshotProvider.GetSnapshot();
            return MovementNetworkSnapshotMessage.FromMovementSnapshot(
                entityId, epoch, snapshot, serverTick, sequence, flags);
        }

        public MovementFullStateMessage CaptureFullState(
            ulong entityId,
            ulong epoch,
            long serverTick,
            ushort sequence = 0)
        {
            MovementFullState state = _snapshotProvider.CaptureFullState();
            return new MovementFullStateMessage(entityId, epoch, serverTick, sequence, state);
        }

        public bool ApplySnapshot(in MovementNetworkSnapshotMessage message)
        {
            if (!message.IsValid) return false;
            MovementSnapshot snapshot = message.ToMovementSnapshot();
            _snapshotProvider.ApplySnapshot(snapshot);
            return true;
        }

        public bool ApplyFullState(in MovementFullStateMessage message)
        {
            if (!message.IsValid) return false;
            _snapshotProvider.ApplyFullState(message.State);
            return true;
        }

        public bool ResetFromFullState(in MovementFullStateMessage message)
        {
            if (!message.IsValid) return false;
            _snapshotProvider.ResetFromFullState(message.State);
            return true;
        }

        public bool ValidateTransition(
            in MovementNetworkSnapshotMessage previous,
            in MovementNetworkSnapshotMessage next,
            float deltaTime)
        {
            if (!previous.IsValid || !next.IsValid) return false;
            if (previous.EntityId != next.EntityId || previous.Epoch != next.Epoch) return false;
            if (!float.IsFinite(deltaTime) || deltaTime <= 0f) return false;
            if (next.ServerTick < previous.ServerTick) return false;

            return _validator.ValidatePosition(
                       previous.Position.ToMovementVector3(),
                       next.Position.ToMovementVector3(),
                       deltaTime)
                   && _validator.ValidateStateTransition(
                       (MovementStateType)previous.StateId,
                       (MovementStateType)next.StateId);
        }
    }
}
