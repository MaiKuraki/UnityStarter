using System;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Applies authoritative snapshots to a local <see cref="ProjectileWorld"/>.
    ///
    /// The receive half of replication: <see cref="IProjectileNetworkSnapshotSource"/> captures
    /// authority state, this sink lands it on a replica. Policy:
    /// <list type="bullet">
    /// <item>An unknown projectile is created from the snapshot using the local definition catalog.</item>
    /// <item>A known projectile is patched in place, keeping its handle, bounce and pierce budget.</item>
    /// <item>An older tick is rejected as stale, so out-of-order unreliable delivery cannot rewind state.</item>
    /// <item>A hard reset is only performed on request (definition mismatch or hard snap).</item>
    /// </list>
    /// No allocation happens per snapshot, and every outcome is counted for observability.
    /// </summary>
    public sealed class ProjectileWorldSnapshotSink : IProjectileNetworkSnapshotSink
    {
        private readonly ProjectileWorld _world;
        private readonly IProjectileDefinitionResolver _definitions;

        private long _appliedCount;
        private long _spawnedCount;
        private long _staleCount;
        private long _invalidCount;
        private long _resetCount;
        private long _missingDefinitionCount;
        private long _capacityRejectedCount;

        public ProjectileWorldSnapshotSink(ProjectileWorld world, IProjectileDefinitionResolver definitions)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
        }

        public ProjectileWorld World => _world;

        /// <summary>Snapshots that patched an existing projectile.</summary>
        public long AppliedCount => _appliedCount;

        /// <summary>Snapshots that created a projectile the replica did not have.</summary>
        public long SpawnedCount => _spawnedCount;

        /// <summary>Snapshots rejected because the local projectile was already ahead.</summary>
        public long StaleCount => _staleCount;

        /// <summary>Snapshots rejected as malformed (zero id, non-finite state, negative age).</summary>
        public long InvalidCount => _invalidCount;

        /// <summary>Successful hard resets.</summary>
        public long ResetCount => _resetCount;

        /// <summary>Snapshots dropped because the local catalog has no such definition id.</summary>
        public long MissingDefinitionCount => _missingDefinitionCount;

        /// <summary>Snapshots dropped because the world was at capacity when a spawn was needed.</summary>
        public long CapacityRejectedCount => _capacityRejectedCount;

        public bool TryApplySnapshot(in ProjectileSnapshot snapshot)
        {
            ProjectileSnapshotApplyResult result = _world.TryApplySnapshot(in snapshot);
            switch (result)
            {
                case ProjectileSnapshotApplyResult.Applied:
                    _appliedCount++;
                    return true;

                case ProjectileSnapshotApplyResult.Stale:
                    _staleCount++;
                    return false;

                case ProjectileSnapshotApplyResult.Invalid:
                    _invalidCount++;
                    return false;

                case ProjectileSnapshotApplyResult.Missing:
                    return TrySpawnFromSnapshot(in snapshot);

                default:
                    return false;
            }
        }

        public bool TryResetFromSnapshot(in ProjectileSnapshot snapshot)
        {
            ProjectileDefinition definition;
            if (!_definitions.TryGetDefinition(snapshot.DefinitionId.Value, out definition))
            {
                _missingDefinitionCount++;
                return false;
            }

            ProjectileSnapshotApplyResult result = _world.TryResetFromSnapshot(in snapshot, in definition, out _);
            switch (result)
            {
                case ProjectileSnapshotApplyResult.Spawned:
                    _resetCount++;
                    return true;

                case ProjectileSnapshotApplyResult.Invalid:
                    _invalidCount++;
                    return false;

                case ProjectileSnapshotApplyResult.Failed:
                    _capacityRejectedCount++;
                    return false;

                default:
                    return false;
            }
        }

        private bool TrySpawnFromSnapshot(in ProjectileSnapshot snapshot)
        {
            ProjectileDefinition definition;
            if (!_definitions.TryGetDefinition(snapshot.DefinitionId.Value, out definition))
            {
                _missingDefinitionCount++;
                return false;
            }

            if (_world.TrySpawnFromSnapshot(in snapshot, in definition, out _))
            {
                _spawnedCount++;
                return true;
            }

            // The only remaining reason a valid snapshot fails to spawn is a full world.
            _capacityRejectedCount++;
            return false;
        }
    }
}
