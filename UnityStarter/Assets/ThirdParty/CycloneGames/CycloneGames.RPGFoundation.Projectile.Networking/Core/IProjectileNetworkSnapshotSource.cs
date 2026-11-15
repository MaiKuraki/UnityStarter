using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Authority-side state provider. The capacity contract lets a bridge preallocate
    /// its conversion scratch once instead of allocating per broadcast.
    /// </summary>
    public interface IProjectileNetworkSnapshotSource
    {
        int SnapshotCapacity { get; }

        bool TryGetSnapshot(ulong projectileEntityId, out ProjectileSnapshot snapshot);

        /// <summary>One sequential pass over dense state into caller-owned storage.</summary>
        int CaptureSnapshots(ProjectileSnapshot[] destination, int startIndex = 0);
    }
}
