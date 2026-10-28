using System;
using System.Collections.Generic;
using System.Threading;
using CycloneGames.Scheduling.Core;
using NUnit.Framework;

namespace CycloneGames.Scheduling.Tests.Editor
{
    public sealed class WorkSchedulerTests
    {
        [Test]
        public void Tick_RoundRobinRunsEachEligibleItemOnce()
        {
            var scheduler = new WorkScheduler(3);
            var order = new List<int>();
            var first = new TestWorkItem(2) { Id = 1, ExecutionOrder = order };
            var second = new TestWorkItem(2) { Id = 2, ExecutionOrder = order };
            var third = new TestWorkItem(2) { Id = 3, ExecutionOrder = order };
            scheduler.TrySchedule(first, out _);
            scheduler.TrySchedule(second, out _);
            scheduler.TrySchedule(third, out _);

            Assert.That(scheduler.Tick(10, double.PositiveInfinity), Is.EqualTo(3));
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3 }));
            Assert.That(scheduler.OutstandingCount, Is.EqualTo(3));

            Assert.That(scheduler.Tick(10, double.PositiveInfinity), Is.EqualTo(3));
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3, 1, 2, 3 }));
            Assert.That(scheduler.OutstandingCount, Is.Zero);
            Assert.That(first.TerminationCount, Is.EqualTo(1));
            Assert.That(second.TerminationCount, Is.EqualTo(1));
            Assert.That(third.TerminationCount, Is.EqualTo(1));
            AssertInvariant(scheduler);
        }

        [Test]
        public void Tick_StepBudgetPreservesFairnessAcrossTicks()
        {
            var scheduler = new WorkScheduler(3);
            var order = new List<int>();
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Id = 1, ExecutionOrder = order }, out _);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Id = 2, ExecutionOrder = order }, out _);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Id = 3, ExecutionOrder = order }, out _);

            Assert.That(scheduler.Tick(2, double.PositiveInfinity), Is.EqualTo(2));
            Assert.That(scheduler.Tick(2, double.PositiveInfinity), Is.EqualTo(2));
            Assert.That(order, Is.EqualTo(new[] { 1, 2, 3, 1 }));
        }

        [Test]
        public void Tick_SoftBudgetStopsAdmissionAndRecordsOverspend()
        {
            var clock = new ManualSchedulerClock();
            var scheduler = new WorkScheduler(4, clock);
            for (int index = 0; index < 4; index++)
            {
                scheduler.TrySchedule(
                    new TestWorkItem(int.MaxValue)
                    {
                        Clock = clock,
                        StepCostMilliseconds = 0.75,
                    },
                    out _);
            }

            Assert.That(scheduler.Tick(10, 2.0), Is.EqualTo(3));
            SchedulerSnapshot snapshot = scheduler.GetSnapshot();
            Assert.That(snapshot.LastTickMilliseconds, Is.EqualTo(2.25).Within(0.0001));
            Assert.That(snapshot.OverBudgetTickCount, Is.EqualTo(1));
            Assert.That(snapshot.OutstandingCount, Is.EqualTo(4));
        }

        [Test]
        public void Tick_SingleStepCanExceedSoftBudget()
        {
            var clock = new ManualSchedulerClock();
            var scheduler = new WorkScheduler(1, clock);
            scheduler.TrySchedule(
                new TestWorkItem { Clock = clock, StepCostMilliseconds = 3.0 },
                out _);

            Assert.That(scheduler.Tick(1, 1.0), Is.EqualTo(1));
            Assert.That(scheduler.GetSnapshot().OverBudgetTickCount, Is.EqualTo(1));
        }

        [Test]
        public void Tick_ZeroStepOrTimeBudgetDoesNotStartWork()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(item, out _);

            Assert.That(scheduler.Tick(0, double.PositiveInfinity), Is.Zero);
            Assert.That(scheduler.Tick(1, 0), Is.Zero);
            Assert.That(item.StepCount, Is.Zero);
            Assert.That(scheduler.OutstandingCount, Is.EqualTo(1));
        }

        [Test]
        public void Cancel_MiddleItemPreservesRemainingFifoOrder()
        {
            var scheduler = new WorkScheduler(3);
            var order = new List<int>();
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Id = 1, ExecutionOrder = order }, out _);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Id = 2, ExecutionOrder = order }, out WorkItemHandle middle);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue) { Id = 3, ExecutionOrder = order }, out _);

            scheduler.Cancel(middle);
            scheduler.Tick(3, double.PositiveInfinity);

            Assert.That(order, Is.EqualTo(new[] { 1, 3 }));
            AssertInvariant(scheduler);
        }

        [Test]
        public void SlotReuseInvalidatesPreviousHandle()
        {
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out WorkItemHandle oldHandle);
            scheduler.Cancel(oldHandle);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out WorkItemHandle newHandle);

            Assert.That(scheduler.Cancel(oldHandle), Is.False);
            Assert.That(scheduler.OutstandingCount, Is.EqualTo(1));
            Assert.That(scheduler.Cancel(newHandle), Is.True);
        }

        [Test]
        public void TrySchedule_FullCapacityRejectsWithoutOwnershipTransfer()
        {
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out _);
            var rejected = new TestWorkItem();

            Assert.That(scheduler.TrySchedule(rejected, out WorkItemHandle handle), Is.False);
            Assert.That(handle.IsDefault, Is.True);
            Assert.That(rejected.TerminationCount, Is.Zero);
            Assert.That(scheduler.GetSnapshot().RejectedCount, Is.EqualTo(1));
            AssertInvariant(scheduler);
        }

        [Test]
        public void Cancel_RemovesItemAndRejectsStaleHandle()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(item, out WorkItemHandle handle);

            Assert.That(scheduler.Cancel(handle), Is.True);
            Assert.That(item.StepCount, Is.Zero);
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Cancelled));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            Assert.That(scheduler.Cancel(handle), Is.False);
            Assert.That(scheduler.Cancel(default), Is.False);
            AssertInvariant(scheduler);
        }

        [Test]
        public void StepExceptionFaultsItemAndSchedulerCanContinue()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem { StepFailure = new InvalidOperationException("Failure") };
            scheduler.TrySchedule(item, out _);

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick(1, 1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Faulted));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            AssertInvariant(scheduler);

            var next = new TestWorkItem();
            scheduler.TrySchedule(next, out _);
            Assert.That(scheduler.Tick(1, 1), Is.EqualTo(1));
            Assert.That(next.Reason, Is.EqualTo(WorkTerminationReason.Completed));
        }

        [Test]
        public void TerminationExceptionDoesNotCorruptState()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem { TerminationFailure = new InvalidOperationException("Callback") };
            scheduler.TrySchedule(item, out _);

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick(1, 1));
            SchedulerSnapshot snapshot = scheduler.GetSnapshot();
            Assert.That(snapshot.OutstandingCount, Is.Zero);
            Assert.That(snapshot.CompletedCount, Is.EqualTo(1));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            AssertInvariant(scheduler);
        }

        [Test]
        public void InvalidStepResultFaultsRegistration()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem { ReturnInvalidResult = true };
            scheduler.TrySchedule(item, out _);

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick(1, 1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Faulted));
            Assert.That(scheduler.GetSnapshot().FaultedCount, Is.EqualTo(1));
            AssertInvariant(scheduler);
        }

        [Test]
        public void ShutdownTerminatesAllAndIsIdempotent()
        {
            var scheduler = new WorkScheduler(3);
            var first = new TestWorkItem(int.MaxValue);
            var second = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(first, out _);
            scheduler.TrySchedule(second, out _);

            scheduler.Shutdown();
            scheduler.Shutdown();

            Assert.That(first.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            Assert.That(second.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            Assert.That(scheduler.Tick(1, 1), Is.Zero);
            Assert.That(scheduler.TrySchedule(new TestWorkItem(), out _), Is.False);
            Assert.That(scheduler.GetSnapshot().CancelledCount, Is.Zero);
            Assert.That(scheduler.GetSnapshot().ShutdownCount, Is.EqualTo(2));
            AssertInvariant(scheduler);
        }

        [Test]
        public void ShutdownStep_StopsAdmissionAndDrainsInBatchesWithoutExecutingWork()
        {
            var scheduler = new WorkScheduler(3);
            var first = new TestWorkItem(int.MaxValue);
            var second = new TestWorkItem(int.MaxValue);
            var third = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(first, out _);
            scheduler.TrySchedule(second, out _);
            scheduler.TrySchedule(third, out _);

            Assert.That(scheduler.ShutdownStep(0), Is.Zero);
            Assert.That(scheduler.IsShutdown, Is.True);
            Assert.That(scheduler.IsShutdownCompleted, Is.False);
            Assert.That(scheduler.TrySchedule(new TestWorkItem(), out _), Is.False);
            Assert.That(scheduler.Tick(3, 1), Is.Zero);

            Assert.That(scheduler.ShutdownStep(2), Is.EqualTo(2));
            Assert.That(first.TerminationCount, Is.EqualTo(1));
            Assert.That(second.TerminationCount, Is.EqualTo(1));
            Assert.That(third.TerminationCount, Is.Zero);
            Assert.That(scheduler.OutstandingCount, Is.EqualTo(1));

            Assert.That(scheduler.ShutdownStep(2), Is.EqualTo(1));
            Assert.That(scheduler.IsShutdownCompleted, Is.True);
            Assert.That(third.Reason, Is.EqualTo(WorkTerminationReason.Shutdown));
            Assert.That(first.StepCount + second.StepCount + third.StepCount, Is.Zero);
            AssertInvariant(scheduler);
        }

        [Test]
        public void ShutdownStep_CallbackFailuresDoNotBlockLaterDrain()
        {
            var scheduler = new WorkScheduler(3);
            scheduler.TrySchedule(
                new TestWorkItem(int.MaxValue) { TerminationFailure = new InvalidOperationException("First") },
                out _);
            scheduler.TrySchedule(
                new TestWorkItem(int.MaxValue) { TerminationFailure = new InvalidOperationException("Second") },
                out _);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out _);

            var failure = Assert.Throws<AggregateException>(() => scheduler.ShutdownStep(2));
            Assert.That(failure.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(scheduler.OutstandingCount, Is.EqualTo(1));
            Assert.That(scheduler.IsShutdownCompleted, Is.False);

            Assert.That(scheduler.ShutdownStep(2), Is.EqualTo(1));
            Assert.That(scheduler.IsShutdownCompleted, Is.True);
            AssertInvariant(scheduler);
        }

        [Test]
        public void Shutdown_AfterPartialDrainCompletesImmediately()
        {
            var scheduler = new WorkScheduler(2);
            var first = new TestWorkItem(int.MaxValue);
            var second = new TestWorkItem(int.MaxValue);
            scheduler.TrySchedule(first, out _);
            scheduler.TrySchedule(second, out _);
            scheduler.ShutdownStep(1);

            scheduler.Shutdown();

            Assert.That(first.TerminationCount, Is.EqualTo(1));
            Assert.That(second.TerminationCount, Is.EqualTo(1));
            Assert.That(scheduler.IsShutdownCompleted, Is.True);
            AssertInvariant(scheduler);
        }

        [Test]
        public void ShutdownAttemptsAllCallbacksBeforeThrowing()
        {
            var scheduler = new WorkScheduler(2);
            scheduler.TrySchedule(
                new TestWorkItem(int.MaxValue) { TerminationFailure = new InvalidOperationException("First") },
                out _);
            scheduler.TrySchedule(
                new TestWorkItem(int.MaxValue) { TerminationFailure = new InvalidOperationException("Second") },
                out _);

            var failure = Assert.Throws<AggregateException>(() => scheduler.Shutdown());
            Assert.That(failure.InnerExceptions.Count, Is.EqualTo(2));
            Assert.That(scheduler.OutstandingCount, Is.Zero);
            AssertInvariant(scheduler);
        }

        [TestCase(TestReentrantOperation.Schedule)]
        [TestCase(TestReentrantOperation.Cancel)]
        [TestCase(TestReentrantOperation.Tick)]
        [TestCase(TestReentrantOperation.Shutdown)]
        public void ReentrantStepOperationFaultsRegistration(TestReentrantOperation operation)
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem();
            item.ReentrantScheduler = scheduler;
            item.ReentrantOperation = operation;
            scheduler.TrySchedule(item, out _);

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick(1, 1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Faulted));
            AssertInvariant(scheduler);
        }

        [Test]
        public void ReentrantTerminationCallbackIsRejectedAfterStateIsReleased()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem();
            item.ReentrantScheduler = scheduler;
            item.ReentrantOperation = TestReentrantOperation.Cancel;
            item.ReenterOnStep = false;
            item.ReenterFromTermination = true;
            scheduler.TrySchedule(item, out _);

            Assert.Throws<InvalidOperationException>(() => scheduler.Tick(1, 1));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            Assert.That(scheduler.OutstandingCount, Is.Zero);
            AssertInvariant(scheduler);
        }

        [Test]
        public void SnapshotFromTerminationCallbackIsAllowed()
        {
            var scheduler = new WorkScheduler(1);
            var item = new TestWorkItem();
            item.ReentrantScheduler = scheduler;
            item.ReentrantOperation = TestReentrantOperation.Snapshot;
            item.ReenterOnStep = false;
            item.ReenterFromTermination = true;
            scheduler.TrySchedule(item, out _);

            Assert.DoesNotThrow(() => scheduler.Tick(1, 1));
            Assert.That(item.Reason, Is.EqualTo(WorkTerminationReason.Completed));
            Assert.That(item.TerminationCount, Is.EqualTo(1));
            Assert.That(scheduler.OutstandingCount, Is.Zero);
            AssertInvariant(scheduler);
        }

        [Test]
        public void GenerationWrapSkipsZeroAndInvalidatesPreviousHandle()
        {
            var scheduler = new WorkScheduler(1, new ManualSchedulerClock(), uint.MaxValue);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out WorkItemHandle oldHandle);
            scheduler.Cancel(oldHandle);
            scheduler.TrySchedule(new TestWorkItem(int.MaxValue), out WorkItemHandle newHandle);

            Assert.That(newHandle.Generation, Is.EqualTo(1));
            Assert.That(scheduler.Cancel(oldHandle), Is.False);
            Assert.That(scheduler.Cancel(newHandle), Is.True);
        }

        [Test]
        public void OwnerThreadViolationIsRejected()
        {
            var scheduler = new WorkScheduler(1);
            Exception failure = null;
            var thread = new Thread(() =>
            {
                try
                {
                    scheduler.GetSnapshot();
                }
                catch (Exception exception)
                {
                    failure = exception;
                }
            });

            thread.Start();
            thread.Join();
            Assert.That(failure, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void MixedOperationsPreserveCountingInvariant()
        {
            var scheduler = new WorkScheduler(5);
            var handles = new WorkItemHandle[5];
            for (int index = 0; index < handles.Length; index++)
            {
                scheduler.TrySchedule(new TestWorkItem(2), out handles[index]);
            }

            scheduler.Cancel(handles[1]);
            scheduler.Tick(3, double.PositiveInfinity);
            scheduler.Cancel(handles[3]);
            scheduler.Tick(5, double.PositiveInfinity);
            scheduler.Shutdown();
            AssertInvariant(scheduler);
        }

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
