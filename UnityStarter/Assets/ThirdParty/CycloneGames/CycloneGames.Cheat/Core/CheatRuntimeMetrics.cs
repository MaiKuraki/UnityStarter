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

        public CheatRuntimeMetrics(
            int runningCommandCount,
            long publishedCommandCount,
            long completedCommandCount,
            long droppedDuplicateCount,
            long cancelRequestedCount,
            long faultedCommandCount)
        {
            RunningCommandCount = runningCommandCount;
            PublishedCommandCount = publishedCommandCount;
            CompletedCommandCount = completedCommandCount;
            DroppedDuplicateCount = droppedDuplicateCount;
            CancelRequestedCount = cancelRequestedCount;
            FaultedCommandCount = faultedCommandCount;
        }
    }
}
