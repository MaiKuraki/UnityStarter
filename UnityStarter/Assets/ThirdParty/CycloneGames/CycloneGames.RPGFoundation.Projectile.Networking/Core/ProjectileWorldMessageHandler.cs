using System;
using CycloneGames.RPGFoundation.Projectile.Core;

namespace CycloneGames.RPGFoundation.Projectile.Networking
{
    /// <summary>
    /// Lands inbound protocol messages on a local <see cref="ProjectileWorld"/>.
    ///
    /// Applies the four reliable messages to a replica: spawn, despawn, correction and hit.
    /// Snapshots arrive on the unreliable channel and go through
    /// <see cref="ProjectileWorldSnapshotSink"/> instead, because they carry state rather than intent.
    ///
    /// Policy invariants, all of them asserted in the EditMode tests:
    /// <list type="bullet">
    /// <item>A spawn for an id that already exists is REJECTED and counted, never applied. A
    /// duplicate must not be able to rewind a projectile that is further along, and it must not
    /// silently replace its definition either. Both would turn "the server re-sent a packet"
    /// into a client-side state change.</item>
    /// <item>A despawn for an id that is already gone is counted, not treated as an error:
    /// unreliable delivery duplicates messages.</item>
    /// <item>A correction is routed by <see cref="ProjectileCorrectionMessage.CorrectionFlags"/>:
    /// <see cref="ProjectileNetworkCorrectionFlags.FullReset"/> or
    /// <see cref="ProjectileNetworkCorrectionFlags.HardSnap"/> rebuilds the projectile
    /// (<see cref="ProjectileWorld.TryResetFromSnapshot"/>), everything else patches it in place
    /// (<see cref="ProjectileWorld.TryApplySnapshot"/>). The flag decides, not the message
    /// contents: a patch must never be silently upgraded into a reset, because a reset drops the
    /// bounce and pierce budget.</item>
    /// <item>A hit NEVER mutates world state. It is forwarded to
    /// <see cref="IProjectileHitReceiver"/>; see that interface for why a client may not own the
    /// outcome of a hit.</item>
    /// </list>
    ///
    /// Every path is allocation-free and every rejection is counted, so a silent drop is
    /// impossible to confuse with "nothing happened".
    /// </summary>
    public sealed class ProjectileWorldMessageHandler
    {
        /// <summary>
        /// <see cref="ProjectileNetworkCorrectionFlags"/> values that mean "rebuild", not "patch".
        /// </summary>
        private const uint HardResetFlags =
            (uint)(ProjectileNetworkCorrectionFlags.FullReset | ProjectileNetworkCorrectionFlags.HardSnap);

        private readonly ProjectileWorld _world;
        private readonly IProjectileDefinitionResolver _definitions;
        private readonly IProjectileHitReceiver _hitReceiver;

        private long _spawnAcceptedCount;
        private long _spawnRejectedDuplicateCount;
        private long _spawnRejectedInvalidCount;
        private long _spawnRejectedDefinitionCount;
        private long _spawnRejectedCapacityCount;

        private long _despawnAcceptedCount;
        private long _despawnAlreadyGoneCount;
        private long _despawnRejectedInvalidCount;

        private long _correctionAppliedCount;
        private long _correctionResetCount;
        private long _correctionStaleCount;
        private long _correctionMissingProjectileCount;
        private long _correctionRejectedInvalidCount;
        private long _correctionRejectedDefinitionCount;
        private long _correctionCapacityRejectedCount;

        private long _hitForwardedCount;
        private long _hitRejectedInvalidCount;
        private long _hitDroppedNoReceiverCount;

        /// <param name="world">Replica world the messages are applied to.</param>
        /// <param name="definitions">Local definition catalog. The wire carries only ids.</param>
        /// <param name="hitReceiver">
        /// Optional presentation sink for hit messages. When null, hits are counted as dropped
        /// the game simply has no impact presentation wired up, which is a valid configuration
        /// and must not throw.
        /// </param>
        public ProjectileWorldMessageHandler(
            ProjectileWorld world,
            IProjectileDefinitionResolver definitions,
            IProjectileHitReceiver hitReceiver = null)
        {
            _world = world ?? throw new ArgumentNullException(nameof(world));
            _definitions = definitions ?? throw new ArgumentNullException(nameof(definitions));
            _hitReceiver = hitReceiver;
        }

        public ProjectileWorld World => _world;

        // ---------------------------------------------------------------- spawn counters

