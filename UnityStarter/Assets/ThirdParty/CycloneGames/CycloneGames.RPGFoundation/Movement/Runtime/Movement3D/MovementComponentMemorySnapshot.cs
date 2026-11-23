namespace CycloneGames.RPGFoundation.Movement.Runtime
{
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
