namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    /// <summary>Allocation-free admission diagnostics for the process animation hash cache.</summary>
    public readonly struct AnimationParameterCacheMemorySnapshot
    {
        internal AnimationParameterCacheMemorySnapshot(
            int entryCount,
            int entryCapacity,
            long rejectedAdmissionCount)
        {
            EntryCount = entryCount;
            EntryCapacity = entryCapacity;
            RejectedAdmissionCount = rejectedAdmissionCount;
        }

        public int EntryCount { get; }
        public int EntryCapacity { get; }
        public long RejectedAdmissionCount { get; }
    }
}
