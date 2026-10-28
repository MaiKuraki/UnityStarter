using System.Collections;
using CycloneGames.Factory.Runtime;
using CycloneGames.Scheduling.Core;
using CycloneGames.Scheduling.Integrations.Factory;
using CycloneGames.Scheduling.Runtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.Scheduling.Tests.PlayMode
{
    public sealed class SchedulingDriverPlayModeTests
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
        public IEnumerator Update_DrivesWorkUntilCompletionWithoutLateSteps()
        {
            CreateDriver();
            var item = new UnityTestWorkItem(2);
            driver.Scheduler.TrySchedule(item, out _);

            yield return null;
            Assert.That(item.StepCount, Is.EqualTo(1));
            yield return null;
            Assert.That(item.StepCount, Is.EqualTo(2));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Completed));

            yield return null;
            Assert.That(item.StepCount, Is.EqualTo(2));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator Disable_PausesWithoutCancellingAndEnableResumes()
        {
            CreateDriver();
            var item = new UnityTestWorkItem(int.MaxValue);
            driver.Scheduler.TrySchedule(item, out _);

            driver.enabled = false;
            yield return null;
            yield return null;
            Assert.That(item.StepCount, Is.Zero);
            Assert.That(item.TerminationCount, Is.Zero);

            driver.enabled = true;
            yield return null;
            Assert.That(item.StepCount, Is.EqualTo(1));
            Assert.That(item.TerminationCount, Is.Zero);
        }

        [UnityTest]
        public IEnumerator Destroy_UsesFakeNullAndPreventsSchedulerRecreationOrLateSteps()
        {
            CreateDriver();
            var item = new UnityTestWorkItem(int.MaxValue);
            driver.Scheduler.TrySchedule(item, out _);

            Object.Destroy(host);
            yield return null;

            Assert.That(driver == null, Is.True, "Unity should expose the destroyed driver as fake-null.");
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            Assert.Throws<System.InvalidOperationException>(() => _ = driver.Scheduler);

            int stepsAfterDestroy = item.StepCount;
            yield return null;
            yield return null;
            Assert.That(item.StepCount, Is.EqualTo(stepsAfterDestroy));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
        }

        [UnityTest]
        public IEnumerator TrimAdapter_ExternallyDestroyedInactive_ReleasesOwnership()
        {
            CreateDriver();
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

        private void CreateDriver()
        {
            host = new GameObject("SchedulingPlayModeHost");
            driver = host.AddComponent<SchedulingDriver>();
            driver.MaxStepsPerFrame = 1;
            driver.SoftBudgetMilliseconds = double.PositiveInfinity;
        }

        private sealed class UnityTestWorkItem : IWorkItem
        {
            private readonly int stepsUntilCompleted;

            public int StepCount { get; private set; }
            public int TerminationCount { get; private set; }
            public WorkTerminationReason? Reason { get; private set; }

            public UnityTestWorkItem(int stepsUntilCompleted)
            {
                this.stepsUntilCompleted = stepsUntilCompleted;
            }

            public WorkStepResult Step()
            {
                StepCount++;
                return StepCount >= stepsUntilCompleted ? WorkStepResult.Completed : WorkStepResult.Running;
            }

            public void OnTerminated(WorkTerminationReason reason)
            {
                TerminationCount++;
                Reason = reason;
            }
        }
    }
}
