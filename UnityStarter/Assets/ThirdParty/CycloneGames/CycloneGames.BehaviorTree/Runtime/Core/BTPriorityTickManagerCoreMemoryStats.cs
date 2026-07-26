namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public readonly struct BTPriorityTickManagerCoreMemoryStats
    {
        public BTPriorityTickManagerCoreMemoryStats(
            int treeCount,
            int treeStorageCapacity,
            int maximumTreeCount,
            int peakTreeCount,
            int pendingMutationCount,
            int maximumPendingMutationCount,
            int peakPendingMutationCount,
            long capacityRejectedTreeCount,
            long capacityRejectedMutationCount)
        {
            TreeCount = treeCount;
            TreeStorageCapacity = treeStorageCapacity;
            MaximumTreeCount = maximumTreeCount;
            PeakTreeCount = peakTreeCount;
            PendingMutationCount = pendingMutationCount;
            MaximumPendingMutationCount = maximumPendingMutationCount;
            PeakPendingMutationCount = peakPendingMutationCount;
            CapacityRejectedTreeCount = capacityRejectedTreeCount;
            CapacityRejectedMutationCount = capacityRejectedMutationCount;
        }

        public int TreeCount { get; }
        public int TreeStorageCapacity { get; }
        public int MaximumTreeCount { get; }
        public int PeakTreeCount { get; }
        public int PendingMutationCount { get; }
        public int MaximumPendingMutationCount { get; }
        public int PeakPendingMutationCount { get; }
        public long CapacityRejectedTreeCount { get; }
        public long CapacityRejectedMutationCount { get; }
    }
}
