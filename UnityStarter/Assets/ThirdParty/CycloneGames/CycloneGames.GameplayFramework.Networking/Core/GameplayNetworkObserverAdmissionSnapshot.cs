namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>Allocation-free observer admission diagnostics for one registry.</summary>
    public readonly struct GameplayNetworkObserverAdmissionSnapshot
    {
        internal GameplayNetworkObserverAdmissionSnapshot(
            int observerCount,
            int maximumObserverCount,
            long rejectedAdmissionCount)
        {
            ObserverCount = observerCount;
            MaximumObserverCount = maximumObserverCount;
            RejectedAdmissionCount = rejectedAdmissionCount;
        }

        public int ObserverCount { get; }
        public int MaximumObserverCount { get; }
        public long RejectedAdmissionCount { get; }
    }
}
