using System;
using System.Collections.Generic;
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

        /// <summary>
        /// A resident item in a high lane must not consume steps owed to lower lanes.
        /// </summary>
        [Test]
        public void Tick_ResidentHighLaneItemDoesNotConsumeLowerLaneBudget()
        {
            var scheduler = new WorkScheduler(64);
            var resident = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(resident, WorkLane.Immediate, out _);

            var backlog = new TestWorkItem[63];
            for (int index = 0; index < backlog.Length; index++)
            {
                backlog[index] = new TestWorkItem(int.MaxValue);
                scheduler.TrySchedule(backlog[index], WorkLane.Normal, out _);
            }

            int steps = scheduler.Tick(64, double.PositiveInfinity);

            Assert.That(resident.StepCount, Is.EqualTo(1), "A resident item must run once, not once per step budget");
            Assert.That(steps, Is.EqualTo(64), "The Tick must spend its budget across lanes");
            for (int index = 0; index < backlog.Length; index++)
            {
                Assert.That(backlog[index].StepCount, Is.EqualTo(1), "Backlog item " + index + " was starved");
            }

            AssertInvariant(scheduler);
        }

        /// <summary>
        /// Exhaustive occupancy matrix: every combination of non-empty lanes must serve each
        /// queued item exactly once when the budget covers the whole queue.
        /// </summary>
        [Test]
        public void Tick_ServesEveryLaneExactlyOnceWhenBudgetCoversTheQueue()
        {
            for (int mask = 1; mask < 16; mask++)
            {
                var scheduler = new WorkScheduler(8);
                var items = new List<TestWorkItem>();
                for (int lane = 0; lane < 4; lane++)
                {
                    if ((mask & (1 << lane)) == 0)
                    {
                        continue;
                    }

                    for (int slot = 0; slot < 2; slot++)
                    {
                        var item = new TestWorkItem(int.MaxValue);
                        items.Add(item);
                        scheduler.TrySchedule(item, (WorkLane)lane, out _);
                    }
                }

                int steps = scheduler.Tick(int.MaxValue, double.PositiveInfinity);

                Assert.That(steps, Is.EqualTo(items.Count), "mask " + mask);
                for (int index = 0; index < items.Count; index++)
                {
                    Assert.That(items[index].StepCount, Is.EqualTo(1), "mask " + mask + " item " + index);
                }
            }
        }

        /// <summary>
        /// The starvation boost grants one step ahead of priority order. That step must be credited
        /// against the lane's pass, otherwise the boosted item is served twice in the same Tick.
        /// </summary>
        [Test]
        public void Tick_StarvedLaneBoostDoesNotRepeatTheItemInTheSameTick()
        {
            var scheduler = new WorkScheduler(20);
            for (int index = 0; index < 16; index++)
            {
                scheduler.TrySchedule(new TestWorkItem(int.MaxValue), WorkLane.Immediate, out _);
            }

            var starved = new TestWorkItem[3];
            for (int index = 0; index < starved.Length; index++)
            {
                starved[index] = new TestWorkItem(int.MaxValue);
                scheduler.TrySchedule(starved[index], (WorkLane)(index + 1), out _);
            }

            for (int tick = 0; tick < 16; tick++)
            {
                var before = new int[starved.Length];
                for (int index = 0; index < starved.Length; index++)
                {
                    before[index] = starved[index].StepCount;
                }

                scheduler.Tick(4, double.PositiveInfinity);

                for (int index = 0; index < starved.Length; index++)
                {
                    Assert.That(starved[index].StepCount - before[index], Is.LessThanOrEqualTo(1),
                        "lane " + (index + 1) + " was served twice in tick " + tick);
                }
            }

            for (int index = 0; index < starved.Length; index++)
            {
                Assert.That(starved[index].StepCount, Is.GreaterThan(0), "lane " + (index + 1) + " was never boosted");
            }

            AssertInvariant(scheduler);
        }

        [Test]
        public void RemainingBudgetMilliseconds_ShrinksAcrossStepsInsideOneTick()
        {
            var clock = new ManualSchedulerClock();
            var scheduler = new WorkScheduler(4, clock);
            var probes = new TestWorkItem[3];
            for (int index = 0; index < probes.Length; index++)
            {
                probes[index] = new TestWorkItem(int.MaxValue)
                {
                    Clock = clock,
                    StepCostMilliseconds = 0.5,
                    ProbeRemainingBudget = true,
                    BudgetProbeScheduler = scheduler,
                };
                scheduler.TrySchedule(probes[index], out _);
            }

            scheduler.Tick(3, 2.0);

            Assert.That(probes[0].ObservedRemainingBudget, Is.EqualTo(2.0).Within(0.001));
            Assert.That(probes[1].ObservedRemainingBudget, Is.EqualTo(1.5).Within(0.001));
            Assert.That(probes[2].ObservedRemainingBudget, Is.EqualTo(1.0).Within(0.001));
            Assert.That(scheduler.RemainingBudgetMilliseconds, Is.EqualTo(double.PositiveInfinity),
                "Outside a Tick the budget is unconstrained");
        }

        [Test]
        public void IdleTick_StillAdvancesTickCount()
        {
            var scheduler = new WorkScheduler(2);

            scheduler.Tick(4, double.PositiveInfinity);
            Assert.That(scheduler.GetSnapshot().TickCount, Is.EqualTo(1));
            Assert.That(scheduler.GetSnapshot().LastTickMilliseconds, Is.Zero);

            scheduler.Tick(0, 1.0);
            Assert.That(scheduler.GetSnapshot().TickCount, Is.EqualTo(2));
        }

#if DEBUG
        [Test]
        public void TrySchedule_RejectsStructImplementationInEditor()
        {
            var scheduler = new WorkScheduler(2);
            var value = new StructWorkItem();

            Assert.Throws<ArgumentException>(() => scheduler.TrySchedule(value, out _));
            Assert.That(scheduler.OutstandingCount, Is.Zero);
        }

        private struct StructWorkItem : IWorkItem
        {
            public int Steps;

            public WorkStepResult Step()
            {
                Steps++;
                return WorkStepResult.Running;
            }

            public void OnTerminated(WorkTerminationReason reason)
            {
            }
        }
#endif

        private static void AssertInvariant(WorkScheduler scheduler)
        {
            SchedulerSnapshot snapshot = scheduler.GetSnapshot();
            long terminalAndOutstanding = snapshot.CompletedCount
                + snapshot.CancelledCount
                + snapshot.ShutdownCount
                + snapshot.FaultedCount
                + snapshot.OutstandingCount;
            Assert.That(snapshot.AcceptedCount, Is.EqualTo(terminalAndOutstanding));
            Assert.That(snapshot.OutstandingCount, Is.InRange(0, snapshot.Capacity));
        }
    }
}
