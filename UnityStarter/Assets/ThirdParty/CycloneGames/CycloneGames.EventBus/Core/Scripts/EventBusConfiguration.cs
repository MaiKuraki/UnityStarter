namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Immutable composition choices for an EventBus. Built by the composition root and consumed by
    /// the facade and the per-bus instances.
    /// </summary>
    public sealed class EventBusConfiguration
    {
        public const int DefaultCommandQueueCapacity = 64;
        public const int DefaultMaxDispatchDepth = 32;

        public static readonly EventBusConfiguration Default = new EventBusConfiguration();

        public EventBusConfiguration(
            CommandBackend commandBackend = CommandBackend.InProcess,
            int commandQueueCapacity = DefaultCommandQueueCapacity,
            CommandOverflowPolicy commandOverflowPolicy = CommandOverflowPolicy.Drop,
            int maxDispatchDepth = DefaultMaxDispatchDepth,
            PublishErrorPolicy publishErrorPolicy = PublishErrorPolicy.Stop,
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

            CommandBackend = commandBackend;
            CommandQueueCapacity = commandQueueCapacity;
            CommandOverflowPolicy = commandOverflowPolicy;
            MaxDispatchDepth = maxDispatchDepth;
            LogSink = logSink ?? NullEventBusLogSink.Instance;
            PublishErrorPolicy = publishErrorPolicy;
        }

        public CommandBackend CommandBackend { get; }

        public int CommandQueueCapacity { get; }

        public CommandOverflowPolicy CommandOverflowPolicy { get; }

        public int MaxDispatchDepth { get; }

        public IEventBusLogSink LogSink { get; }

        public PublishErrorPolicy PublishErrorPolicy { get; }
    }
}
