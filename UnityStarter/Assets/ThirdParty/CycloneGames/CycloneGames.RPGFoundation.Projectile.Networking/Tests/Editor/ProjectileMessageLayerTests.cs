using CycloneGames.Networking;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Networking.Tests.Editor
{
    /// <summary>
    /// Covers <see cref="ProjectileWorldMessageHandler"/>: the only place where a replicated
    /// spawn, despawn, correction or hit becomes something the local world can see.
    ///
    /// The four policy decisions these tests lock in:
    /// <list type="number">
    /// <item>a duplicate spawn is rejected, not applied (a re-sent packet must not rewind or
    /// redefine a live projectile);</item>
    /// <item>a despawn for an already-dead id is counted, not an error;</item>
    /// <item>a correction's flags decide patch vs rebuild — never the payload;</item>
    /// <item>a hit never mutates the world, it only forwards.</item>
    /// </list>
    ///
    /// No timing assertions: a busy machine must not be able to turn these red or green.
    /// </summary>
    public sealed class ProjectileMessageLayerTests
    {
        private const int KnownDefinitionId = 7301;
        private const int SecondDefinitionId = 7302;
        private const int UnknownDefinitionId = 9999;
        private const ulong OwnerEntityId = 42UL;

        private static readonly ProjectileSpaceProfile Space = ProjectileSpaceProfile.Full3D();

        // ---------------------------------------------------------------- spawn

        [Test]
        public void Spawn_CreatesTheProjectileFromTheMessage()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage message = SpawnMessage(1UL, KnownDefinitionId, tick: 10, x: 100f);
            Assert.That(handler.TryHandleSpawn(in message), Is.True);
            Assert.That(handler.SpawnAcceptedCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(1));

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState state), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(100f).Within(0.001f));
            Assert.That(state.OwnerEntityId, Is.EqualTo(OwnerEntityId));
            Assert.That(state.CurrentTick, Is.EqualTo(10), "the spawn tick must survive the round trip");
            Assert.That(state.DefinitionId.Value, Is.EqualTo(KnownDefinitionId));
        }

        [Test]
        public void Spawn_RejectsDuplicateIdWithoutOverwritingTheLiveProjectile()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage first = SpawnMessage(1UL, KnownDefinitionId, tick: 10, x: 100f);
            Assert.That(handler.TryHandleSpawn(in first), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState before), Is.True);

            // Same id, different position and tick: an overwrite would move the projectile.
            ProjectileSpawnMessage duplicate = SpawnMessage(1UL, SecondDefinitionId, tick: 3, x: 999f);
            Assert.That(handler.TryHandleSpawn(in duplicate), Is.False);
            Assert.That(handler.SpawnRejectedDuplicateCount, Is.EqualTo(1L));
            Assert.That(handler.SpawnAcceptedCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(1), "a duplicate must not add a second projectile");

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState after), Is.True);
            Assert.That(after.Handle, Is.EqualTo(before.Handle), "the live handle must not be replaced");
            Assert.That(after.Position.X, Is.EqualTo(100f).Within(0.001f), "the live position must not be rewound");
            Assert.That(after.CurrentTick, Is.EqualTo(10));
            Assert.That(after.DefinitionId.Value, Is.EqualTo(KnownDefinitionId),
                "a duplicate must not silently redefine the projectile");
        }

        [Test]
        public void Spawn_RejectsUnknownDefinition()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage message = SpawnMessage(1UL, UnknownDefinitionId, tick: 10, x: 0f);
            Assert.That(handler.TryHandleSpawn(in message), Is.False);
            Assert.That(handler.SpawnRejectedDefinitionCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Spawn_RejectsWhenTheWorldIsFull()
        {
            ProjectileWorld world = CreateWorld(2);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage first = SpawnMessage(1UL, KnownDefinitionId, tick: 1, x: 0f);
            ProjectileSpawnMessage second = SpawnMessage(2UL, KnownDefinitionId, tick: 1, x: 0f);
            ProjectileSpawnMessage third = SpawnMessage(3UL, KnownDefinitionId, tick: 1, x: 0f);

            Assert.That(handler.TryHandleSpawn(in first), Is.True);
            Assert.That(handler.TryHandleSpawn(in second), Is.True);
            Assert.That(handler.TryHandleSpawn(in third), Is.False);

            Assert.That(handler.SpawnRejectedCapacityCount, Is.EqualTo(1L));
            Assert.That(handler.SpawnRejectedDuplicateCount, Is.EqualTo(0L),
                "a full world must not be misreported as a duplicate");
            Assert.That(world.ActiveCount, Is.EqualTo(2));

            // Freeing a slot must make the same message acceptable: capacity, not a sticky failure.
            Assert.That(handler.TryHandleDespawn(DespawnMessage(1UL)), Is.True);
            Assert.That(handler.TryHandleSpawn(in third), Is.True);
            Assert.That(handler.SpawnAcceptedCount, Is.EqualTo(3L));
        }

        [Test]
        public void Spawn_RejectsMalformedMessage()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage zeroId = SpawnMessage(0UL, KnownDefinitionId, tick: 1, x: 0f);
            Assert.That(handler.TryHandleSpawn(in zeroId), Is.False);

            ProjectileSpawnMessage negativeTick = SpawnMessage(5UL, KnownDefinitionId, tick: -1, x: 0f);
            Assert.That(handler.TryHandleSpawn(in negativeTick), Is.False);

            ProjectileSpawnMessage nonFinite = new ProjectileSpawnMessage(
                6UL, OwnerEntityId, 0UL, KnownDefinitionId, 1, 0, 0u, 0u,
                new NetworkVector3(float.NaN, 0f, 0f),
                new NetworkVector3(0f, 0f, 1f),
                NetworkVector3.Zero);
            Assert.That(handler.TryHandleSpawn(in nonFinite), Is.False);

            Assert.That(handler.SpawnRejectedInvalidCount, Is.EqualTo(3L));
            Assert.That(handler.SpawnAcceptedCount, Is.EqualTo(0L));
            Assert.That(world.ActiveCount, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- despawn

        [Test]
        public void Despawn_RemovesTheProjectile()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage spawn = SpawnMessage(1UL, KnownDefinitionId, tick: 1, x: 0f);
            Assert.That(handler.TryHandleSpawn(in spawn), Is.True);

            Assert.That(handler.TryHandleDespawn(DespawnMessage(1UL)), Is.True);
            Assert.That(handler.DespawnAcceptedCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(0));
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out _), Is.False);
        }

        [Test]
        public void Despawn_CountsAnAlreadyDeadProjectileWithoutFailing()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage spawn = SpawnMessage(1UL, KnownDefinitionId, tick: 1, x: 0f);
            Assert.That(handler.TryHandleSpawn(in spawn), Is.True);

            // Unreliable delivery duplicates messages; the second one is expected, not an error.
            Assert.That(handler.TryHandleDespawn(DespawnMessage(1UL)), Is.True);
            Assert.That(handler.TryHandleDespawn(DespawnMessage(1UL)), Is.False);
            Assert.That(handler.DespawnAlreadyGoneCount, Is.EqualTo(1L));
            Assert.That(handler.DespawnAcceptedCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(0));

            ProjectileDespawnMessage zeroId = new ProjectileDespawnMessage(0UL, 1, 0, 0);
            Assert.That(handler.TryHandleDespawn(in zeroId), Is.False);
            Assert.That(handler.DespawnRejectedInvalidCount, Is.EqualTo(1L),
                "a malformed despawn must not be counted as an already-dead one");
        }

        // ---------------------------------------------------------------- correction

        [Test]
        public void Correction_PatchesInPlaceWhenNoHardResetFlagIsSet()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage spawn = SpawnMessage(1UL, KnownDefinitionId, tick: 10, x: 100f);
            Assert.That(handler.TryHandleSpawn(in spawn), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState before), Is.True);

            ProjectileCorrectionMessage correction = CorrectionMessage(
                1UL, KnownDefinitionId, tick: 12, x: 120f,
                ProjectileNetworkCorrectionFlags.Transform | ProjectileNetworkCorrectionFlags.Velocity);
            Assert.That(handler.TryHandleCorrection(in correction), Is.True);

            Assert.That(handler.CorrectionAppliedCount, Is.EqualTo(1L));
            Assert.That(handler.CorrectionResetCount, Is.EqualTo(0L));

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState after), Is.True);
            Assert.That(after.Position.X, Is.EqualTo(120f).Within(0.001f));
            Assert.That(after.CurrentTick, Is.EqualTo(12));
            Assert.That(after.Handle, Is.EqualTo(before.Handle), "a patch must keep the handle");
        }

        [Test]
        public void Correction_RebuildsTheProjectileForHardSnapAndFullReset()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage spawn = SpawnMessage(1UL, KnownDefinitionId, tick: 10, x: 100f);
            Assert.That(handler.TryHandleSpawn(in spawn), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState beforeHardSnap), Is.True);

            ProjectileCorrectionMessage hardSnap = CorrectionMessage(
                1UL, KnownDefinitionId, tick: 20, x: 200f, ProjectileNetworkCorrectionFlags.HardSnap);
            Assert.That(handler.TryHandleCorrection(in hardSnap), Is.True);

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState afterHardSnap), Is.True);
            Assert.That(afterHardSnap.Handle, Is.Not.EqualTo(beforeHardSnap.Handle),
                "a hard snap must rebuild, and a rebuild always changes the handle");
            Assert.That(afterHardSnap.Position.X, Is.EqualTo(200f).Within(0.001f));

            ProjectileCorrectionMessage fullReset = CorrectionMessage(
                1UL, KnownDefinitionId, tick: 30, x: 300f, ProjectileNetworkCorrectionFlags.FullReset);
            Assert.That(handler.TryHandleCorrection(in fullReset), Is.True);

            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState afterFullReset), Is.True);
            Assert.That(afterFullReset.Handle, Is.Not.EqualTo(afterHardSnap.Handle));
            Assert.That(afterFullReset.Position.X, Is.EqualTo(300f).Within(0.001f));

            Assert.That(handler.CorrectionResetCount, Is.EqualTo(2L));
            Assert.That(handler.CorrectionAppliedCount, Is.EqualTo(0L),
                "a hard reset must never be reported as a patch");
            Assert.That(world.ActiveCount, Is.EqualTo(1), "a rebuild must not leak a second projectile");
        }

        [Test]
        public void Correction_PatchForAnUnknownProjectileIsCountedAndCreatesNothing()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileCorrectionMessage orphan = CorrectionMessage(
                77UL, KnownDefinitionId, tick: 12, x: 120f, ProjectileNetworkCorrectionFlags.Transform);
            Assert.That(handler.TryHandleCorrection(in orphan), Is.False);
            Assert.That(handler.CorrectionMissingProjectileCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(0),
                "a patch must not become a covert spawn channel");
        }

        [Test]
        public void Correction_CountsStaleAndMalformedAndUnknownDefinition()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileSpawnMessage spawn = SpawnMessage(1UL, KnownDefinitionId, tick: 20, x: 100f);
            Assert.That(handler.TryHandleSpawn(in spawn), Is.True);

            // Older tick than the local one: unreliable delivery can reorder, state must not rewind.
            ProjectileCorrectionMessage stale = CorrectionMessage(
                1UL, KnownDefinitionId, tick: 15, x: 50f, ProjectileNetworkCorrectionFlags.Transform);
            Assert.That(handler.TryHandleCorrection(in stale), Is.False);
            Assert.That(handler.CorrectionStaleCount, Is.EqualTo(1L));
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState afterStale), Is.True);
            Assert.That(afterStale.Position.X, Is.EqualTo(100f).Within(0.001f));

            // Mismatched inner snapshot id: the envelope is invalid by contract.
            ProjectileCorrectionMessage mismatched = new ProjectileCorrectionMessage(
                1UL, 20, 0, (uint)ProjectileNetworkCorrectionFlags.Transform,
                SnapshotMessage(2UL, KnownDefinitionId, tick: 25, x: 0f));
            Assert.That(handler.TryHandleCorrection(in mismatched), Is.False);
            Assert.That(handler.CorrectionRejectedInvalidCount, Is.EqualTo(1L));

            // A rebuild needs kinematics, so an unknown definition id is rejected and counted.
            ProjectileCorrectionMessage unknownDefinition = CorrectionMessage(
                1UL, UnknownDefinitionId, tick: 30, x: 0f, ProjectileNetworkCorrectionFlags.HardSnap);
            Assert.That(handler.TryHandleCorrection(in unknownDefinition), Is.False);
            Assert.That(handler.CorrectionRejectedDefinitionCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(1));
        }

        // ---------------------------------------------------------------- hit

        [Test]
        public void Hit_ForwardsOnceAndLeavesTheWorldUntouched()
        {
            ProjectileWorld world = CreateWorld(8);
            var receiver = new RecordingHitReceiver();
            var handler = new ProjectileWorldMessageHandler(world, new MapDefinitionResolver(), receiver);

            ProjectileSpawnMessage spawn = SpawnMessage(1UL, KnownDefinitionId, tick: 10, x: 100f);
            Assert.That(handler.TryHandleSpawn(in spawn), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState before), Is.True);

            ProjectileHitMessage hit = HitMessage(1UL, KnownDefinitionId, tick: 11, terminal: true);
            Assert.That(handler.TryHandleHit(in hit), Is.True);

            Assert.That(receiver.CallCount, Is.EqualTo(1));
            Assert.That(receiver.LastProjectileEntityId, Is.EqualTo(1UL));
            Assert.That(receiver.LastIsTerminal, Is.True);
            Assert.That(receiver.LastTick, Is.EqualTo(11));
            Assert.That(handler.HitForwardedCount, Is.EqualTo(1L));

            // The world must be bit-for-bit unaffected: a terminal hit would have despawned it.
            Assert.That(world.ActiveCount, Is.EqualTo(1), "a client hit must not despawn anything");
            Assert.That(world.Stats.TotalDespawnCount, Is.EqualTo(0L));
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out ProjectileState after), Is.True);
            Assert.That(after.Handle, Is.EqualTo(before.Handle));
            Assert.That(after.Position.X, Is.EqualTo(before.Position.X));
            Assert.That(after.CurrentTick, Is.EqualTo(before.CurrentTick));
        }

        [Test]
        public void Hit_IsForwardedEvenWhenTheProjectileIsNotInTheLocalWorld()
        {
            ProjectileWorld world = CreateWorld(8);
            var receiver = new RecordingHitReceiver();
            var handler = new ProjectileWorldMessageHandler(world, new MapDefinitionResolver(), receiver);

            ProjectileHitMessage hit = HitMessage(404UL, KnownDefinitionId, tick: 5, terminal: false);
            Assert.That(handler.TryHandleHit(in hit), Is.True);
            Assert.That(receiver.CallCount, Is.EqualTo(1),
                "hit presentation must not depend on the local world owning the projectile");
            Assert.That(world.ActiveCount, Is.EqualTo(0));
        }

        [Test]
        public void Hit_WithoutReceiverOrWithMalformedMessageIsCounted()
        {
            ProjectileWorld world = CreateWorld(8);
            var handler = CreateHandler(world);

            ProjectileHitMessage valid = HitMessage(1UL, KnownDefinitionId, tick: 5, terminal: false);
            Assert.That(handler.TryHandleHit(in valid), Is.False);
            Assert.That(handler.HitDroppedNoReceiverCount, Is.EqualTo(1L),
                "no impact presentation wired up is a valid configuration, not a crash");

            ProjectileHitMessage nonFinite = new ProjectileHitMessage(
                1UL, OwnerEntityId, 0UL, KnownDefinitionId, 0, 5, 0, false,
                new NetworkVector3(float.PositiveInfinity, 0f, 0f),
                new NetworkVector3(0f, 0f, 1f),
                NetworkVector3.Zero);
            Assert.That(handler.TryHandleHit(in nonFinite), Is.False);
            Assert.That(handler.HitRejectedInvalidCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(0));
        }

        // ---------------------------------------------------------------- helpers

        private static ProjectileWorld CreateWorld(int capacity)
        {
            var config = new ProjectileWorldConfig(capacity, 64, 8, in Space, 2, 0f, 1);
            return new ProjectileWorld(in config);
        }

        private static ProjectileWorldMessageHandler CreateHandler(ProjectileWorld world)
        {
            return new ProjectileWorldMessageHandler(world, new MapDefinitionResolver());
        }

        private static ProjectileSpawnMessage SpawnMessage(ulong id, int definitionId, int tick, float x)
        {
            return new ProjectileSpawnMessage(
                id,
                OwnerEntityId,
                targetEntityId: 0UL,
                definitionId,
                serverTick: tick,
                predictionKey: 0,
                seed: 0u,
                lifecycleFlags: (uint)ProjectileLifecycleFlags.DespawnOnHit,
                position: new NetworkVector3(x, 0f, 0f),
                direction: new NetworkVector3(0f, 0f, 1f),
                initialVelocity: new NetworkVector3(0f, 0f, 10f));
        }

        private static ProjectileDespawnMessage DespawnMessage(ulong id)
        {
            return new ProjectileDespawnMessage(id, serverTick: 30, sequence: 0, reason: 0);
        }

        private static ProjectileSnapshotMessage SnapshotMessage(ulong id, int definitionId, int tick, float x)
        {
            return new ProjectileSnapshotMessage(
                id,
                OwnerEntityId,
                targetEntityId: 0UL,
                definitionId,
                serverTick: tick,
                sequence: 0,
                predictionKey: 0,
                lifecycleFlags: (uint)ProjectileLifecycleFlags.DespawnOnHit,
                age: tick * 0.1f,
                radius: 0.2f,
                position: new NetworkVector3(x, 0f, 0f),
                previousPosition: new NetworkVector3(x - 1f, 0f, 0f),
                velocity: new NetworkVector3(10f, 0f, 0f));
        }

        private static ProjectileCorrectionMessage CorrectionMessage(
            ulong id,
            int definitionId,
            int tick,
            float x,
            ProjectileNetworkCorrectionFlags flags)
        {
            return new ProjectileCorrectionMessage(
                id,
                serverTick: tick,
                sequence: 0,
                correctionFlags: (uint)flags,
                SnapshotMessage(id, definitionId, tick, x));
        }

        private static ProjectileHitMessage HitMessage(ulong id, int definitionId, int tick, bool terminal)
        {
            return new ProjectileHitMessage(
                id,
                OwnerEntityId,
                targetEntityId: 7UL,
                definitionId,
                effectPayloadId: 0,
                serverTick: tick,
                predictionKey: 0,
                isTerminal: terminal,
                position: new NetworkVector3(0f, 0f, 5f),
                normal: new NetworkVector3(0f, 0f, -1f),
                velocity: new NetworkVector3(0f, 0f, 10f));
        }

        private static ProjectileDefinition Definition(int definitionId)
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(definitionId),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit | ProjectileLifecycleFlags.IgnoreOwner,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 100f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 4,
                collisionLayerMask: 0,
                effectPayloadId: 0);
        }

        /// <summary>Two-entry catalog; anything else is "unknown definition".</summary>
        private sealed class MapDefinitionResolver : IProjectileDefinitionResolver
        {
            private readonly ProjectileDefinition _known = Definition(KnownDefinitionId);
            private readonly ProjectileDefinition _second = Definition(SecondDefinitionId);

            public bool TryGetDefinition(int definitionId, out ProjectileDefinition definition)
            {
                if (definitionId == _known.DefinitionId.Value)
                {
                    definition = _known;
                    return true;
                }

                if (definitionId == _second.DefinitionId.Value)
                {
                    definition = _second;
                    return true;
                }

                definition = default;
                return false;
            }
        }

        private sealed class RecordingHitReceiver : IProjectileHitReceiver
        {
            public int CallCount;
            public ulong LastProjectileEntityId;
            public int LastTick;
            public bool LastIsTerminal;

            public void OnProjectileHit(in ProjectileHitMessage message)
            {
                CallCount++;
                LastProjectileEntityId = message.ProjectileEntityId;
                LastTick = message.ServerTick;
                LastIsTerminal = message.IsTerminal;
            }
        }
    }
}
