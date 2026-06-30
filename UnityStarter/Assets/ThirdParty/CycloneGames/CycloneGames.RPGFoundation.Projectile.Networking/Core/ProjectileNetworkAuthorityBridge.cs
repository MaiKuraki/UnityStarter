using System;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public sealed class ProjectileNetworkAuthorityBridge
    {
        private readonly IProjectileNetworkSnapshotSource _snapshotSource;
        private readonly IProjectileNetworkSnapshotSink _snapshotSink;

        public ProjectileNetworkAuthorityBridge(
            IProjectileNetworkSnapshotSource snapshotSource,
            IProjectileNetworkSnapshotSink snapshotSink = null)
        {
            _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
            _snapshotSink = snapshotSink;
        }

        public bool TryCaptureSnapshot(
            ulong projectileEntityId,
            ushort sequence,
            out ProjectileSnapshotMessage message)
        {
            if (!_snapshotSource.TryGetSnapshot(projectileEntityId, out ProjectileSnapshot snapshot))
            {
                message = default;
                return false;
            }

            message = ProjectileSnapshotMessage.FromSnapshot(in snapshot, sequence);
            return message.IsValid;
        }

        public bool TryApplySnapshot(in ProjectileSnapshotMessage message)
        {
            if (_snapshotSink == null || !message.IsValid)
            {
                return false;
            }

            ProjectileSnapshot snapshot = message.ToProjectileSnapshot();
            return _snapshotSink.TryApplySnapshot(in snapshot);
        }

        public bool TryResetFromSnapshot(in ProjectileSnapshotMessage message)
        {
            if (_snapshotSink == null || !message.IsValid)
            {
                return false;
            }

            ProjectileSnapshot snapshot = message.ToProjectileSnapshot();
            return _snapshotSink.TryResetFromSnapshot(in snapshot);
        }
    }
}