        /// <summary>Spawn messages that created a projectile.</summary>
        public long SpawnAcceptedCount => _spawnAcceptedCount;

        /// <summary>Spawn messages rejected because the id was already live on this replica.</summary>
        public long SpawnRejectedDuplicateCount => _spawnRejectedDuplicateCount;

        /// <summary>Spawn messages rejected as malformed (zero id, non-finite vectors, negative tick).</summary>
        public long SpawnRejectedInvalidCount => _spawnRejectedInvalidCount;

        /// <summary>Spawn messages rejected because the local catalog had no matching definition id.</summary>
        public long SpawnRejectedDefinitionCount => _spawnRejectedDefinitionCount;

        /// <summary>Spawn messages rejected because the world was full.</summary>
        public long SpawnRejectedCapacityCount => _spawnRejectedCapacityCount;

        // ---------------------------------------------------------------- despawn counters

        /// <summary>Despawn messages that removed a live projectile.</summary>
        public long DespawnAcceptedCount => _despawnAcceptedCount;

        /// <summary>Despawn messages for an id that was already gone. Duplicates are expected.</summary>
        public long DespawnAlreadyGoneCount => _despawnAlreadyGoneCount;

        /// <summary>Despawn messages rejected as malformed.</summary>
        public long DespawnRejectedInvalidCount => _despawnRejectedInvalidCount;

        // ---------------------------------------------------------------- correction counters

        /// <summary>Corrections applied in place (no flag in the hard-reset set).</summary>
        public long CorrectionAppliedCount => _correctionAppliedCount;

        /// <summary>Corrections that rebuilt the projectile (<c>FullReset</c> / <c>HardSnap</c>).</summary>
        public long CorrectionResetCount => _correctionResetCount;

        /// <summary>Corrections dropped because the replica was already ahead of the snapshot tick.</summary>
        public long CorrectionStaleCount => _correctionStaleCount;

        /// <summary>Patch corrections for a projectile this replica does not have.</summary>
        public long CorrectionMissingProjectileCount => _correctionMissingProjectileCount;

        /// <summary>Corrections rejected as malformed.</summary>
        public long CorrectionRejectedInvalidCount => _correctionRejectedInvalidCount;

        /// <summary>Reset corrections rejected because the local catalog had no matching definition id.</summary>
        public long CorrectionRejectedDefinitionCount => _correctionRejectedDefinitionCount;

        /// <summary>Reset corrections rejected because the world was full.</summary>
        public long CorrectionCapacityRejectedCount => _correctionCapacityRejectedCount;

        // ---------------------------------------------------------------- hit counters

        /// <summary>Hit messages delivered to the presentation receiver.</summary>
        public long HitForwardedCount => _hitForwardedCount;

        /// <summary>Hit messages rejected as malformed.</summary>
        public long HitRejectedInvalidCount => _hitRejectedInvalidCount;

        /// <summary>Hit messages dropped because no receiver was injected.</summary>
        public long HitDroppedNoReceiverCount => _hitDroppedNoReceiverCount;

        // ---------------------------------------------------------------- spawn

        /// <summary>
        /// Creates the projectile described by an authoritative spawn message.
        ///
        /// Order of checks matters and is deliberate: duplicate before capacity. A full replica
        /// that receives a re-sent spawn for an id it already has must report a duplicate, not a
        /// capacity rejection, otherwise a dropped packet and a full world look identical in the
        /// counters, and the one that can be fixed (raise capacity) is indistinguishable from the
        /// one that never needed fixing.
        /// </summary>
        public bool TryHandleSpawn(in ProjectileSpawnMessage message)
        {
            if (!message.IsValid)
            {
                _spawnRejectedInvalidCount++;
                return false;
            }

            if (!_definitions.TryGetDefinition(message.DefinitionId, out ProjectileDefinition definition)
                || !definition.IsValid)
            {
                _spawnRejectedDefinitionCount++;
                return false;
            }

            if (_world.TryGetStateByNetworkEntityId(message.ProjectileEntityId, out _))
            {
                _spawnRejectedDuplicateCount++;
                return false;
            }

            if (_world.ActiveCount >= _world.Capacity)
            {
                _spawnRejectedCapacityCount++;
                return false;
            }

            var request = new ProjectileSpawnRequest(
                definition,
                message.OwnerEntityId,
                message.ProjectileEntityId,
                message.TargetEntityId,
                message.ServerTick,
                message.PredictionKey,
                message.Seed,
                message.Position.ToProjectileVector3(),
                message.Direction.ToProjectileVector3(),
                message.InitialVelocity.ToProjectileVector3());

            // Every rejection reason was already excluded above, so a false here means the world
            // ran out of slots between the check and the call (only possible if a caller is
            // mutating the world concurrently, which the single-writer contract forbids).
            if (!_world.TrySpawn(in request, out _))
            {
                _spawnRejectedCapacityCount++;
                return false;
            }

            _spawnAcceptedCount++;
            return true;
        }

