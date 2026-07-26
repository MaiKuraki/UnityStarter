namespace CycloneGames.Cheat.Core
{
    public readonly struct CheatRuntimeMetrics
    {
        public readonly int RunningCommandCount;
        public readonly long PublishedCommandCount;
        public readonly long CompletedCommandCount;
        public readonly long DroppedDuplicateCount;
        public readonly long CancelRequestedCount;
        public readonly long FaultedCommandCount;
        public readonly long CapacityRejectedCommandCount;
        public readonly int MaximumConcurrentCommandCount;

        public CheatRuntimeMetrics(
            int runningCommandCount,
            long publishedCommandCount,
            long completedCommandCount,
            long droppedDuplicateCount,
            long cancelRequestedCount,
            long faultedCommandCount)
            : this(
                runningCommandCount,
                publishedCommandCount,
                completedCommandCount,
                droppedDuplicateCount,
                cancelRequestedCount,
                faultedCommandCount,
                0L,
                0)
        {
        }

        public CheatRuntimeMetrics(
            int runningCommandCount,
            long publishedCommandCount,
            long completedCommandCount,
            long droppedDuplicateCount,
            long cancelRequestedCount,
            long faultedCommandCount,
            long capacityRejectedCommandCount,
            int maximumConcurrentCommandCount)
        {
            RunningCommandCount = runningCommandCount;
            PublishedCommandCount = publishedCommandCount;
            CompletedCommandCount = completedCommandCount;
            DroppedDuplicateCount = droppedDuplicateCount;
            CancelRequestedCount = cancelRequestedCount;
            FaultedCommandCount = faultedCommandCount;
            CapacityRejectedCommandCount = capacityRejectedCommandCount;
            MaximumConcurrentCommandCount = maximumConcurrentCommandCount;
        }
    }
}
