namespace CycloneGames.GameplayFramework.Runtime
{
    /// <summary>Allocation-free actor admission diagnostics for one World.</summary>
    public readonly struct WorldActorAdmissionSnapshot
    {
        internal WorldActorAdmissionSnapshot(int actorCount, int actorCapacity, long rejectedAdmissionCount)
        {
            ActorCount = actorCount;
            ActorCapacity = actorCapacity;
            RejectedAdmissionCount = rejectedAdmissionCount;
        }

        public int ActorCount { get; }
        public int ActorCapacity { get; }
        public long RejectedAdmissionCount { get; }
    }
}
