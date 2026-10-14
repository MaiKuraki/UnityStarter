namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Immutable composition choices for an EventBus, built by the composition root.
    /// The default error policy is <see cref="PublishErrorPolicy.ContinueOnError"/>: on a bus with
    /// many subscribers, <c>Stop</c> lets one broken listener silently cost every later listener
    /// its delivery. <c>ContinueOnError</c> surfaces the fault after the round and never skips a
    /// subscriber.
    /// </summary>
    public sealed class EventBusConfiguration
    {
        public const int DefaultCommandQueueCapacity = 64;

        /// <summary>
        /// Default re-entrancy ceiling. A stack-depth guard, not a policy knob: generous enough
        /// that a legitimate cascade never reaches it, bounded enough to prevent a stack overflow.
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
