using System;

namespace CycloneGames.GameplayFramework.Core
{
    /// <summary>Allocation-free actor admission diagnostics for one gameplay World.</summary>
    public readonly struct ActorAdmissionSnapshot
    {
        public ActorAdmissionSnapshot(
            int actorCount,
            int maximumActorCount,
            int allocatedActorCapacity,
            int peakActorCount,
            long rejectedAdmissionCount)
        {
            if (maximumActorCount <= 0 || maximumActorCount > WorldRuntimeLimits.MaximumSupportedActorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(maximumActorCount));
            }

            if (actorCount < 0 || actorCount > maximumActorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(actorCount));
            }

            if (allocatedActorCapacity < actorCount ||
                allocatedActorCapacity > WorldRuntimeLimits.MaximumSupportedActorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(allocatedActorCapacity));
            }

            if (peakActorCount < actorCount || peakActorCount > maximumActorCount)
            {
                throw new ArgumentOutOfRangeException(nameof(peakActorCount));
            }

            if (rejectedAdmissionCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(rejectedAdmissionCount));
            }

            ActorCount = actorCount;
            MaximumActorCount = maximumActorCount;
            AllocatedActorCapacity = allocatedActorCapacity;
            PeakActorCount = peakActorCount;
            RejectedAdmissionCount = rejectedAdmissionCount;
        }

        public int ActorCount { get; }
        public int MaximumActorCount { get; }
        public int AllocatedActorCapacity { get; }
        public int PeakActorCount { get; }
        public long RejectedAdmissionCount { get; }
    }
}
