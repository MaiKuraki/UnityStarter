using CycloneGames.Networking;
using CycloneGames.Networking.Simulation;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public sealed class ProjectileNetworkSnapshotHistory
    {
        private readonly NetworkActionHistory<ProjectileSnapshotMessage> _history;

        public ProjectileNetworkSnapshotHistory(int capacity)
        {
            _history = new NetworkActionHistory<ProjectileSnapshotMessage>(capacity);
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

        public void Record(in ProjectileSnapshotMessage snapshot)
        {
            _history.Record(
                snapshot.ProjectileEntityId,
                new NetworkTickId(snapshot.ServerTick),
                snapshot.Sequence,
                snapshot);
        }

        public bool TryGet(
            ulong projectileEntityId,
            int serverTick,
            ushort sequence,
            out ProjectileSnapshotMessage snapshot)
        {
            return _history.TryGet(
                projectileEntityId,
                new NetworkTickId(serverTick),
                sequence,
                out snapshot);
        }

        public bool TryGetLatest(
            ulong projectileEntityId,
            out ProjectileSnapshotMessage snapshot)
        {
            if (_history.TryGetLatest(projectileEntityId, out NetworkActionHistoryEntry<ProjectileSnapshotMessage> entry))
            {
                snapshot = entry.Snapshot;
                return true;
            }

            snapshot = default;
            return false;
        }

        public int RemoveProjectile(ulong projectileEntityId)
        {
            return _history.RemoveEntity(projectileEntityId);
        }

        public void Clear()
        {
            _history.Clear();
        }
    }
}
