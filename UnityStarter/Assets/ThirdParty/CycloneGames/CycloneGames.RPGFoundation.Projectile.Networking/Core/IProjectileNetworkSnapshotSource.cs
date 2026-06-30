using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public interface IProjectileNetworkSnapshotSource
    {
        bool TryGetSnapshot(
            ulong projectileEntityId,
            out ProjectileSnapshot snapshot);
    }
}
