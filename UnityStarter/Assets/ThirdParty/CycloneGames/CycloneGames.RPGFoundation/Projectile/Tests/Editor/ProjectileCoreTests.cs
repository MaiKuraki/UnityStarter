using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    public sealed class ProjectileCoreTests
    {
        [Test]
        public void LinearProjectile_AdvancesOnConfiguredPlane()
        {
            var config = new ProjectileWorldConfig(
                4, 4, 2,
                ProjectileSpaceProfile.TopDown2D());
            var world = new ProjectileWorld(in config);

            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(
                definitionId: 100,
                speed: 10f,
                radius: 0.2f,
                maxLifetime: 2f,
                collisionLayerMask: 0);

            var request = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 10UL,
                spawnTick: 0,
                position: new ProjectileVector3(0f, 0f, 0f),
                direction: new ProjectileVector3(1f, 1f, 0f));

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            world.Step(0.5f, tick: 1);

            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(5f).Within(0.0001f));
            Assert.That(state.Position.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(state.Position.Z, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void HomingProjectile_RotatesTowardTarget()
        {
            var config = new ProjectileWorldConfig(
                4, 4, 2,
                ProjectileSpaceProfile.TopDown2D());
            var world = new ProjectileWorld(in config);

            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(200),
                ProjectileGuidanceMode.Homing,
                ProjectileLifecycleFlags.DespawnOnHit,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 2f,
                turnRateRadiansPerSecond: (float)(Math.PI * 0.5d),
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: 0,
                effectPayloadId: 0);

            var request = new ProjectileSpawnRequest(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 20UL,
                targetEntityId: 99UL,
                spawnTick: 0,
                predictionKey: 0,
                seed: 0u,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(1f, 0f, 0f),
                initialVelocity: ProjectileVector3.Zero);

            var targets = new StaticTargetProvider(new ProjectileVector3(0f, 0f, 10f));

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);
            world.Step(0.5f, tick: 1, targetProvider: targets);

            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.Velocity.Z, Is.GreaterThan(0f));
            Assert.That(state.Velocity.X, Is.GreaterThan(0f));
        }

        [Test]
        public void InitialVelocity_DoesNotFoldLockedAxisSpeedIntoPlane()
        {
            var config = new ProjectileWorldConfig(
                4, 4, 2,
                ProjectileSpaceProfile.TopDown2D());
            var world = new ProjectileWorld(in config);

            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(
                definitionId: 250,
                speed: 0f,
                radius: 0.2f,
                maxLifetime: 2f,
                collisionLayerMask: 0);

            var request = new ProjectileSpawnRequest(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 25UL,
                targetEntityId: 0UL,
                spawnTick: 0,
                predictionKey: 0,
                seed: 0u,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(1f, 0f, 0f),
                initialVelocity: new ProjectileVector3(3f, 4f, 0f));

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);
            world.Step(1f, tick: 1);

            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.Position.X, Is.EqualTo(3f).Within(0.0001f));
            Assert.That(state.Position.Y, Is.EqualTo(0f).Within(0.0001f));
            Assert.That(state.Position.Z, Is.EqualTo(0f).Within(0.0001f));
        }

        [Test]
        public void Collision_EmitsHitAndInvalidatesHandleOnTerminalHit()
        {
            var config = new ProjectileWorldConfig(
                4, 4, 2,
                ProjectileSpaceProfile.Full3D());
            var world = new ProjectileWorld(in config);

            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(
                definitionId: 300,
                speed: 10f,
                radius: 0.2f,
                maxLifetime: 2f,
                collisionLayerMask: ~0);

            var request = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 30UL,
                spawnTick: 0,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(1f, 0f, 0f));

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);
            world.Step(0.1f, tick: 1, collisionWorld: new SingleHitCollisionWorld());

            Assert.That(world.HitEvents.Count, Is.EqualTo(1));
            Assert.That(world.HitEvents[0].ProjectileEntityId, Is.EqualTo(30UL));
            Assert.That(world.HitEvents[0].TargetEntityId, Is.EqualTo(99UL));
            Assert.That(world.TryGetState(handle, out _), Is.False);
        }

        [Test]
        public void Collision_WithoutDespawnOnHit_EmitsNonTerminalHitAndKeepsProjectile()
        {
            var config = new ProjectileWorldConfig(
                4, 4, 2,
                ProjectileSpaceProfile.Full3D());
            var world = new ProjectileWorld(in config);

            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(350),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 2f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: ~0,
                effectPayloadId: 0);

            var request = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 35UL,
                spawnTick: 0,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(1f, 0f, 0f));

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);
            world.Step(0.1f, tick: 1, collisionWorld: new SingleHitCollisionWorld());

            Assert.That(world.HitEvents.Count, Is.EqualTo(1));
            Assert.That(world.HitEvents[0].IsTerminal, Is.False);
            Assert.That(world.TryGetState(handle, out _), Is.True);
        }

        [Test]
        public void Collision_BounceContinuesAlongRemainingSweepDistance()
        {
            var config = new ProjectileWorldConfig(
                4, 4, 2,
                ProjectileSpaceProfile.Full3D());
            var world = new ProjectileWorld(in config);

            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(400),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 2f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 1,
                collisionLayerMask: ~0,
                effectPayloadId: 0);

            var request = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 40UL,
                spawnTick: 0,
                position: ProjectileVector3.Zero,
                direction: new ProjectileVector3(1f, 0f, 0f));

            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);
            world.Step(1f, tick: 1, collisionWorld: new BounceOnceCollisionWorld());

            Assert.That(world.HitEvents.Count, Is.EqualTo(1));
            Assert.That(world.HitEvents[0].IsTerminal, Is.False);
            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.Velocity.X, Is.LessThan(0f));
            Assert.That(state.Position.X, Is.LessThan(0.1f));
        }

        [Test]
        public void DefinitionValidator_ReportsInvalidAuthoringData()
        {
            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(0),
                ProjectileGuidanceMode.Homing,
                ProjectileLifecycleFlags.Predicted | ProjectileLifecycleFlags.Authoritative,
                initialSpeed: -1f,
                maxSpeed: 0f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: -0.1f,
                maxLifetime: 0f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: -1,
                bounceCount: -1,
                collisionLayerMask: 0,
                effectPayloadId: 0);

            var issues = new ProjectileDefinitionValidationIssue[ProjectileDefinitionValidator.RECOMMENDED_ISSUE_CAPACITY];
            int issueCount = ProjectileDefinitionValidator.Validate(in definition, issues);

            Assert.That(issueCount, Is.GreaterThan(0));
            Assert.That(ProjectileDefinitionValidator.GetWorstSeverity(issues, issueCount), Is.EqualTo(ProjectileValidationSeverity.Error));
        }

        [Test]
        public void WorldStats_RecordSpawnCapacityPressure()
        {
            var config = new ProjectileWorldConfig(
                1, 1, 1,
                ProjectileSpaceProfile.Full3D());
            var world = new ProjectileWorld(in config);

            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(
                definitionId: 500,
                speed: 1f,
                radius: 0.1f,
                maxLifetime: 10f,
                collisionLayerMask: 0);

            ProjectileSpawnRequest first = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 500UL,
                spawnTick: 0,
                ProjectileVector3.Zero,
                ProjectileVector3.Forward);

            ProjectileSpawnRequest second = ProjectileSpawnRequest.Create(
                definition,
                ownerEntityId: 1UL,
                networkEntityId: 501UL,
                spawnTick: 0,
                ProjectileVector3.Zero,
                ProjectileVector3.Forward);

            Assert.That(world.TrySpawn(in first, out _), Is.True);
            Assert.That(world.TrySpawn(in second, out _), Is.False);

            ProjectileWorldStats stats = world.Stats;
            Assert.That(stats.TotalSpawnAcceptedCount, Is.EqualTo(1));
            Assert.That(stats.TotalSpawnRejectedCapacityCount, Is.EqualTo(1));
            Assert.That(stats.PeakActiveCount, Is.EqualTo(1));
        }

        private sealed class StaticTargetProvider : IProjectileTargetProvider
        {
            private readonly ProjectileVector3 _position;

            public StaticTargetProvider(ProjectileVector3 position)
            {
                _position = position;
            }

            public bool TryGetTargetPosition(ulong targetEntityId, out ProjectileVector3 position)
            {
                position = _position;
                return true;
            }

            public bool TryGetTargetVelocity(ulong targetEntityId, out ProjectileVector3 velocity)
            {
                velocity = ProjectileVector3.Zero;
                return true;
            }
        }

        private sealed class SingleHitCollisionWorld : IProjectileCollisionWorld
        {
            public int Cast(
                in ProjectileCollisionQuery query,
                ProjectileCollisionHit[] results,
                int maxResults)
            {
                results[0] = new ProjectileCollisionHit(
                    targetEntityId: 99UL,
                    targetObjectId: 0,
                    hitLayerMask: 1,
                    distance: 0.5f,
                    position: new ProjectileVector3(0.5f, 0f, 0f),
                    normal: new ProjectileVector3(-1f, 0f, 0f));
                return 1;
            }
        }

        private sealed class BounceOnceCollisionWorld : IProjectileCollisionWorld
        {
            private int _castCount;

            public int Cast(
                in ProjectileCollisionQuery query,
                ProjectileCollisionHit[] results,
                int maxResults)
            {
                if (_castCount > 0)
                {
                    return 0;
                }

                _castCount++;
                results[0] = new ProjectileCollisionHit(
                    targetEntityId: 99UL,
                    targetObjectId: 0,
                    hitLayerMask: 1,
                    distance: 5f,
                    position: new ProjectileVector3(5f, 0f, 0f),
                    normal: new ProjectileVector3(-1f, 0f, 0f));
                return 1;
            }
        }

        [Test]
        public void Clear_InvalidatesOutstandingHandles()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var config = new ProjectileWorldConfig(8, 16, 4, in space);
            var world = new ProjectileWorld(in config);
            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(1, 10f, 0.2f, 10f, 0);
            ProjectileSpawnRequest first = ProjectileSpawnRequest.Create(
                definition, 1UL, 1UL, 0, ProjectileVector3.Zero, ProjectileVector3.Forward);
            Assert.That(world.TrySpawn(in first, out ProjectileHandle retained), Is.True);

            world.Clear();

            ProjectileSpawnRequest second = ProjectileSpawnRequest.Create(
                definition, 1UL, 2UL, 0, ProjectileVector3.Zero, ProjectileVector3.Forward);
            Assert.That(world.TrySpawn(in second, out ProjectileHandle fresh), Is.True);
            Assert.That(fresh.Equals(retained), Is.False);
            Assert.That(world.TryGetState(retained, out _), Is.False);
            Assert.That(world.TryGetState(fresh, out ProjectileState state), Is.True);
            Assert.That(state.NetworkEntityId, Is.EqualTo(2UL));
        }

        [Test]
        public void Config_ExposesExplicitBounds()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var zeroCapacity = new ProjectileWorldConfig(0, 16, 4, in space);
            Assert.That(zeroCapacity.IsValid, Is.False);

            var zeroEvents = new ProjectileWorldConfig(8, 0, 4, in space);
            Assert.That(zeroEvents.IsValid, Is.False);

            var negativeStep = new ProjectileWorldConfig(8, 16, 4, in space, 4, -1f, 4);
            Assert.That(negativeStep.IsValid, Is.False);

            var valid = new ProjectileWorldConfig(8, 16, 4, in space, 4, 1f / 60f, 4);
            Assert.That(valid.IsValid, Is.True);
        }

        [Test]
        public void Config_RejectsUnboundedBudgets()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var hugeIterations = new ProjectileWorldConfig(8, 16, 4, in space, int.MaxValue, 1f / 60f, 4);
            Assert.That(hugeIterations.IsValid, Is.False);

            var hugeSubSteps = new ProjectileWorldConfig(8, 16, 4, in space, 4, 1e-30f, int.MaxValue);
            Assert.That(hugeSubSteps.IsValid, Is.False);

            var hugeDelta = new ProjectileWorldConfig(8, 16, 4, in space, 4, float.MaxValue, 4);
            Assert.That(hugeDelta.IsValid, Is.False);

            var nanDelta = new ProjectileWorldConfig(8, 16, 4, in space, 4, float.NaN, 4);
            Assert.That(nanDelta.IsValid, Is.False);

            var sane = new ProjectileWorldConfig(8, 16, 4, in space, 16, 1f / 60f, 8);
            Assert.That(sane.IsValid, Is.True);
        }

        [Test]
        public void SubSteps_BoundEachIntegrationStep()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var config = new ProjectileWorldConfig(4, 16, 4, in space, 4, 0.02f, 4);
            var world = new ProjectileWorld(in config);
            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(2, 10f, 0.2f, 100f, 0);
            ProjectileSpawnRequest request = ProjectileSpawnRequest.Create(
                definition, 1UL, 1UL, 0, ProjectileVector3.Zero, ProjectileVector3.Forward);
            Assert.That(world.TrySpawn(in request, out ProjectileHandle handle), Is.True);

            world.Step(0.5f, 1);

            Assert.That(world.TryGetState(handle, out ProjectileState state), Is.True);
            Assert.That(state.Position.Z, Is.EqualTo(0.8f).Within(0.0001f));
            Assert.That(world.Stats.TotalSubStepCount, Is.EqualTo(4L));
            Assert.That(world.Stats.TotalClampedFrameCount, Is.EqualTo(1L));
        }

        [Test]
        public void BatchSnapshots_AreDenseAndTruncate()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var config = new ProjectileWorldConfig(8, 16, 4, in space);
            var world = new ProjectileWorld(in config);
            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(3, 10f, 0.2f, 10f, 0);
            for (int i = 1; i <= 3; i++)
            {
                ProjectileSpawnRequest request = ProjectileSpawnRequest.Create(
                    definition, 1UL, (ulong)i, 0, ProjectileVector3.Zero, ProjectileVector3.Forward);
                Assert.That(world.TrySpawn(in request, out _), Is.True);
            }

            var truncated = new ProjectileSnapshot[2];
            Assert.That(world.CaptureSnapshots(truncated), Is.EqualTo(2));
            Assert.That(truncated[0].NetworkEntityId, Is.EqualTo(1UL));
            Assert.That(truncated[1].NetworkEntityId, Is.EqualTo(2UL));

            var full = new ProjectileSnapshot[3];
            Assert.That(world.CaptureSnapshots(full), Is.EqualTo(3));
            Assert.That(full[2].NetworkEntityId, Is.EqualTo(3UL));
        }

        [Test]
        public void RepeatedHit_IsSuppressedWithinOneSweep()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var config = new ProjectileWorldConfig(4, 64, 4, in space, 8);
            var world = new ProjectileWorld(in config);
            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(4),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                10f, 20f, 0f, 0f, 0.2f, 10f, 0f, 0f,
                0, 6, ~0, 0);
            ProjectileSpawnRequest request = ProjectileSpawnRequest.Create(
                definition, 1UL, 1UL, 0, ProjectileVector3.Zero, ProjectileVector3.Forward);
            Assert.That(world.TrySpawn(in request, out _), Is.True);

            world.Step(1f / 60f, 1, new RepeatingTargetCollisionWorld(), null);

            Assert.That(world.HitEvents.Count, Is.EqualTo(1));
        }

        [Test]
        public void IgnoreOwner_FiltersOwnerEntityHits()
        {
            ProjectileSpaceProfile space = ProjectileSpaceProfile.Full3D();
            var config = new ProjectileWorldConfig(4, 16, 4, in space);
            var world = new ProjectileWorld(in config);
            ProjectileDefinition definition = ProjectileDefinition.CreateKinematic(5, 10f, 0.2f, 10f, ~0);
            ProjectileSpawnRequest request = ProjectileSpawnRequest.Create(
                definition, 7UL, 1UL, 0, ProjectileVector3.Zero, ProjectileVector3.Forward);
            Assert.That(world.TrySpawn(in request, out _), Is.True);

            world.Step(1f / 60f, 1, new OwnerHitCollisionWorld(), null);

            Assert.That(world.HitEvents.Count, Is.EqualTo(0));
            Assert.That(world.ActiveCount, Is.EqualTo(1));
        }

        private sealed class RepeatingTargetCollisionWorld : IProjectileCollisionWorld
        {
            public int Cast(
                in ProjectileCollisionQuery query,
                ProjectileCollisionHit[] results,
                int maxResults)
            {
                results[0] = new ProjectileCollisionHit(
                    99UL, 99UL, 1, 0.01f,
                    query.From, new ProjectileVector3(0f, 1f, 0f));
                return 1;
            }
        }

        private sealed class OwnerHitCollisionWorld : IProjectileCollisionWorld
        {
            public int Cast(
                in ProjectileCollisionQuery query,
                ProjectileCollisionHit[] results,
                int maxResults)
            {
                results[0] = new ProjectileCollisionHit(
                    7UL, 7UL, 1, 0.01f,
                    query.From, new ProjectileVector3(0f, 1f, 0f));
                return 1;
            }
        }
    }
}
