using System;
using System.Collections.Generic;
using CycloneGames.Scheduling.Core;

namespace CycloneGames.Scheduling.Tests.Editor
{
    public enum TestReentrantOperation
    {
        None = 0,
        Schedule = 1,
        Cancel = 2,
        Tick = 3,
        Shutdown = 4,
        Snapshot = 5,
    }

    internal sealed class TestWorkItem : IWorkItem
    {
        private readonly int stepsUntilCompleted;

        public int Id { get; set; }
        public int StepCount { get; private set; }
        public int TerminationCount { get; private set; }
        public WorkTerminationReason? Reason { get; private set; }
        public List<int> ExecutionOrder { get; set; }
        public Exception StepFailure { get; set; }
        public Exception TerminationFailure { get; set; }
        public bool ReturnInvalidResult { get; set; }
        public ManualSchedulerClock Clock { get; set; }
        public double StepCostMilliseconds { get; set; }
        public WorkScheduler ReentrantScheduler { get; set; }
        public TestReentrantOperation ReentrantOperation { get; set; }
        public bool ReenterFromTermination { get; set; }

        public TestWorkItem(int stepsUntilCompleted = 1)
        {
            this.stepsUntilCompleted = stepsUntilCompleted;
        }

        public WorkStepResult Step()
        {
            StepCount++;
            ExecutionOrder?.Add(Id);
            Clock?.Advance(StepCostMilliseconds);

            if (ReentrantScheduler != null)
            {
                ExecuteReentrantOperation();
            }

            if (StepFailure != null)
            {
                throw StepFailure;
            }

            if (ReturnInvalidResult)
            {
                return (WorkStepResult)int.MaxValue;
            }

            return StepCount >= stepsUntilCompleted ? WorkStepResult.Completed : WorkStepResult.Running;
        }

        public void OnTerminated(WorkTerminationReason reason)
        {
            TerminationCount++;
            Reason = reason;
            if (ReenterFromTermination)
            {
                ReentrantScheduler.GetSnapshot();
            }

            if (TerminationFailure != null)
            {
                throw TerminationFailure;
            }
        }

        private void ExecuteReentrantOperation()
        {
            if (ReentrantOperation == TestReentrantOperation.Schedule)
            {
                ReentrantScheduler.TrySchedule(this, out _);
            }
            else if (ReentrantOperation == TestReentrantOperation.Cancel)
            {
                ReentrantScheduler.Cancel(default);
            }
            else if (ReentrantOperation == TestReentrantOperation.Tick)
            {
                ReentrantScheduler.Tick(1, 1);
            }
            else if (ReentrantOperation == TestReentrantOperation.Shutdown)
            {
                ReentrantScheduler.Shutdown();
            }
            else if (ReentrantOperation == TestReentrantOperation.Snapshot)
            {
                ReentrantScheduler.GetSnapshot();
            }
        }
    }

    internal sealed class ManualSchedulerClock : ISchedulerClock
    {
        private long now;

        public void Advance(double milliseconds)
        {
            now += (long)(milliseconds * 1000.0);
        }

        public long GetTimestamp()
        {
            return now;
        }

        public double ToMilliseconds(long ticks)
        {
            return ticks / 1000.0;
        }
    }
}
