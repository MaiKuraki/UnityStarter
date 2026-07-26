namespace CycloneGames.GameplayFramework.Networking
{
    /// <summary>Allocation-free observer admission diagnostics for one registry.</summary>
    public readonly struct GameplayNetworkObserverAdmissionSnapshot
    {
        internal GameplayNetworkObserverAdmissionSnapshot(
            int observerCount,
            int observerCapacity,
            long rejectedAdmissionCount)
        {
            ObserverCount = observerCount;
            ObserverCapacity = observerCapacity;
            RejectedAdmissionCount = rejectedAdmissionCount;
        }

        public int ObserverCount { get; }
        public int ObserverCapacity { get; }
        public long RejectedAdmissionCount { get; }
    }
}
