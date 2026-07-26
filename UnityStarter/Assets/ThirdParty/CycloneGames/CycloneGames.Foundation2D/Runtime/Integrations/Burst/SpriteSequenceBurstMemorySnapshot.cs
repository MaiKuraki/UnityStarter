namespace CycloneGames.Foundation2D.Runtime
{
    /// <summary>
    /// Stable, allocation-free diagnostics exposed by the real owner of the optional Burst buffers.
    /// Call from the Unity main thread.
    /// </summary>
    public interface ISpriteSequenceBurstMemoryOwner
    {
        SpriteSequenceBurstMemorySnapshot GetMemorySnapshot();
    }

    /// <summary>O(1) snapshot of manager-owned registrations, native capacity, and admission history.</summary>
    public readonly struct SpriteSequenceBurstMemorySnapshot
    {
        public SpriteSequenceBurstMemorySnapshot(
            int ownedControllerCount,
            int runtimeRegistrationCount,
            int bufferCapacity,
            int maximumControllerCapacity,
            int peakOwnedControllerCount,
            int peakBufferCapacity,
            long capacityRejectionCount,
            long ownershipConflictRejectionCount,
            long allocationFailureCount,
            bool isUpdating,
            bool isJobScheduled)
        {
            OwnedControllerCount = ownedControllerCount;
            RuntimeRegistrationCount = runtimeRegistrationCount;
            BufferCapacity = bufferCapacity;
            MaximumControllerCapacity = maximumControllerCapacity;
            PeakOwnedControllerCount = peakOwnedControllerCount;
            PeakBufferCapacity = peakBufferCapacity;
            CapacityRejectionCount = capacityRejectionCount;
            OwnershipConflictRejectionCount = ownershipConflictRejectionCount;
            AllocationFailureCount = allocationFailureCount;
            IsUpdating = isUpdating;
            IsJobScheduled = isJobScheduled;
        }

        public int OwnedControllerCount { get; }
        public int RuntimeRegistrationCount { get; }
        public int BufferCapacity { get; }
        public int MaximumControllerCapacity { get; }
        public int PeakOwnedControllerCount { get; }
        public int PeakBufferCapacity { get; }
        public long CapacityRejectionCount { get; }
        public long OwnershipConflictRejectionCount { get; }
        public long AllocationFailureCount { get; }
        public bool IsUpdating { get; }
        public bool IsJobScheduled { get; }
    }
}
