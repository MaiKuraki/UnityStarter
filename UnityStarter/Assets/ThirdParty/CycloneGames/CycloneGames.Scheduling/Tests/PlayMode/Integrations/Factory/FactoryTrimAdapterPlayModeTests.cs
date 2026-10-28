using System.Collections;
using CycloneGames.Factory.Runtime;
using CycloneGames.Scheduling.Core;
using CycloneGames.Scheduling.Integrations.Factory;
using CycloneGames.Scheduling.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Scheduling.Tests.PlayMode.Integrations.Factory
{
    public sealed class FactoryTrimAdapterPlayModeTests
    {
        private GameObject host;
        private SchedulingDriver driver;
        private GameObject prefab;

        [TearDown]
        public void TearDown()
        {
            if (host != null)
            {
                if (driver != null)
                {
                    driver.Shutdown();
                }

                Object.DestroyImmediate(host);
            }

            if (prefab != null)
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [UnityTest]
        public IEnumerator TrimAdapter_ExternallyDestroyedInactive_ReleasesOwnership()
        {
            host = new GameObject("SchedulingFactoryIntegrationHost");
            driver = host.AddComponent<SchedulingDriver>();
            driver.MaxStepsPerFrame = 1;
            driver.SoftBudgetMilliseconds = double.PositiveInfinity;

            prefab = new GameObject("SchedulingInvalidInactivePrefab");
            var pool = new MonoFastPool<Transform>(
                prefab.transform,
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 2),
                autoSetActive: false);
            pool.Prewarm(1);
            Transform abandoned = pool.Spawn();
            pool.Despawn(abandoned);

            Object.Destroy(abandoned.gameObject);
            yield return null;
            Assert.That(abandoned == null, Is.True, "Unity should expose the inactive instance as fake-null.");

            var work = new PoolTrimInactiveWorkItem(pool, 0);
            driver.Scheduler.TrySchedule(work, out _);
            yield return null;

            Assert.That(work.TargetReached, Is.True);
            Assert.That(work.TerminationReason, Is.EqualTo(WorkTerminationReason.Completed));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.TrackedItemCount, Is.Zero);
            Assert.That(pool.Diagnostics.InvalidatedInactiveItems, Is.EqualTo(1));
        }
    }
}
