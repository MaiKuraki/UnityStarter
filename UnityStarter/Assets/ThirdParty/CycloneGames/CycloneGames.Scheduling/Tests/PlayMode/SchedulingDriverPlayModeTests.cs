using System.Collections;
using CycloneGames.Scheduling.Core;
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
        public IEnumerator BeginShutdown_DrainsInBatchesWithoutExecutingSteps()
        {
            CreateDriver();
            var items = new[]
            {
                new UnityTestWorkItem(int.MaxValue),
                new UnityTestWorkItem(int.MaxValue),
                new UnityTestWorkItem(int.MaxValue),
            };
            for (int index = 0; index < items.Length; index++)
            {
                driver.Scheduler.TrySchedule(items[index], out _);
            }

            driver.BeginShutdown();
            Assert.That(driver.IsShuttingDown, Is.True);
            yield return null;
            Assert.That(items[0].Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            Assert.That(items[1].TerminationCount, Is.Zero);
            Assert.That(driver.Scheduler.OutstandingCount, Is.EqualTo(2));

            yield return null;
            Assert.That(driver.Scheduler.OutstandingCount, Is.EqualTo(1));
            yield return null;

            for (int index = 0; index < items.Length; index++)
            {
                Assert.That(items[index].StepCount, Is.Zero);
                Assert.That(items[index].TerminationCount, Is.EqualTo(1));
                Assert.That(items[index].Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            }

            Assert.That(driver.IsShuttingDown, Is.False);
            Assert.Throws<System.InvalidOperationException>(() => _ = driver.Scheduler);
        }

        [UnityTest]
        public IEnumerator DestroyDuringShutdownDrain_CompletesCleanupImmediately()
        {
            CreateDriver();
            var items = new[]
            {
                new UnityTestWorkItem(int.MaxValue),
                new UnityTestWorkItem(int.MaxValue),
                new UnityTestWorkItem(int.MaxValue),
            };
            for (int index = 0; index < items.Length; index++)
            {
                driver.Scheduler.TrySchedule(items[index], out _);
            }

            driver.BeginShutdown();
            yield return null;
            Assert.That(items[0].TerminationCount, Is.EqualTo(1));
            Assert.That(items[1].TerminationCount, Is.Zero);

            Object.Destroy(host);
            yield return null;

            for (int index = 0; index < items.Length; index++)
            {
                Assert.That(items[index].StepCount, Is.Zero);
                Assert.That(items[index].TerminationCount, Is.EqualTo(1));
                Assert.That(items[index].Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            }
        }

        [UnityTest]
        public IEnumerator BoundScheduler_RemainsOwnedByCompositionRootAfterHostDestroy()
        {
            CreateDriver();
            var external = new WorkScheduler(2);
            try
            {
                driver.Bind(external);
                Assert.That(driver.OwnsScheduler, Is.False);
                var item = new UnityTestWorkItem(int.MaxValue);
                external.TrySchedule(item, out _);
                Assert.Throws<System.InvalidOperationException>(() => driver.BeginShutdown());

                Object.Destroy(host);
                yield return null;

                Assert.That(driver == null, Is.True);
                Assert.That(external.IsShutdown, Is.False);
                Assert.That(item.TerminationCount, Is.Zero);

                external.Shutdown();
                Assert.That(item.TerminationCount, Is.EqualTo(1));
                Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            }
            finally
            {
                external.Shutdown();
            }
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
