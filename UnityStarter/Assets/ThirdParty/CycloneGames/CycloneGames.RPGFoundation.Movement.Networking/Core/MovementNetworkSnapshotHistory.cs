namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public sealed class MovementNetworkSnapshotHistory
    {
        private readonly MovementEntityHistory<MovementNetworkSnapshotMessage> _history;

        public MovementNetworkSnapshotHistory(MovementHistoryCapacity capacity)
        {
            _history = new MovementEntityHistory<MovementNetworkSnapshotMessage>(capacity);
        }

        public MovementNetworkSnapshotHistory(int entityCount, int perEntityCount)
            : this(new MovementHistoryCapacity(entityCount, perEntityCount))
        {
        }

        public MovementHistoryDiagnostics GetDiagnostics()
        {
            return _history.GetDiagnostics();
        }

        public void Record(in MovementNetworkSnapshotMessage snapshot)
        {
            _history.Record(snapshot.EntityId, snapshot.ServerTick, snapshot.Sequence, snapshot);
        }

        public bool TryGet(ulong entityId, long serverTick, ushort sequence, out MovementNetworkSnapshotMessage snapshot)
        {
            return _history.TryGet(entityId, serverTick, sequence, out snapshot);
        }

        public bool TryGetLatest(ulong entityId, out MovementNetworkSnapshotMessage snapshot)
        {
            long tick;
            ushort sequence;
            return _history.TryGetLatest(entityId, out snapshot, out tick, out sequence);
        }

        public bool Contains(in MovementNetworkSnapshotMessage snapshot)
        {
            return _history.Contains(snapshot.EntityId, snapshot.ServerTick, snapshot.Sequence);
        }

        public int PruneUpTo(ulong entityId, long serverTick)
        {
            return _history.PruneUpTo(entityId, serverTick);
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
