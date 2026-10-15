namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Snapshot of the most recent subscriber exception a bus handled. Diagnostics tooling only;
    /// never allocated on the dispatch path.
    /// </summary>
    public readonly struct EventBusFaultInfo
    {
        /// <summary>Handled subscriber exceptions since construction (monotonic).</summary>
        public readonly int Count;

        /// <summary>Full type name of the thrown exception (e.g. "System.InvalidOperationException").</summary>
        public readonly string ExceptionTypeName;

        /// <summary>
        /// Exception message, truncated to 256 characters to bound the memory the bus retains.
        /// </summary>
        public readonly string Message;

        /// <summary>Handler method name that threw. May be stripped in a Player build; the consumer is Editor tooling.</summary>
        public readonly string SubscriberName;

        /// <summary>Type declaring the throwing handler. Same stripping caveat as <see cref="SubscriberName"/>.</summary>
        public readonly string DeclaringTypeName;

        public EventBusFaultInfo(
            int count,
            string exceptionTypeName,
            string message,
            string subscriberName,
            string declaringTypeName)
        {
            Count = count;
            ExceptionTypeName = exceptionTypeName;
            Message = message;
            SubscriberName = subscriberName;
            DeclaringTypeName = declaringTypeName;
        }
    }

    /// <summary>
    /// Cold-path port exposing the most recent subscriber exception, separate from
    /// <see cref="IEventBusDiagnostics"/> so the counter contract stays stable. It exists so a
    /// diagnostics UI can answer "which subscription failed, with what exception, and why" without
    /// sending the reader to the Console.
    ///
    /// Reads delegate metadata (<c>Delegate.Method</c>), which a Player build may strip — intended
    /// for Editor tooling, the same contract as <see cref="IEventBusSubscriberInspection"/>. Owner
    /// thread only, like every other bus member.
    /// </summary>
    public interface IEventBusFaultInspection
    {
        /// <summary>
        /// The policy this bus applies to subscriber exceptions. Per-bus: two buses on one context
        /// may use different configurations, so a diagnostics UI must read the policy from the bus
        /// itself, never from the context.
        /// </summary>
        PublishErrorPolicy PublishErrorPolicy { get; }

        /// <summary>
        /// Returns the most recent handled subscriber exception. False when the bus has never
        /// handled one. After <see cref="System.IDisposable.Dispose"/> the last recorded value is
        /// still returned (Dispose does not clear diagnostics).
        /// </summary>
        bool TryGetLastFault(out EventBusFaultInfo fault);
    }
}
