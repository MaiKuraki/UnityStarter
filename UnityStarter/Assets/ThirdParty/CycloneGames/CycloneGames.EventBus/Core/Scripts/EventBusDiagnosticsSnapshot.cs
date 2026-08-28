namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Fixed-size, allocation-free diagnostic view of a single bus. Counters are monotonic and do not
    /// expose the internal handler array or any object graph.
    /// </summary>
    public readonly struct EventBusSnapshot
    {
        public EventBusSnapshot(
            int subscriptionCount,
            int tombstoneCount,
            long publishCount,
            long droppedReentrantCount,
            int dispatchDepth,
            bool isDisposed)
        {
            SubscriptionCount = subscriptionCount;
            TombstoneCount = tombstoneCount;
            PublishCount = publishCount;
            DroppedReentrantCount = droppedReentrantCount;
            DispatchDepth = dispatchDepth;
            IsDisposed = isDisposed;
        }

        public int SubscriptionCount { get; }

        public int TombstoneCount { get; }

        public long PublishCount { get; }

        public long DroppedReentrantCount { get; }

        public int DispatchDepth { get; }

        public bool IsDisposed { get; }
    }

    /// <summary>
    /// Fixed-size system-level diagnostic snapshot. Intended as the safe public read contract for a
    /// future MemoryGovernance metric source; it exposes counts only, never internal collections.
    /// </summary>
    public readonly struct EventBusDiagnosticsSnapshot
    {
        public static readonly EventBusDiagnosticsSnapshot Empty =
            new EventBusDiagnosticsSnapshot(0, 0, 0, 0, 0);

        public EventBusDiagnosticsSnapshot(
            int activeBusCount,
            int scopeCount,
            int subscriptionCount,
            int tombstoneCount,
            long publishCount)
        {
            ActiveBusCount = activeBusCount;
            ScopeCount = scopeCount;
            SubscriptionCount = subscriptionCount;
            TombstoneCount = tombstoneCount;
            PublishCount = publishCount;
        }

        public int ActiveBusCount { get; }

        public int ScopeCount { get; }

        public int SubscriptionCount { get; }

        public int TombstoneCount { get; }

        public long PublishCount { get; }
    }
}
