namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Immutable composition choices for an EventBus. Built by the composition root and consumed by
    /// the facade and the per-bus instances.
    ///
    /// The default error policy is <see cref="PublishErrorPolicy.ContinueOnError"/>, not
    /// <see cref="PublishErrorPolicy.Stop"/>. On a bus with more than a handful of subscribers,
    /// <c>Stop</c> means one broken listener silently costs every later listener its delivery — the
    /// failure mode that is hardest to trace in a large project. <c>ContinueOnError</c> still
    /// surfaces the fault (rethrown after the round) and still never skips a subscriber.
    /// </summary>
    public sealed class EventBusConfiguration
    {
        public const int DefaultCommandQueueCapacity = 64;

        /// <summary>
        /// Default re-entrancy ceiling. A stack-depth guard, not a policy knob, so it stays loose
        /// enough that a legitimate cascade never reaches it: each hop that publishes its own event
        /// type costs one level, and interactions routinely cascade (hit -> damage -> death -> loot ->
        /// quest -> achievement -> UI). Exceeding it is silent at the callsite, so the default is
        /// generous while still bounding the stack.
        /// </summary>
        public const int DefaultMaxDispatchDepth = 64;

        public static readonly EventBusConfiguration Default = new EventBusConfiguration();

        public EventBusConfiguration(
            int commandQueueCapacity = DefaultCommandQueueCapacity,
            CommandOverflowPolicy commandOverflowPolicy = CommandOverflowPolicy.Drop,
            int maxDispatchDepth = DefaultMaxDispatchDepth,
            PublishErrorPolicy publishErrorPolicy = PublishErrorPolicy.ContinueOnError,
            IEventBusLogSink logSink = null)
        {
            if (commandQueueCapacity <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(commandQueueCapacity));
            }

            if (maxDispatchDepth <= 0)
            {
                throw new System.ArgumentOutOfRangeException(nameof(maxDispatchDepth));
            }

            CommandQueueCapacity = commandQueueCapacity;
            CommandOverflowPolicy = commandOverflowPolicy;
            MaxDispatchDepth = maxDispatchDepth;
            LogSink = logSink ?? NullEventBusLogSink.Instance;
            PublishErrorPolicy = publishErrorPolicy;
        }

        public int CommandQueueCapacity { get; }

        public CommandOverflowPolicy CommandOverflowPolicy { get; }

        public int MaxDispatchDepth { get; }

        public IEventBusLogSink LogSink { get; }

        public PublishErrorPolicy PublishErrorPolicy { get; }
    }
}