        // ---------------------------------------------------------------- despawn

        /// <summary>
        /// Removes the projectile named by a despawn message. A despawn for an id that is already
        /// gone is counted as <see cref="DespawnAlreadyGoneCount"/> and returns false: that is the
        /// normal shape of a duplicated or late packet, not a protocol violation.
        /// </summary>
        public bool TryHandleDespawn(in ProjectileDespawnMessage message)
        {
            if (!message.IsValid)
            {
                _despawnRejectedInvalidCount++;
                return false;
            }

            if (!_world.TryDespawnByNetworkEntityId(message.ProjectileEntityId, out _))
            {
                _despawnAlreadyGoneCount++;
                return false;
            }

            _despawnAcceptedCount++;
            return true;
        }

        // ---------------------------------------------------------------- correction

        /// <summary>
        /// Applies an authoritative correction. The flag decides the mechanism:
        /// <see cref="ProjectileNetworkCorrectionFlags.FullReset"/> or
        /// <see cref="ProjectileNetworkCorrectionFlags.HardSnap"/> rebuild the projectile, every
        /// other value patches it in place.
        ///
        /// The definition catalog is only consulted on the reset path. A patch does not need
        /// kinematics, and the projectile it patches already has them, so requiring a catalog entry
        /// there would reject a correction for a reason that has nothing to do with the
        /// correction.
        ///
        /// A patch for an id this replica never spawned is dropped and counted as missing. It is
        /// tempting to create the projectile from the correction payload (it does carry position,
        /// age and tick), but that would make corrections a back door around spawn ordering: the
        /// replica would acquire a projectile whose bounce/pierce budget and spawn tick were never
        /// negotiated. Missing-state recovery belongs to a full-state request, not to a patch.
        /// </summary>
        public bool TryHandleCorrection(in ProjectileCorrectionMessage message)
        {
            if (!message.IsValid)
            {
                _correctionRejectedInvalidCount++;
                return false;
            }

            ProjectileSnapshot snapshot = message.Snapshot.ToProjectileSnapshot();

            if ((message.CorrectionFlags & HardResetFlags) != 0u)
            {
                if (!_definitions.TryGetDefinition(snapshot.DefinitionId.Value, out ProjectileDefinition definition))
                {
                    _correctionRejectedDefinitionCount++;
                    return false;
                }

                ProjectileSnapshotApplyResult resetResult =
                    _world.TryResetFromSnapshot(in snapshot, in definition, out _);
                switch (resetResult)
                {
                    case ProjectileSnapshotApplyResult.Spawned:
                        _correctionResetCount++;
                        return true;

                    case ProjectileSnapshotApplyResult.Invalid:
                        _correctionRejectedInvalidCount++;
                        return false;

                    case ProjectileSnapshotApplyResult.Failed:
                        _correctionCapacityRejectedCount++;
                        return false;

                    default:
                        _correctionRejectedInvalidCount++;
                        return false;
                }
            }

            switch (_world.TryApplySnapshot(in snapshot))
            {
                case ProjectileSnapshotApplyResult.Applied:
                    _correctionAppliedCount++;
                    return true;

                case ProjectileSnapshotApplyResult.Stale:
                    _correctionStaleCount++;
                    return false;

                case ProjectileSnapshotApplyResult.Missing:
                    _correctionMissingProjectileCount++;
                    return false;

                default:
                    _correctionRejectedInvalidCount++;
                    return false;
            }
        }

        // ---------------------------------------------------------------- hit

        /// <summary>
        /// Forwards a replicated hit to the presentation receiver. Does not read or write
        /// <see cref="ProjectileWorld"/>: the impact already happened on the authority, and a
        /// client-side mutation would be a second outcome.
        /// </summary>
        public bool TryHandleHit(in ProjectileHitMessage message)
        {
            if (!message.IsValid)
            {
                _hitRejectedInvalidCount++;
                return false;
            }

            if (_hitReceiver == null)
            {
                _hitDroppedNoReceiverCount++;
                return false;
            }

            _hitReceiver.OnProjectileHit(in message);
            _hitForwardedCount++;
            return true;
        }
    }
}
