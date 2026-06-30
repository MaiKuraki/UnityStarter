using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    public interface IProjectileNetworkSnapshotSink
    {
        bool TryApplySnapshot(in ProjectileSnapshot snapshot);

        bool TryResetFromSnapshot(in ProjectileSnapshot snapshot);
    }
}
