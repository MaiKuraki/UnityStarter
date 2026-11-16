using CycloneGames.RPGFoundation.Projectile.Core;
using CycloneGames.RPGFoundation.Projectile.Runtime;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    /// <summary>
    /// The component carries two sub-step budgets that used to be one field: how many fixed steps
    /// may run per frame to catch up, and how finely a single step is split. These tests pin the
    /// separation so raising one cannot silently change the other.
    /// </summary>
    public sealed class ProjectileSystemBehaviourTests
    {
        [Test]
        public void IntegrationSubSteps_ZeroMirrorsCatchUpBudget()
        {
            Assert.That(ProjectileSystemBehaviour.ResolveIntegrationSubSteps(0, 4), Is.EqualTo(4));
            Assert.That(ProjectileSystemBehaviour.ResolveIntegrationSubSteps(0, 8), Is.EqualTo(8));
        }

        [Test]
        public void IntegrationSubSteps_AreClampedIndependently()
        {
            Assert.That(ProjectileSystemBehaviour.ResolveIntegrationSubSteps(16, 4), Is.EqualTo(16));
            Assert.That(ProjectileSystemBehaviour.ResolveIntegrationSubSteps(99, 4), Is.EqualTo(16));
            Assert.That(ProjectileSystemBehaviour.ResolveIntegrationSubSteps(0, 99), Is.EqualTo(8));
        }

        [Test]
        public void IntegrationSubSteps_BoundIsIndependentOfCatchUpBudget()
        {
            // Same catch-up budget, different integration budget: the world must split a single
            // step differently. Before the split these two were the same number.
            long withFour = SubStepsForOneStep(catchUpSubSteps: 4, integrationSubSteps: 4, travel: 0.01f);
            long withSixteen = SubStepsForOneStep(catchUpSubSteps: 4, integrationSubSteps: 16, travel: 0.01f);

            Assert.That(withFour, Is.EqualTo(4L), "catch-up budget of 4 must cap integration at 4 when unspecified");
            Assert.That(withSixteen, Is.EqualTo(16L), "an explicit integration budget must be honoured");
        }

        [Test]
        public void DisplacementBound_IsReachableThroughTheComponent()
        {
            long unbound = SubStepsForOneStep(catchUpSubSteps: 4, integrationSubSteps: 4, travel: 0f);
            long bound = SubStepsForOneStep(catchUpSubSteps: 4, integrationSubSteps: 16, travel: 0.01f);

            Assert.That(unbound, Is.EqualTo(1L), "with the bound disabled a single step must not be split");
            Assert.That(bound, Is.GreaterThan(1L), "the bound must reach the world through the component");
        }

        /// <summary>Spawns one fast projectile, steps once, and returns the sub-step count.</summary>
        private static long SubStepsForOneStep(int catchUpSubSteps, int integrationSubSteps, float travel)
        {
            var go = new GameObject("__step_bound_probe");
            try
            {
                var behaviour = go.AddComponent<ProjectileSystemBehaviour>();
                var serialized = new SerializedObject(behaviour);
                serialized.FindProperty("FixedDeltaTime").floatValue = 1f / 60f;
                serialized.FindProperty("MaxSubSteps").intValue = catchUpSubSteps;
                serialized.FindProperty("MaxIntegrationSubSteps").intValue = integrationSubSteps;
                serialized.FindProperty("MaxTravelPerStep").floatValue = travel;
                serialized.FindProperty("AutoTick").boolValue = false;
                serialized.ApplyModifiedPropertiesWithoutUndo();

                ProjectileWorld world = behaviour.World;

                var definition = new ProjectileDefinition(
                    new ProjectileDefinitionId(500),
                    ProjectileGuidanceMode.Direction,
                    ProjectileLifecycleFlags.None,
                    initialSpeed: 300f,
                    maxSpeed: 300f,
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
                    definition, 1UL, 1UL, 0UL, 0, 0, 0u,
                    ProjectileVector3.Zero,
                    new ProjectileVector3(0f, 0f, 1f),
                    ProjectileVector3.Zero);
                world.TrySpawn(in request, out _);

                behaviour.Step(1f / 60f, 1, null);
                return world.Stats.TotalSubStepCount;
            }
            finally
            {
                Object.DestroyImmediate(go);
            }
        }
    }
}
