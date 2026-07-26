namespace CycloneGames.BehaviorTree.Runtime.Components
{
    /// <summary>
    /// Allocation-free snapshot of the priority tick manager's bounded wake-up admission state.
    /// </summary>
    public readonly struct BTPriorityTickManagerMemoryStats
    {
        public BTPriorityTickManagerMemoryStats(
            int registeredTreeCount,
            int pendingWakeUpCount,
            int pendingWakeUpCapacity,
            int pendingWakeUpPeak,
            long acceptedWakeUpCount,
            long coalescedWakeUpCount,
            long capacityRejectedWakeUpCount)
            : this(
                registeredTreeCount,
                pendingWakeUpCount,
                pendingWakeUpCapacity,
                pendingWakeUpPeak,
                acceptedWakeUpCount,
                coalescedWakeUpCount,
                capacityRejectedWakeUpCount,
                default,
                default)
        {
        }

        public BTPriorityTickManagerMemoryStats(
            int registeredTreeCount,
            int pendingWakeUpCount,
            int pendingWakeUpCapacity,
            int pendingWakeUpPeak,
            long acceptedWakeUpCount,
            long coalescedWakeUpCount,
            long capacityRejectedWakeUpCount,
            Core.BTPriorityTickManagerCoreMemoryStats core,
            BTDistanceLODProviderMemoryStats lod)
        {
            RegisteredTreeCount = registeredTreeCount;
            PendingWakeUpCount = pendingWakeUpCount;
            PendingWakeUpCapacity = pendingWakeUpCapacity;
            PendingWakeUpPeak = pendingWakeUpPeak;
            AcceptedWakeUpCount = acceptedWakeUpCount;
            CoalescedWakeUpCount = coalescedWakeUpCount;
            CapacityRejectedWakeUpCount = capacityRejectedWakeUpCount;
            Core = core;
            LOD = lod;
        }

        public int RegisteredTreeCount { get; }
        public int PendingWakeUpCount { get; }
        public int PendingWakeUpCapacity { get; }
        public int PendingWakeUpPeak { get; }
        public long AcceptedWakeUpCount { get; }
        public long CoalescedWakeUpCount { get; }
        public long CapacityRejectedWakeUpCount { get; }
        public Core.BTPriorityTickManagerCoreMemoryStats Core { get; }
        public BTDistanceLODProviderMemoryStats LOD { get; }
    }
}
