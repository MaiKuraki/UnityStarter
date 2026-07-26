namespace CycloneGames.RPGFoundation.Movement.Runtime
{
    /// <summary>Allocation-free retained-policy diagnostics for one MovementComponent.</summary>
    public readonly struct MovementComponentMemorySnapshot
    {
        internal MovementComponentMemorySnapshot(
            int ignoredColliderCount,
            int ignoredColliderCapacity,
            long rejectedIgnoredColliderAdmissionCount)
        {
            IgnoredColliderCount = ignoredColliderCount;
            IgnoredColliderCapacity = ignoredColliderCapacity;
            RejectedIgnoredColliderAdmissionCount = rejectedIgnoredColliderAdmissionCount;
        }

        public int IgnoredColliderCount { get; }
        public int IgnoredColliderCapacity { get; }
        public long RejectedIgnoredColliderAdmissionCount { get; }
    }
}
