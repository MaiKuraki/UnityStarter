namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Fixed-size, allocation-free diagnostics view of one bounded container (an
    /// <see cref="MpscEventQueue{T}"/>, an <see cref="EventStream{T}"/>, a command publisher, or a
    /// scheduler such as the pump). Counters that do not apply to a container are 0 / false, so a
    /// single flat row can render every container.
    /// </summary>
    public readonly struct EventBusContainerSnapshot
    {
        /// <summary>Stable display name, e.g. "MpscEventQueue&lt;ValueChangedEvent&gt;".</summary>
        public readonly string Name;

        /// <summary>Fixed capacity. For a scheduler with no buffer this carries the registered
        /// source count instead (see the implementer's XML docs).</summary>
        public readonly int Capacity;

        /// <summary>Events currently buffered and awaiting a drain/flush. Advisory on cross-thread
        /// containers.</summary>
        public readonly int PendingCount;

        /// <summary>Confirmed loss: events that entered the container and were discarded unread.</summary>
        public readonly long DroppedCount;

        /// <summary>Refused writes (backpressure). Not a loss count: a retrying caller loses
        /// nothing.</summary>
        public readonly long RejectedCount;

        /// <summary>Writes refused because the container was already closed. Diagnostics only.</summary>
        public readonly long RejectedAfterCloseCount;

        /// <summary>True once the container has been closed to new writes.</summary>
        public readonly bool IsClosed;

        public EventBusContainerSnapshot(
            string name,
            int capacity,
            int pendingCount,
            long droppedCount,
            long rejectedCount,
            long rejectedAfterCloseCount,
            bool isClosed)
        {
            Name = name;
            Capacity = capacity;
            PendingCount = pendingCount;
            DroppedCount = droppedCount;
            RejectedCount = rejectedCount;
            RejectedAfterCloseCount = rejectedAfterCloseCount;
            IsClosed = isClosed;
        }
    }

    /// <summary>
    /// Narrow port that exposes one bounded container's backpressure counters to aggregation
    /// tooling (the debugger window, an external metrics sink). It exists because the bus-level
    /// <see cref="IEventBusDiagnostics"/> only describes a single <see cref="EventBus{T}"/>: without
    /// this port the queue/stream/pump counters are collected but unreachable, which hides the first
    /// place a backpressure problem shows up.
    ///
    /// Implemented by each container; registered on the context with
    /// <c>EventBusContext.RegisterContainer</c>. Read-only and side-effect-free.
    /// </summary>
    public interface IEventBusContainerDiagnostics
    {
        /// <summary>Reads the current counters into a fixed-size snapshot. Allocation-free.</summary>
        EventBusContainerSnapshot GetContainerSnapshot();
    }
}
