using System;
using CycloneGames.Scheduling.Core;
using NUnit.Framework;

namespace CycloneGames.Scheduling.Tests.Editor
{
    /// <summary>
    /// Locks the lane, starvation, isolation and handle-identity contracts. Each test fails
    /// without the corresponding scheduler mechanism, so it doubles as a regression gate.
    /// </summary>
    public sealed class WorkSchedulerLaneTests
    {
        [Test]
        public void Tick_DrainsImmediateLaneBeforeSaturatedNormalLane()
        {
            var scheduler = new WorkScheduler(64);
            for (int index = 0; index < 63; index++)
            {
                scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out _);
            }

            var urgent = new TestWorkItem(1);
            scheduler.TrySchedule(urgent, WorkLane.Immediate, out _);

            scheduler.Tick(32, double.PositiveInfinity);

            Assert.That(urgent.StepCount, Is.EqualTo(1), "Immediate must run in the first Tick regardless of Normal depth");
            Assert.That(scheduler.GetLaneOutstanding(WorkLane.Normal), Is.EqualTo(63));
        }

        [Test]
        public void Tick_SaturatedNormalLaneStillServesBackgroundWithinStarvationWindow()
        {
            var scheduler = new WorkScheduler(64);
            for (int index = 0; index < 63; index++)
            {
                scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out _);
            }

            var background = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(background, WorkLane.Background, out _);

            int ticks = 0;
            while (background.StepCount == 0 && ticks < 32)
            {
                scheduler.Tick(32, double.PositiveInfinity);
                ticks++;
            }

            Assert.That(background.StepCount, Is.GreaterThan(0), "Background must not be locked out");
            Assert.That(ticks, Is.LessThanOrEqualTo(scheduler.StarvationLimitTicks + 1),
                "Starvation window must bound the wait");
        }

        [Test]
        public void Tick_FaultIsolatesFailingItemAndKeepsServingOthers()
        {
            var scheduler = new WorkScheduler(8);
            scheduler.TrySchedule(new TestWorkItem { StepFailure = new InvalidOperationException("boom") }, out _);
            var others = new TestWorkItem[4];
            for (int index = 0; index < others.Length; index++)
            {
                others[index] = new TestWorkItem(int.MaxValue);
                scheduler.TrySchedule(others[index], out _);
            }

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick(8, double.PositiveInfinity));

            for (int index = 0; index < others.Length; index++)
            {
                Assert.That(others[index].StepCount, Is.EqualTo(1),
                    "Item " + index + " must still receive its step in the same Tick");
            }

            Assert.That(scheduler.GetSnapshot().FaultedCount, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_RejectsHandleIssuedByAnotherScheduler()
        {
            var first = new WorkScheduler(2);
            var second = new WorkScheduler(2);
            var firstItem = new TestWorkItem(int.MaxValue);
            var secondItem = new TestWorkItem(int.MaxValue);

            first.TrySchedule(firstItem, out WorkItemHandle firstHandle);
            second.TrySchedule(secondItem, out _);

            Assert.That(second.Cancel(firstHandle), Is.False, "A foreign handle must never resolve");
            Assert.That(secondItem.TerminationCount, Is.Zero);
            Assert.That(firstItem.TerminationCount, Is.Zero);
            Assert.That(second.OutstandingCount, Is.EqualTo(1));
        }

        [Test]
        public void Shutdown_IsCountedSeparatelyFromCancel()
        {
            var scheduler = new WorkScheduler(4);
            var cancelled = new TestWorkItem(int.MaxValue);
            var drained = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(cancelled, out WorkItemHandle handle);
            scheduler.TrySchedule(drained, out _);

            Assert.That(scheduler.Cancel(handle), Is.True);
            scheduler.Shutdown();

            SchedulerSnapshot snapshot = scheduler.GetSnapshot();
            Assert.That(snapshot.CancelledCount, Is.EqualTo(1));
            Assert.That(snapshot.ShutdownCount, Is.EqualTo(1));
            Assert.That(cancelled.Reason, Is.EqualTo(WorkTerminationReason.Cancelled));
            Assert.That(drained.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
        }

        [Test]
        public void TrySchedule_DuplicateInstanceIsRejectedInEditor()
        {
            var scheduler = new WorkScheduler(4);
            var shared = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(shared, out _);

            Assert.Throws<InvalidOperationException>(() => scheduler.TrySchedule(shared, out _));
            Assert.That(scheduler.OutstandingCount, Is.EqualTo(1));
        }

        [Test]
        public void GetSnapshot_IsReadableFromInsideStep()
        {
            var scheduler = new WorkScheduler(2);
            var item = new TestWorkItem(1)
            {
                ReentrantScheduler = scheduler,
                ReentrantOperation = TestReentrantOperation.Snapshot,
            };

            scheduler.TrySchedule(item, out _);
            Assert.DoesNotThrow(() => scheduler.Tick(1, double.PositiveInfinity));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Completed));
        }

        [Test]
        public void GetSnapshot_AttributesSlowestStepToTheCorrectRegistration()
        {
            var clock = new ManualSchedulerClock();
            var scheduler = new WorkScheduler(8, clock);
            scheduler.TrackStepTiming = true;
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Clock = clock, StepCostMilliseconds = 0.01 }, out _);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Clock = clock, StepCostMilliseconds = 0.5 }, out _);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Clock = clock, StepCostMilliseconds = 0.02 }, out _);

            scheduler.Tick(8, 10.0);

            SchedulerSnapshot snapshot = scheduler.GetSnapshot();
            Assert.That(snapshot.SlowestStepMilliseconds, Is.EqualTo(0.5).Within(0.001));
            Assert.That(snapshot.SlowestStep.Slot, Is.EqualTo(1));
        }
    }
}
