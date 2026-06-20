using System;
using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementNetworkAuthorityBridge
    {
        private readonly IMovementSnapshotProvider _snapshotProvider;
        private readonly IMovementValidator _validator;

        public MovementNetworkAuthorityBridge(
            IMovementSnapshotProvider snapshotProvider,
            IMovementValidator validator = null)
        {
            _snapshotProvider = snapshotProvider ?? throw new ArgumentNullException(nameof(snapshotProvider));
            _validator = validator;
        }

        public MovementNetworkSnapshotMessage CaptureSnapshot(
            ulong entityId,
            int serverTick,
            ushort sequence = 0,
            byte flags = MovementNetworkSnapshotFlags.None)
        {
            MovementSnapshot snapshot = _snapshotProvider.GetSnapshot();
            return MovementNetworkSnapshotMessage.FromMovementSnapshot(
                entityId,
                snapshot,
                serverTick,
                sequence,
                flags);
        }

        public bool ApplySnapshot(in MovementNetworkSnapshotMessage message)
        {
            if (!message.IsValid)
            {
                return false;
            }

            MovementSnapshot snapshot = message.ToMovementSnapshot();
            _snapshotProvider.ApplySnapshot(snapshot);
            return true;
        }

        public bool ResetFromSnapshot(in MovementNetworkSnapshotMessage message)
        {
            if (!message.IsValid)
            {
                return false;
            }

            MovementSnapshot snapshot = message.ToMovementSnapshot();
            _snapshotProvider.ResetFromSnapshot(snapshot);
            return true;
        }

        public bool ValidateTransition(
            in MovementNetworkSnapshotMessage previous,
            in MovementNetworkSnapshotMessage next,
            float deltaTime)
        {
            if (!previous.IsValid || !next.IsValid || previous.EntityId != next.EntityId)
            {
                return false;
            }

            if (_validator == null)
            {
                return true;
            }

            MovementSnapshot previousSnapshot = previous.ToMovementSnapshot();
            MovementSnapshot nextSnapshot = next.ToMovementSnapshot();
            return _validator.ValidatePosition(
                       previousSnapshot.Position,
                       nextSnapshot.Position,
                       deltaTime)
                   && _validator.ValidateStateTransition(
                       previousSnapshot.StateType,
                       nextSnapshot.StateType);
        }
    }
}
