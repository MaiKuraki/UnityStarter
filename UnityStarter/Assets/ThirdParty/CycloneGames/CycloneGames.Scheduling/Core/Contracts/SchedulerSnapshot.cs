namespace CycloneGames.Scheduling.Core
{
    /// <summary>An owner-thread snapshot. It is not a cross-thread consistent read.</summary>
    public readonly struct SchedulerSnapshot
    {
        public int Capacity { get; }
        public int OutstandingCount { get; }
        public long AcceptedCount { get; }
        public long RejectedCount { get; }
        public long CompletedCount { get; }
        public long CancelledCount { get; }
        public long FaultedCount { get; }
        public long StepCount { get; }
        public long TickCount { get; }
        public long OverBudgetTickCount { get; }
        public double LastTickMilliseconds { get; }
        public double MaxTickMilliseconds { get; }
        public bool IsShutdown { get; }

        internal SchedulerSnapshot(
            int capacity,
            int outstandingCount,
            long acceptedCount,
            long rejectedCount,
            long completedCount,
            long cancelledCount,
            long faultedCount,
            long stepCount,
            long tickCount,
            long overBudgetTickCount,
            double lastTickMilliseconds,
            double maxTickMilliseconds,
            bool isShutdown)
        {
            Capacity = capacity;
            OutstandingCount = outstandingCount;
            AcceptedCount = acceptedCount;
            RejectedCount = rejectedCount;
            CompletedCount = completedCount;
            CancelledCount = cancelledCount;
            FaultedCount = faultedCount;
            StepCount = stepCount;
            TickCount = tickCount;
            OverBudgetTickCount = overBudgetTickCount;
            LastTickMilliseconds = lastTickMilliseconds;
            MaxTickMilliseconds = maxTickMilliseconds;
            IsShutdown = isShutdown;
        }
    }
}
