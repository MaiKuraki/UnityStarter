namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// Outcome of applying an authoritative snapshot to a world.
    /// </summary>
    public enum ProjectileSnapshotApplyResult
    {
        /// <summary>Snapshot is unusable: zero network id, or non-finite position/velocity/age.</summary>
        Invalid = 0,

        /// <summary>No live projectile owns this network id. The receiver must create one.</summary>
        Missing = 1,

        /// <summary>The local projectile is already at or ahead of this tick; the snapshot is stale.</summary>
        Stale = 2,

        /// <summary>Existing projectile state was overwritten in place.</summary>
        Applied = 3,

        /// <summary>The projectile did not exist and was created from the snapshot.</summary>
        Spawned = 4,

        /// <summary>The reset could not be performed (no definition, or the world is at capacity).</summary>
        Failed = 5,
    }
}
