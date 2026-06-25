using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementNetworkSnapshotHistory
    {
        private readonly NetworkActionHistory<MovementNetworkSnapshotMessage> _history;

        public MovementNetworkSnapshotHistory(int capacity)
        {
            _history = new NetworkActionHistory<MovementNetworkSnapshotMessage>(capacity);
        }

        public int Capacity
        {
            get
            {
                return _history.Capacity;
            }
        }

        public int Count
        {
            get
            {
                return _history.Count;
            }
        }

        public void Record(in MovementNetworkSnapshotMessage snapshot)
        {
            _history.Record(
                snapshot.EntityId,
                new NetworkTickId(snapshot.ServerTick),
                snapshot.Sequence,
                snapshot);
        }

        public bool TryGet(
            ulong entityId,
            int serverTick,
            ushort sequence,
            out MovementNetworkSnapshotMessage snapshot)
        {
            return _history.TryGet(
                entityId,
                new NetworkTickId(serverTick),
                sequence,
                out snapshot);
        }

        public bool TryGetLatest(
            ulong entityId,
            out MovementNetworkSnapshotMessage snapshot)
        {
            if (_history.TryGetLatest(entityId, out NetworkActionHistoryEntry<MovementNetworkSnapshotMessage> entry))
            {
                snapshot = entry.Snapshot;
                return true;
            }

            snapshot = default;
            return false;
        }

        public int RemoveEntity(ulong entityId)
        {
            return _history.RemoveEntity(entityId);
        }

        public void Clear()
        {
            _history.Clear();
        }
    }
}
