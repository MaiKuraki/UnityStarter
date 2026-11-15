using System;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Captures authoritative snapshots from a source and applies inbound snapshots
    /// through an optional sink. The conversion scratch is allocated once at
    /// construction, so per-tick batch capture performs no allocation.
    /// </summary>
    public sealed class ProjectileNetworkAuthorityBridge
    {
        private readonly IProjectileNetworkSnapshotSource _snapshotSource;
        private readonly IProjectileNetworkSnapshotSink _snapshotSink;
        private readonly ProjectileSnapshot[] _scratch;

        public ProjectileNetworkAuthorityBridge(
            IProjectileNetworkSnapshotSource snapshotSource,
            IProjectileNetworkSnapshotSink snapshotSink = null)
        {
            _snapshotSource = snapshotSource ?? throw new ArgumentNullException(nameof(snapshotSource));
            _snapshotSink = snapshotSink;
            _scratch = new ProjectileSnapshot[Math.Max(1, snapshotSource.SnapshotCapacity)];
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

        /// <summary>
        /// Full authoritative broadcast in one dense pass: source state is read
        /// sequentially into the preallocated scratch, then converted sequentially
        /// into the caller-owned message array. No per-projectile lookup, no allocation.
        /// A return value lower than the active count means the destination was truncated.
        /// </summary>
        public int CaptureSnapshots(
            ProjectileSnapshotMessage[] destination,
            ushort sequence,
            int startIndex = 0)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            if ((uint)startIndex > (uint)destination.Length)
            {
                throw new ArgumentOutOfRangeException(nameof(startIndex));
            }

            int count = _snapshotSource.CaptureSnapshots(_scratch, 0);
            int available = destination.Length - startIndex;
            int written = count < available ? count : available;
            for (int i = 0; i < written; i++)
            {
                destination[startIndex + i] = ProjectileSnapshotMessage.FromSnapshot(in _scratch[i], sequence);
            }

            return written;
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
