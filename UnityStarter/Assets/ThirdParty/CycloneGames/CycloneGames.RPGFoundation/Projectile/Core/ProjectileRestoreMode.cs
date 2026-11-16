using System;

namespace CycloneGames.RPGFoundation.Projectile.Core
{
    /// <summary>
    /// How <see cref="ProjectileWorld.TryRestoreStates"/> treats projectiles that exist locally
    /// but are absent from the supplied batch.
    /// </summary>
    public enum ProjectileRestoreMode
    {
        /// <summary>
        /// Keep local projectiles the batch does not mention. Use when the batch is a partial
        /// view (interest-managed subset) rather than a complete authority snapshot.
        /// </summary>
        Merge = 0,

        /// <summary>
        /// Despawn every local projectile the batch does not mention. Use when the batch is the
        /// complete authoritative state for a tick, which is the rollback and hard-resync case.
        /// </summary>
        Replace = 1,
    }

    /// <summary>Reason a batch restore left the world unchanged.</summary>
    public enum ProjectileRestoreRejection
    {
        /// <summary>The batch was applied.</summary>
        None = 0,

        /// <summary>At least one entry had a zero id, non-finite state or negative age.</summary>
        InvalidSnapshot = 1,

        /// <summary>An entry needed creating but its definition id is not in the catalog.</summary>
        UnknownDefinition = 2,

        /// <summary>The batch would not fit the world after removals.</summary>
        Capacity = 3,
    }

    /// <summary>
    /// Outcome of a batch restore. A rejected restore is atomic: nothing changed, so the caller
    /// can keep simulating as if the call never happened.
    /// </summary>
    public readonly struct ProjectileRestoreResult
    {
        /// <summary>Local projectiles patched from the batch, including ones created earlier in the same batch.</summary>
        public readonly int Applied;

        /// <summary>Projectiles created because the batch mentioned an unknown id.</summary>
        public readonly int Spawned;

        /// <summary>Projectiles removed under <see cref="ProjectileRestoreMode.Replace"/> because the batch omitted them.</summary>
        public readonly int Despawned;

        /// <summary>Entries rejected as unusable; non-zero only on a rejected batch.</summary>
        public readonly int RejectedInvalid;

        /// <summary>How many entries did not fit; non-zero only on a rejected batch.</summary>
        public readonly int RejectedCapacity;

        /// <summary>Entries whose definition could not be resolved; non-zero only on a rejected batch.</summary>
        public readonly int RejectedUnknownDefinition;

        /// <summary>
        /// Patches against projectiles this same batch created. The result is deterministic
        /// (later entry wins) but a caller building duplicates is almost certainly doing it wrong.
        /// </summary>
        public readonly int DuplicateIds;

        /// <summary>The tick the world is anchored to after the call.</summary>
        public readonly int Tick;

        /// <summary>False when the batch was rejected; the world is unchanged in that case.</summary>
        public readonly bool Succeeded;

        public readonly ProjectileRestoreRejection Rejection;

        public ProjectileRestoreResult(
            int applied,
            int spawned,
            int despawned,
            int rejectedInvalid,
            int rejectedCapacity,
            int rejectedUnknownDefinition,
            int duplicateIds,
            int tick,
            bool succeeded,
            ProjectileRestoreRejection rejection)
        {
            Applied = applied;
            Spawned = spawned;
            Despawned = despawned;
            RejectedInvalid = rejectedInvalid;
            RejectedCapacity = rejectedCapacity;
            RejectedUnknownDefinition = rejectedUnknownDefinition;
            DuplicateIds = duplicateIds;
            Tick = tick;
            Succeeded = succeeded;
            Rejection = rejection;
        }
    }
}
