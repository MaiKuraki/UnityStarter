using System;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public sealed class ProjectileWorldNetworkSnapshotSource : IProjectileNetworkSnapshotSource
    {
        private readonly ProjectileWorld _world;

        public ProjectileWorldNetworkSnapshotSource(ProjectileWorld world)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
        }

        public bool TryGetSnapshot(
            ulong projectileEntityId,
            out ProjectileSnapshot snapshot)
        {
            return _world.TryGetSnapshotByNetworkEntityId(projectileEntityId, out snapshot);
        }
    }
}
