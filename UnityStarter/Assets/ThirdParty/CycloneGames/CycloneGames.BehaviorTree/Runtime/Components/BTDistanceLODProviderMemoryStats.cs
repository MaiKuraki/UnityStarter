namespace CycloneGames.BehaviorTree.Runtime.Components
{
    public readonly struct BTDistanceLODProviderMemoryStats
    {
        public BTDistanceLODProviderMemoryStats(
            int treeCount,
            int treeStorageCapacity,
            int maximumTreeCount,
            int peakTreeCount,
            long capacityRejectedTreeCount)
        {
            TreeCount = treeCount;
            TreeStorageCapacity = treeStorageCapacity;
            MaximumTreeCount = maximumTreeCount;
            PeakTreeCount = peakTreeCount;
            CapacityRejectedTreeCount = capacityRejectedTreeCount;
        }

        public int TreeCount { get; }
        public int TreeStorageCapacity { get; }
        public int MaximumTreeCount { get; }
        public int PeakTreeCount { get; }
        public long CapacityRejectedTreeCount { get; }
    }
}
