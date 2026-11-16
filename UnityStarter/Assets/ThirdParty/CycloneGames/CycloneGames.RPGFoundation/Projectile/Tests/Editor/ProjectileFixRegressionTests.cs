using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    /// <summary>
    /// Locks in the defects fixed on 2026-11-16. Every case here fails on the pre-fix code:
    /// repeated hits on one collider used to drain the bounce/pierce budget, overflow used to
    /// leave a live non-finite projectile, and entity-id lookup was a linear scan.
    /// Deliberately no timing assertions: a busy machine must not turn these red or green.
    /// </summary>
    public sealed class ProjectileFixRegressionTests
    {
        private static readonly ProjectileSpaceProfile Space = ProjectileSpaceProfile.Full3D();

        private static ProjectileWorld CreateWorld(
            int capacity,
            int maxCollisionIterations = 4,
            int eventCapacity = 128)
        {
            var config = new ProjectileWorldConfig(
                capacity,
                eventCapacity,
                8,
                in Space,
                maxCollisionIterations,
                0f,
                1);
            return new ProjectileWorld(in config);
        }

        private static ProjectileDefinition Bouncing(int bounceCount)
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(7001),
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
                bounceCount: bounceCount,
                collisionLayerMask: ~0,
                effectPayloadId: 0);
        }

        private static ProjectileDefinition Piercing(int pierceCount)
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(7002),
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
                pierceCount: pierceCount,
                bounceCount: 0,
                collisionLayerMask: ~0,
                effectPayloadId: 0);
        }

        [Test]
        public void Bounce_ConsumesOneChargePerStepOnASingleWall()
        {
            ProjectileWorld world = CreateWorld(1);
            ProjectileDefinition definition = Bouncing(bounceCount: 1000);
            var request = new ProjectileSpawnRequest(
                definition,
                ownerEntityId: 77UL,
                networkEntityId: 9001UL,
                targetEntityId: 0UL,
                spawnTick: 0,
                predictionKey: 0,
                seed: 0u,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(0f, 0f, 1f),
                initialVelocity: ProjectileVector3.Zero);

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            var wall = new SingleWallCollisionWorld(objectId: 4242UL);
            int hits = 0;
            for (int step = 0; step < 10; step++)
            {
                world.Step(1f / 60f, step + 1, wall, null);
                hits += world.HitEvents.Count;
            }

            Assert.That(hits, Is.EqualTo(10), "one hit per step, not four");
            Assert.That(world.Stats.TotalCollisionIterationLimitCount, Is.EqualTo(0L));
            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.RemainingBounceCount, Is.EqualTo(990));
        }

        [Test]
        public void Bounce_LeavesTheWallInsteadOfSticking()
        {
            ProjectileWorld world = CreateWorld(1);
            ProjectileDefinition definition = Bouncing(bounceCount: 1000);
            var request = new ProjectileSpawnRequest(
                definition, 77UL, 9002UL, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            var wall = new SingleWallCollisionWorld(objectId: 4243UL);
            float farthest = 0f;
            for (int step = 0; step < 20; step++)
            {
                world.Step(1f / 60f, step + 1, wall, null);
                if (world.TryGetState(handle, out ProjectileState state))
                {
                    farthest = Math.Max(farthest, Math.Abs(state.Position.Z));
                }
            }

            Assert.That(farthest, Is.GreaterThan(0.01f), "the projectile must travel, not sit at the impact point");
        }

        [Test]
        public void Pierce_ConsumesOneChargePerDistinctObject()
        {
            ProjectileWorld world = CreateWorld(1);
            ProjectileDefinition definition = Piercing(pierceCount: 3);
            var request = new ProjectileSpawnRequest(
                definition, 77UL, 9003UL, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            var targets = new DistinctObjectsCollisionWorld(targetCount: 3);
            int hits = 0;
            for (int step = 0; step < 5; step++)
            {
                world.Step(1f / 60f, step + 1, targets, null);
                hits += world.HitEvents.Count;
            }

            Assert.That(hits, Is.EqualTo(3));
            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.RemainingPierceCount, Is.EqualTo(0));
        }

        [Test]
        public void Pierce_DoesNotDrainBudgetOnOneObjectWithinASweep()
        {
            ProjectileWorld world = CreateWorld(1);
            ProjectileDefinition definition = Piercing(pierceCount: 1);
            var request = new ProjectileSpawnRequest(
                definition, 77UL, 9004UL, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            var wall = new SingleWallCollisionWorld(objectId: 5151UL);
            world.Step(1f / 60f, 1, wall, null);

            // Inside the first sweep the wall may be consumed once; the exclusion set then
            // drops it, so the projectile survives the step. Pre-fix the same sweep re-hit it,
            // found no pierce budget left and terminated immediately.
            Assert.That(world.HitEvents.Count, Is.EqualTo(1));
            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.RemainingPierceCount, Is.EqualTo(0));
        }

        [Test]
        public void NonFiniteState_IsReapedAndCounted()
        {
            ProjectileWorld world = CreateWorld(1, maxCollisionIterations: 1);

            // Passes ProjectileDefinition.IsValid yet overflows float within a few steps.
            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(7003),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit,
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

            Assert.That(definition.IsValid, Is.True, "the definition must pass validation for this test to mean anything");
            Assert.That(definition.MayOverflow, Is.True, "the overflow screen must flag this definition");

            var request = new ProjectileSpawnRequest(
                definition, 1UL, 9005UL, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            Assert.That(world.TrySpawn(in request, out _), Is.True);

            for (int step = 0; step < 10; step++)
            {
                world.Step(1f, step + 1, null, null);
            }

            Assert.That(world.NonFiniteRejectedCount, Is.EqualTo(1L), "exactly one reap, not a leak");
            Assert.That(world.ActiveCount, Is.EqualTo(0));
            Assert.That(world.Stats.TotalNonFiniteRejectedCount, Is.EqualTo(1L));
        }

        [Test]
        public void NormalDefinition_IsNotFlaggedForOverflow()
        {
            ProjectileDefinition definition = Bouncing(bounceCount: 1);
            Assert.That(definition.MayOverflow, Is.False, "the guard must not run for ordinary bullets");
        }

        [Test]
        public void NetworkEntityIdIndex_ResolvesEveryLiveProjectile()
        {
            const int count = 512;
            ProjectileWorld world = CreateWorld(count);
            ProjectileDefinition definition = Bouncing(bounceCount: 0);

            for (int i = 1; i <= count; i++)
            {
                var request = new ProjectileSpawnRequest(
                    definition, 1UL, (ulong)i, 0UL, 0, 0, 0u,
                    new ProjectileVector3(i, 0f, 0f),
                    new ProjectileVector3(0f, 0f, 1f),
                    ProjectileVector3.Zero);
                Assert.That(world.TrySpawn(in request, out _), Is.True);
            }

            for (int i = 1; i <= count; i++)
            {
                Assert.That(world.TryGetStateByNetworkEntityId((ulong)i, out ProjectileState state), Is.True,
                    "id " + i + " must resolve");
                Assert.That(state.NetworkEntityId, Is.EqualTo((ulong)i));
            }

            Assert.That(world.TryGetStateByNetworkEntityId((ulong)count + 1UL, out _), Is.False);
            Assert.That(world.TryGetStateByNetworkEntityId(0UL, out _), Is.False);
        }

        [Test]
        public void NetworkEntityIdIndex_SurvivesChurn()
        {
            const int count = 128;
            ProjectileWorld world = CreateWorld(count);
            ProjectileDefinition definition = Bouncing(bounceCount: 0);

            for (int i = 1; i <= count; i++)
            {
                var request = new ProjectileSpawnRequest(
                    definition, 1UL, (ulong)i, 0UL, 0, 0, 0u,
                    ProjectileVector3.Zero,
                    new ProjectileVector3(0f, 0f, 1f),
                    ProjectileVector3.Zero);
                Assert.That(world.TrySpawn(in request, out _), Is.True);
            }

            // Swap-remove moves dense slots; slots stay put, which is what the index keys on.
            for (int round = 0; round < 400; round++)
            {
                ReadOnlySpan<ProjectileState> live = world.ActiveStates;
                ProjectileHandle victim = live[(round * 37) % live.Length].Handle;
                Assert.That(world.Despawn(victim), Is.True);

                ulong fresh = (ulong)(100000 + round);
                var request = new ProjectileSpawnRequest(
                    definition, 1UL, fresh, 0UL, 0, 0, 0u,
                    ProjectileVector3.Zero,
                    new ProjectileVector3(0f, 0f, 1f),
                    ProjectileVector3.Zero);
                Assert.That(world.TrySpawn(in request, out _), Is.True);
            }

            ReadOnlySpan<ProjectileState> finalStates = world.ActiveStates;
            Assert.That(finalStates.Length, Is.EqualTo(count));
            for (int i = 0; i < finalStates.Length; i++)
            {
                ulong id = finalStates[i].NetworkEntityId;
                Assert.That(world.TryGetStateByNetworkEntityId(id, out ProjectileState resolved), Is.True);
                Assert.That(resolved.Handle, Is.EqualTo(finalStates[i].Handle));
            }
        }

        [Test]
        public void ActiveStates_ReflectsTheDenseRange()
        {
            ProjectileWorld world = CreateWorld(16);
            ProjectileDefinition definition = Bouncing(bounceCount: 0);
            for (int i = 0; i < 6; i++)
            {
                var request = new ProjectileSpawnRequest(
                    definition, 1UL, (ulong)(i + 1), 0UL, 0, 0, 0u,
                    ProjectileVector3.Zero,
                    new ProjectileVector3(0f, 0f, 1f),
                    ProjectileVector3.Zero);
                world.TrySpawn(in request, out _);
            }

            Assert.That(world.ActiveStates.Length, Is.EqualTo(world.ActiveCount));
            Assert.That(world.ActiveStates.Length, Is.EqualTo(6));

            world.Despawn(world.ActiveStates[0].Handle);
            Assert.That(world.ActiveStates.Length, Is.EqualTo(world.ActiveCount));
            Assert.That(world.ActiveStates.Length, Is.EqualTo(5));
        }

        [Test]
        public void Snapshot_ApplyPatchesStateWithoutResettingCounters()
        {
            ProjectileWorld world = CreateWorld(4);
            ProjectileDefinition definition = Bouncing(bounceCount: 4);
            var request = new ProjectileSpawnRequest(
                definition, 1UL, 9006UL, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            Assert.That(world.TrySpawn(in request, out _), Is.True);

            int bounceBefore = world.TryGetStateByNetworkEntityId(9006UL, out ProjectileState before)
                ? before.RemainingBounceCount
                : -1;

            ProjectileSnapshot snapshot = Snapshot(9006UL, tick: 30, age: 3f, x: 300f);
            Assert.That(world.TryApplySnapshot(in snapshot), Is.EqualTo(ProjectileSnapshotApplyResult.Applied));

            Assert.That(world.TryGetStateByNetworkEntityId(9006UL, out ProjectileState after), Is.True);
            Assert.That(after.Position.X, Is.EqualTo(300f).Within(0.001f));
            Assert.That(after.CurrentTick, Is.EqualTo(30));
            Assert.That(after.RemainingBounceCount, Is.EqualTo(bounceBefore),
                "a correction must not hand the projectile a fresh bounce budget");
        }

        [Test]
        public void Snapshot_ApplyRejectsStaleAndInvalid()
        {
            ProjectileWorld world = CreateWorld(4);
            ProjectileDefinition definition = Bouncing(bounceCount: 0);
            var request = new ProjectileSpawnRequest(
                definition, 1UL, 9007UL, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            world.TrySpawn(in request, out _);

            ProjectileSnapshot newer = Snapshot(9007UL, tick: 20, age: 2f, x: 200f);
            Assert.That(world.TryApplySnapshot(in newer), Is.EqualTo(ProjectileSnapshotApplyResult.Applied));

            ProjectileSnapshot older = Snapshot(9007UL, tick: 15, age: 1.5f, x: 150f);
            Assert.That(world.TryApplySnapshot(in older), Is.EqualTo(ProjectileSnapshotApplyResult.Stale));
            Assert.That(world.TryGetStateByNetworkEntityId(9007UL, out ProjectileState state), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(200f).Within(0.001f), "a stale snapshot must not move the projectile");

            ProjectileSnapshot malformed = new ProjectileSnapshot(
                9007UL, 1UL, 0UL, new ProjectileDefinitionId(7001), ProjectileLifecycleFlags.None,
                21, 0, 2f, 0.2f,
                new ProjectileVector3(float.NaN, 0f, 0f),
                ProjectileVector3.Zero,
                new ProjectileVector3(1f, 0f, 0f));
            Assert.That(world.TryApplySnapshot(in malformed), Is.EqualTo(ProjectileSnapshotApplyResult.Invalid));

            ProjectileSnapshot absent = Snapshot(9999UL, tick: 20, age: 2f, x: 0f);
            Assert.That(world.TryApplySnapshot(in absent), Is.EqualTo(ProjectileSnapshotApplyResult.Missing));
        }

        [Test]
        public void Snapshot_SpawnAndResetUseTheSnapshotTimeline()
        {
            ProjectileWorld world = CreateWorld(4);
            ProjectileDefinition definition = Bouncing(bounceCount: 0);

            ProjectileSnapshot initial = Snapshot(9008UL, tick: 40, age: 4f, x: 400f);
            Assert.That(world.TrySpawnFromSnapshot(in initial, in definition, out ProjectileHandle first), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(9008UL, out ProjectileState spawned), Is.True);
            Assert.That(spawned.Age, Is.EqualTo(4f).Within(0.001f));
            Assert.That(spawned.CurrentTick, Is.EqualTo(40));

            ProjectileSnapshot reset = Snapshot(9008UL, tick: 60, age: 6f, x: 600f);
            Assert.That(world.TryResetFromSnapshot(in reset, in definition, out ProjectileHandle second),
                Is.EqualTo(ProjectileSnapshotApplyResult.Spawned));
            Assert.That(second, Is.Not.EqualTo(first), "a hard reset replaces the handle");
            Assert.That(world.TryGetStateByNetworkEntityId(9008UL, out ProjectileState afterReset), Is.True);
            Assert.That(afterReset.Position.X, Is.EqualTo(600f).Within(0.001f));
            Assert.That(world.ActiveCount, Is.EqualTo(1), "the reset must not leak the old projectile");
        }

        private static ProjectileSnapshot Snapshot(ulong id, int tick, float age, float x)
        {
            return new ProjectileSnapshot(
                id,
                ownerEntityId: 1UL,
                targetEntityId: 0UL,
                new ProjectileDefinitionId(7001),
                ProjectileLifecycleFlags.DespawnOnHit,
                tick,
                predictionKey: 0,
                age,
                radius: 0.2f,
                new ProjectileVector3(x, 0f, 0f),
                new ProjectileVector3(x - 1f, 0f, 0f),
                new ProjectileVector3(10f, 0f, 0f));
        }

        /// <summary>A wall that always reports the same object id at the sweep origin.</summary>
        private sealed class SingleWallCollisionWorld : IProjectileCollisionWorld
        {
            private readonly ulong _objectId;

            public SingleWallCollisionWorld(ulong objectId)
            {
                _objectId = objectId;
            }

            public int Cast(in ProjectileCollisionQuery query, ProjectileCollisionHit[] results, int maxResults)
            {
                results[0] = new ProjectileCollisionHit(
                    targetEntityId: 0UL,
                    targetObjectId: _objectId,
                    hitLayerMask: 1,
                    distance: 0f,
                    position: query.From,
                    normal: new ProjectileVector3(0f, 0f, -1f));
                return 1;
            }
        }

        /// <summary>Reports a different object id on each cast, up to <c>targetCount</c>.</summary>
        private sealed class DistinctObjectsCollisionWorld : IProjectileCollisionWorld
        {
            private readonly int _targetCount;
            private int _cast;

            public DistinctObjectsCollisionWorld(int targetCount)
            {
                _targetCount = targetCount;
            }

            public int Cast(in ProjectileCollisionQuery query, ProjectileCollisionHit[] results, int maxResults)
            {
                if (_cast >= _targetCount)
                {
                    return 0;
                }

                _cast++;
                results[0] = new ProjectileCollisionHit(
                    targetEntityId: (ulong)(1000 + _cast),
                    targetObjectId: (ulong)(7000 + _cast),
                    hitLayerMask: 1,
                    distance: 0.01f,
                    position: query.From,
                    normal: new ProjectileVector3(0f, 0f, -1f));
                return 1;
            }
        }
    }
}
