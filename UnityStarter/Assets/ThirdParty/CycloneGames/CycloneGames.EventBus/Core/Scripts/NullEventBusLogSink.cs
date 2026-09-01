using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Null Object sink: silently discards all diagnostics. This is the default so the Core layer
    /// produces no logging dependency and no overhead when no backend is configured.
    /// </summary>
    public sealed class NullEventBusLogSink : IEventBusLogSink
    {
        public static readonly NullEventBusLogSink Instance = new NullEventBusLogSink();

        private NullEventBusLogSink()
        {
        }

        public bool IsEnabled(EventBusLogSeverity severity, string category) => false;

        public void Write(EventBusLogSeverity severity, string category, string message)
        {
        }

        public void WriteException(
            EventBusLogSeverity severity,
            string category,
            Exception exception,
            string message)
        {
        }
    }
}
