using System;
using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    /// <summary>
    /// Covers the displacement bound: the frame is split until no projectile travels further than
    /// the configured distance, the budget cap is reported when it cannot be honoured, and a
    /// rollback does not change the sub-step count of the replay.
    /// </summary>
    public sealed class ProjectileStepBoundTests
    {
        private static readonly ProjectileSpaceProfile Space = ProjectileSpaceProfile.Full3D();

        [Test]
        public void Bound_SplitsTheFrameUntilDisplacementFits()
        {
            ProjectileWorld world = CreateWorld(maxTravelPerStep: 0.1f, maxSubSteps: 16);
            Spawn(world, speed: 30f);

            world.Step(1f / 60f, 1, null, null);

            // 30 m/s at 1/60 s is 0.5 m, so five sub-steps of 0.1 m are required. The bound applies
            // one frame late because max observed speed is sampled during the step, and the spawn
            // raises it immediately, so the first frame is already bounded.
            long subSteps = world.Stats.TotalSubStepCount;
            Assert.That(subSteps, Is.GreaterThanOrEqualTo(5L), "expected the frame to be split, got " + subSteps);
            Assert.That(world.SubStepSaturationCount, Is.EqualTo(0L));
        }

        [Test]
        public void Bound_ZeroKeepsTheTimeOnlyBehaviour()
        {
            ProjectileWorld world = CreateWorld(maxTravelPerStep: 0f, maxSubSteps: 16);
            Spawn(world, speed: 30f);

            world.Step(1f / 60f, 1, null, null);

            Assert.That(world.Stats.TotalSubStepCount, Is.EqualTo(1L));
            Assert.That(world.SubStepSaturationCount, Is.EqualTo(0L));
        }

        [Test]
        public void Bound_ReportsSaturationWhenTheBudgetIsTooSmall()
        {
            ProjectileWorld world = CreateWorld(maxTravelPerStep: 0.01f, maxSubSteps: 4);
            Spawn(world, speed: 300f);

            world.Step(1f / 60f, 1, null, null);

            // 300 m/s needs 500 sub-steps for a 0.01 m bound; the budget is 4.
            Assert.That(world.SubStepSaturationCount, Is.EqualTo(1L));
            Assert.That(world.Stats.TotalSubStepCount, Is.EqualTo(4L));
        }

        [Test]
        public void Bound_SaturationIsClearedByResetStats()
        {
            ProjectileWorld world = CreateWorld(maxTravelPerStep: 0.01f, maxSubSteps: 4);
            Spawn(world, speed: 300f);
            world.Step(1f / 60f, 1, null, null);

            Assert.That(world.SubStepSaturationCount, Is.EqualTo(1L));
            world.ResetStats();
            Assert.That(world.SubStepSaturationCount, Is.EqualTo(0L));
        }

        [Test]
        public void Bound_IsAnchoredByAFastSpawn()
        {
            ProjectileWorld world = CreateWorld(maxTravelPerStep: 0.5f, maxSubSteps: 16);
            Spawn(world, speed: 400f);

            // The spawn raises the observed speed immediately, so the very first frame is already
            // split. Without that, a fast projectile would run one under-sub-stepped frame.
            world.Step(1f / 60f, 1, null, null);

            Assert.That(world.Stats.TotalSubStepCount, Is.GreaterThan(1L));
        }

        [Test]
        public void Bound_RollbackReplayKeepsTheSubStepCount()
        {
            ProjectileWorld world = CreateWorld(maxTravelPerStep: 0.2f, maxSubSteps: 8);
            for (int i = 1; i <= 16; i++)
            {
                Spawn(world, speed: 20f + i, id: (ulong)i);
            }

            for (int step = 0; step < 40; step++)
            {
                world.Step(1f / 60f, step + 1, null, null);
            }

            var captured = new ProjectileSnapshot[world.ActiveCount];
            world.CaptureSnapshots(captured);
            ulong origin = Hash(world);

            for (int step = 0; step < 30; step++)
            {
                world.Step(1f / 60f, 41 + step, null, null);
            }

            ulong forward = Hash(world);

            world.TryRestoreStates(captured, ProjectileRestoreMode.Replace, null);
            Assert.That(Hash(world), Is.EqualTo(origin), "restore must reproduce the captured tick");

            for (int step = 0; step < 30; step++)
            {
                world.Step(1f / 60f, 41 + step, null, null);
            }

            // Equal state after replay is the proof: a different sub-step split integrates a
            // different number of times and lands somewhere else.
            Assert.That(Hash(world), Is.EqualTo(forward),
                "the replay must reproduce the state; a stale observed speed would change the sub-step count");
        }

        [Test]
        public void Conformance_RejectsAnEndpointOnlyAdapter()
        {
            var swept = new SweptWallWorld();
            var endpoint = new EndpointOnlyWallWorld();

            Assert.That(
                ProjectileCollisionWorldConformance.VerifySweep(swept, swept.Query(), out string sweptFailure),
                Is.True, sweptFailure);

            Assert.That(
                ProjectileCollisionWorldConformance.VerifySweep(endpoint, endpoint.Query(), out string endpointFailure),
                Is.False, "an endpoint test must fail the sweep conformance check");
            Assert.That(endpointFailure != null && endpointFailure.Contains("not"), Is.True,
                "failure message should explain the adapter is not sweeping: " + endpointFailure);
        }

        [Test]
        public void Conformance_RejectsHitsWithoutIdentifiers()
        {
            var anonymous = new AnonymousHitWorld();
            Assert.That(
                ProjectileCollisionWorldConformance.VerifySweep(anonymous, anonymous.Query(), out string failure),
                Is.False);
            Assert.That(failure != null && failure.Contains("neither"), Is.True,
                "failure message should mention the missing identifiers: " + failure);
        }

        private static ProjectileWorld CreateWorld(float maxTravelPerStep, int maxSubSteps)
        {
            var config = new ProjectileWorldConfig(
                capacity: 64,
                eventCapacity: 32,
                collisionHitCapacity: 4,
                space: in Space,
                maxCollisionIterations: 1,
                maxDeltaTime: 0f,
                maxSubSteps: maxSubSteps,
                enableNetworkEntityIdIndex: true,
                maxTravelPerStep: maxTravelPerStep);
            return new ProjectileWorld(in config);
        }

        private static void Spawn(ProjectileWorld world, float speed, ulong id = 1UL)
        {
            var definition = new ProjectileDefinition(
                new ProjectileDefinitionId(400),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                initialSpeed: speed,
                maxSpeed: speed,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.05f,
                maxLifetime: 100f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: 0,
                effectPayloadId: 0);

            var request = new ProjectileSpawnRequest(
                definition, 1UL, id, 0UL, 0, 0, 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
            world.TrySpawn(in request, out _);
        }

        private static ulong Hash(ProjectileWorld world)
        {
            ReadOnlySpan<ProjectileState> states = world.ActiveStates;
            ulong sum = 0;
            for (int i = 0; i < states.Length; i++)
            {
                ulong h = 1469598103934665603UL;
                h = Mix(h, states[i].NetworkEntityId);
                h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(states[i].Position.Z));
                h = Mix(h, (ulong)BitConverter.SingleToInt32Bits(states[i].Velocity.Z));
                sum += h;
            }

            return sum;
        }

        private static ulong Mix(ulong hash, ulong value)
        {
            hash ^= value;
            hash *= 1099511628211UL;
            return hash;
        }

        /// <summary>Reports the near face of a wall for any segment that reaches it.</summary>
        private sealed class SweptWallWorld : IProjectileCollisionWorld
        {
            public ProjectileCollisionQuery Query()
            {
                return new ProjectileCollisionQuery(
                    default(ProjectileHandle), 1UL, 1UL, ~0, 0.05f,
                    new ProjectileVector3(0f, 0f, 9.9f),
                    new ProjectileVector3(0f, 0f, 10.1f));
            }

            public int Cast(in ProjectileCollisionQuery query, ProjectileCollisionHit[] results, int maxResults)
            {
                if (maxResults < 1 || query.To.Z < 10f || query.From.Z > 10f)
                {
                    return 0;
                }

                results[0] = new ProjectileCollisionHit(
                    0UL, 77UL, 1, Math.Max(0f, 10f - query.From.Z),
                    new ProjectileVector3(0f, 0f, 10f), new ProjectileVector3(0f, 0f, -1f));
                return 1;
            }
        }

        /// <summary>Only tests the segment's end point. This is the adapter the check must fail.</summary>
        private sealed class EndpointOnlyWallWorld : IProjectileCollisionWorld
        {
            public ProjectileCollisionQuery Query()
            {
                return new ProjectileCollisionQuery(
                    default(ProjectileHandle), 1UL, 1UL, ~0, 0.05f,
                    new ProjectileVector3(0f, 0f, 9.9f),
                    new ProjectileVector3(0f, 0f, 10.1f));
            }

            public int Cast(in ProjectileCollisionQuery query, ProjectileCollisionHit[] results, int maxResults)
            {
                if (maxResults < 1 || Math.Abs(query.To.Z - 10f) > 0.01f)
                {
                    return 0;
                }

                results[0] = new ProjectileCollisionHit(
                    0UL, 78UL, 1, 0f,
                    new ProjectileVector3(0f, 0f, 10f), new ProjectileVector3(0f, 0f, -1f));
                return 1;
            }
        }

        /// <summary>Reports a hit with no identifiers, which defeats repeated-contact suppression.</summary>
        private sealed class AnonymousHitWorld : IProjectileCollisionWorld
        {
            public ProjectileCollisionQuery Query()
            {
                return new ProjectileCollisionQuery(
                    default(ProjectileHandle), 1UL, 1UL, ~0, 0.05f,
                    new ProjectileVector3(0f, 0f, 9.9f),
                    new ProjectileVector3(0f, 0f, 10.1f));
            }

            public int Cast(in ProjectileCollisionQuery query, ProjectileCollisionHit[] results, int maxResults)
            {
                if (maxResults < 1)
                {
                    return 0;
                }

                results[0] = new ProjectileCollisionHit(
                    0UL, 0UL, 1, 0f,
                    new ProjectileVector3(0f, 0f, 10f), new ProjectileVector3(0f, 0f, -1f));
                return 1;
            }
        }
    }
}
