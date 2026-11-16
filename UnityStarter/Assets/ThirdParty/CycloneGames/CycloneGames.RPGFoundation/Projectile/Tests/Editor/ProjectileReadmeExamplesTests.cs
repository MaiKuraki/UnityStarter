using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    /// <summary>
    /// Executes the examples published in the package README. The code here is deliberately
    /// copied from the documentation rather than written for the test, so a change to either
    /// one fails the build instead of leaving a stale example behind.
    /// </summary>
    public sealed class ProjectileReadmeExamplesTests
    {
        [Test]
        public void FirstProjectile_MovesAfterOneStep()
        {
            var space = ProjectileSpaceProfile.Full3D();
            var config = new ProjectileWorldConfig(
                capacity: 1024,
                eventCapacity: 256,
                collisionHitCapacity: 8,
                space: in space,
                maxCollisionIterations: 4,
                maxDeltaTime: 1f / 60f,
                maxSubSteps: 4);

            var world = new ProjectileWorld(in config);

            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(1),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit,
                initialSpeed: 20f,
                maxSpeed: 20f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 5f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: ~0,
                effectPayloadId: 0);

            var request = new ProjectileSpawnRequest(
                definition,
                ownerEntityId: 100,
                networkEntityId: 5001,
                targetEntityId: 0,
                spawnTick: 0,
                predictionKey: 0,
                seed: 0,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(0f, 0f, 1f),
                initialVelocity: ProjectileVector3.Zero);

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            world.Step(1f / 60f, tick: 1);

            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.Position.Z, Is.GreaterThan(0f), "the example claims the projectile advanced");
            Assert.That(state.Position.Z, Is.EqualTo(20f / 60f).Within(0.001f));
        }

        [Test]
        public void CapacityRejection_ReturnsFalseInsteadOfGrowing()
        {
            ProjectileWorld world = new ProjectileWorld(
                new ProjectileWorldConfig(2, 8, 4, ProjectileSpaceProfile.Full3D()));

            ProjectileDefinition definition = new ProjectileDefinition(
                new ProjectileDefinitionId(2),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                10f, 10f, 0f, 0f, 0.2f, 100f, 0f, 0f, 0, 0, 0, 0);

            for (int i = 1; i <= 2; i++)
            {
                var accepted = new ProjectileSpawnRequest(
                    definition, 1, (ulong)i, 0, 0, 0, 0,
                    ProjectileVector3.Zero, new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);
                Assert.That(world.TrySpawn(in accepted, out _), Is.True);
            }

            var rejected = new ProjectileSpawnRequest(
                definition, 1, 3, 0, 0, 0, 0,
                ProjectileVector3.Zero, new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);

            Assert.That(world.TrySpawn(in rejected, out _), Is.False);
            Assert.That(world.ActiveCount, Is.EqualTo(2));
            Assert.That(world.Stats.TotalSpawnRejectedCapacityCount, Is.EqualTo(1L));
        }

        [Test]
        public void StaleHandle_DoesNotResolveToAReusedSlot()
        {
            ProjectileWorld world = new ProjectileWorld(
                new ProjectileWorldConfig(1, 8, 4, ProjectileSpaceProfile.Full3D()));

            ProjectileDefinition definition = new ProjectileDefinition(
                new ProjectileDefinitionId(3),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                10f, 10f, 0f, 0f, 0.2f, 100f, 0f, 0f, 0, 0, 0, 0);

            var first = new ProjectileSpawnRequest(
                definition, 1, 10UL, 0, 0, 0, 0,
                ProjectileVector3.Zero, new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);
            world.TrySpawn(in first, out ProjectileHandle oldHandle);
            world.Despawn(oldHandle);

            var second = new ProjectileSpawnRequest(
                definition, 1, 11UL, 0, 0, 0, 0,
                ProjectileVector3.Zero, new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);
            Assert.That(world.TrySpawn(in second, out ProjectileHandle newHandle), Is.True);

            Assert.That(world.TryGetState(oldHandle, out _), Is.False,
                "a handle from a despawned projectile must not resolve, even after slot reuse");
            Assert.That(world.TryGetState(newHandle, out _), Is.True);
        }

        [Test]
        public void ActiveStates_SeesEveryLiveProjectile()
        {
            ProjectileWorld world = new ProjectileWorld(
                new ProjectileWorldConfig(8, 16, 4, ProjectileSpaceProfile.Full3D()));

            ProjectileDefinition definition = new ProjectileDefinition(
                new ProjectileDefinitionId(4),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                10f, 10f, 0f, 0f, 0.2f, 100f, 0f, 0f, 0, 0, 0, 0);

            for (int i = 0; i < 5; i++)
            {
                var request = new ProjectileSpawnRequest(
                    definition, 1, (ulong)(100 + i), 0, 0, 0, 0,
                    new ProjectileVector3(i, 0f, 0f), new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);
                world.TrySpawn(in request, out _);
            }

            ReadOnlySpan<ProjectileState> active = world.ActiveStates;
            Assert.That(active.Length, Is.EqualTo(world.ActiveCount));
            Assert.That(active.Length, Is.EqualTo(5));
        }

        [Test]
        public void ReplicationCaptureApplyRestore_RoundTrips()
        {
            ProjectileDefinition definition = new ProjectileDefinition(
                new ProjectileDefinitionId(5),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit,
                12f, 12f, 0f, 0f, 0.2f, 100f, 0f, 0f, 0, 0, 0, 0);

            var world = new ProjectileWorld(new ProjectileWorldConfig(32, 64, 8, ProjectileSpaceProfile.Full3D()));
            for (int i = 1; i <= 8; i++)
            {
                var request = new ProjectileSpawnRequest(
                    definition, 1, (ulong)i, 0, 0, 0, 0,
                    new ProjectileVector3(0f, 0f, i), new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);
                world.TrySpawn(in request, out _);
            }

            for (int step = 0; step < 30; step++)
            {
                world.Step(1f / 60f, step + 1, null, null);
            }

            var buffer = new ProjectileSnapshot[world.Capacity];
            int count = world.CaptureSnapshots(buffer);
            Assert.That(count, Is.EqualTo(8));

            // Replica side: patch a known projectile.
            ProjectileSnapshot first = buffer[0];
            Assert.That(world.TryApplySnapshot(in first), Is.EqualTo(ProjectileSnapshotApplyResult.Applied));

            // Replica side: create one it does not have.
            ProjectileSnapshot extra = new ProjectileSnapshot(
                999UL, 1UL, 0UL, new ProjectileDefinitionId(5), ProjectileLifecycleFlags.DespawnOnHit,
                30, 0, 0.5f, 0.2f,
                new ProjectileVector3(1f, 0f, 1f), new ProjectileVector3(1f, 0f, 0f), new ProjectileVector3(0f, 0f, 12f));
            Assert.That(world.TrySpawnFromSnapshot(in extra, in definition, out _), Is.True);

            // Rollback: replace the whole world with the captured tick.
            var captured = new ReadOnlySpan<ProjectileSnapshot>(buffer, 0, count);
            ProjectileRestoreResult restored = world.TryRestoreStates(
                captured, ProjectileRestoreMode.Replace, null);

            Assert.That(restored.Succeeded, Is.True);
            Assert.That(world.ActiveCount, Is.EqualTo(8), "Replace removes the projectile the batch omitted");
            Assert.That(world.LatestTick, Is.EqualTo(30));
        }

        [Test]
        public void NonFiniteDefinition_IsReapedAndCounted()
        {
            var world = new ProjectileWorld(new ProjectileWorldConfig(4, 8, 4, ProjectileSpaceProfile.Full3D()));

            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(6),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                initialSpeed: 3.0e38f,
                maxSpeed: 3.0e38f,
                acceleration: 3.0e38f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 1000000f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: 0,
                effectPayloadId: 0);

            Assert.That(definition.MayOverflow, Is.True);

            var request = new ProjectileSpawnRequest(
                definition, 1, 7UL, 0, 0, 0, 0,
                ProjectileVector3.Zero, new ProjectileVector3(0f, 0f, 1f), ProjectileVector3.Zero);
            world.TrySpawn(in request, out _);

            for (int step = 0; step < 10; step++)
            {
                world.Step(1f, step, null, null);
            }

            Assert.That(world.NonFiniteRejectedCount, Is.EqualTo(1L));
            Assert.That(world.ActiveCount, Is.EqualTo(0));
        }
    }
}
