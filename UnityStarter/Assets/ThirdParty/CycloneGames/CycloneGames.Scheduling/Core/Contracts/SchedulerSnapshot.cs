namespace CycloneGames.Scheduling.Core
{
    /// <summary>Owner-thread snapshot. Counters are monotonic; only OutstandingCount decreases.</summary>
    public readonly struct SchedulerSnapshot
    {
        public int Capacity { get; }

        public int OutstandingCount { get; }

        public long AcceptedCount { get; }

        public long RejectedCount { get; }

        public long CompletedCount { get; }

        /// <summary>Registrations stopped by an explicit <c>Cancel</c> call.</summary>
        public long CancelledCount { get; }

        /// <summary>Registrations whose Step threw or returned an undefined value.</summary>
        public long FaultedCount { get; }

        /// <summary>Registrations terminated by <c>Shutdown</c> or <c>ShutdownStep</c>.</summary>
        public long ShutdownCount { get; }

        public long StepCount { get; }

        public long TickCount { get; }

        public long OverBudgetTickCount { get; }

        public double LastTickMilliseconds { get; }

        public double MaxTickMilliseconds { get; }

        /// <summary>
        /// Longest single Step since construction. Sampled only under a finite soft budget,
        /// because that is the only path that reads the clock between steps.
        /// </summary>
        public double SlowestStepMilliseconds { get; }

        /// <summary>Handle of the registration behind SlowestStepMilliseconds. Diagnostic; may be stale.</summary>
        public WorkItemHandle SlowestStep { get; }

        public bool IsShutdown { get; }

        public bool IsShutdownCompleted { get; }

        internal SchedulerSnapshot(
            int capacity,
            int outstandingCount,
            long acceptedCount,
            long rejectedCount,
            long completedCount,
            long cancelledCount,
            long faultedCount,
            long shutdownCount,
            long stepCount,
            long tickCount,
            long overBudgetTickCount,
            double lastTickMilliseconds,
            double maxTickMilliseconds,
            double slowestStepMilliseconds,
            WorkItemHandle slowestStep,
            bool isShutdown,
            bool isShutdownCompleted)
        {
            Capacity = capacity;
            OutstandingCount = outstandingCount;
            AcceptedCount = acceptedCount;
            RejectedCount = rejectedCount;
            CompletedCount = completedCount;
            CancelledCount = cancelledCount;
            FaultedCount = faultedCount;
            ShutdownCount = shutdownCount;
            StepCount = stepCount;
            TickCount = tickCount;
            OverBudgetTickCount = overBudgetTickCount;
            LastTickMilliseconds = lastTickMilliseconds;
            MaxTickMilliseconds = maxTickMilliseconds;
            SlowestStepMilliseconds = slowestStepMilliseconds;
            SlowestStep = slowestStep;
            IsShutdown = isShutdown;
            IsShutdownCompleted = isShutdownCompleted;
        }
    }
}
