namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Fixed-size, allocation-free diagnostic view of a single bus. Counters are monotonic and expose
    /// no internal handler array and no object graph, so a profiler window, a MemoryGovernance metric
    /// source or a CI assertion can read it without touching bus internals.
    /// </summary>
    public readonly struct EventBusSnapshot
    {
        public EventBusSnapshot(
            int subscriptionCount,
            int tombstoneCount,
            long publishCount,
            long droppedReentrantCount,
            long subscriberErrorCount,
            int peakSubscriptionCount,
            int dispatchDepth,
            int capacity,
            bool isDisposed)
        {
            SubscriptionCount = subscriptionCount;
            TombstoneCount = tombstoneCount;
            PublishCount = publishCount;
            DroppedReentrantCount = droppedReentrantCount;
            SubscriberErrorCount = subscriberErrorCount;
            PeakSubscriptionCount = peakSubscriptionCount;
            DispatchDepth = dispatchDepth;
            Capacity = capacity;
            IsDisposed = isDisposed;
        }

        public int SubscriptionCount { get; }

        public int TombstoneCount { get; }

        public long PublishCount { get; }

        public long DroppedReentrantCount { get; }

        public long SubscriberErrorCount { get; }

        public int PeakSubscriptionCount { get; }

        public int DispatchDepth { get; }

        public int Capacity { get; }

        public bool IsDisposed { get; }
    }

    /// <summary>
    /// Fixed-size system-level diagnostic snapshot: the safe public read contract for tooling.
    /// It exposes counts only, never internal collections.
    /// </summary>
    public readonly struct EventBusDiagnosticsSnapshot
    {
        public static readonly EventBusDiagnosticsSnapshot Empty = new EventBusDiagnosticsSnapshot(
            0, 0, 0, 0, 0, 0, 0, 0);

        public EventBusDiagnosticsSnapshot(
            int activeBusCount,
            int scopeCount,
            int subscriptionCount,
            int tombstoneCount,
            long publishCount,
            long droppedReentrantCount,
            long subscriberErrorCount,
            int peakSubscriptionCount)
        {
            ActiveBusCount = activeBusCount;
            ScopeCount = scopeCount;
            SubscriptionCount = subscriptionCount;
            TombstoneCount = tombstoneCount;
            PublishCount = publishCount;
            DroppedReentrantCount = droppedReentrantCount;
            SubscriberErrorCount = subscriberErrorCount;
            PeakSubscriptionCount = peakSubscriptionCount;
        }

        public int ActiveBusCount { get; }

        public int ScopeCount { get; }

        public int SubscriptionCount { get; }

        public int TombstoneCount { get; }

        public long PublishCount { get; }

        public long DroppedReentrantCount { get; }

        public long SubscriberErrorCount { get; }

        public int PeakSubscriptionCount { get; }
    }
}
