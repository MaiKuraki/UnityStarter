using System;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Runtime.Integrations.Logging
{
    /// <summary>
    /// Adapts the Core's narrow <see cref="IEventBusLogSink"/> port to CycloneGames.Logging.
    /// CycloneGames.Logging types appear only in this integration assembly; the Core layer stays
    /// neutral.
    /// </summary>
    public sealed class CycloneGamesLogSinkAdapter : IEventBusLogSink
    {
        private readonly ILogWriter _writer;

        public CycloneGamesLogSinkAdapter(ILogWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public bool IsEnabled(EventBusLogSeverity severity, string category)
        {
            return EventBusLoggingLog.CreateForCategory(category, _writer).IsEnabled(Map(severity));
        }

        public void Write(
            EventBusLogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder)
        {
            EventBusLoggingLog.CreateForCategory(category, _writer).Write(Map(severity), messageBuilder);
        }

        public void WriteException(
            EventBusLogSeverity severity,
            string category,
            Exception exception,
            string message)
        {
            EventBusLoggingLog.CreateForCategory(category, _writer).WriteException(Map(severity), exception, message);
        }

        private static LogSeverity Map(EventBusLogSeverity severity)
        {
            switch (severity)
            {
                case EventBusLogSeverity.Debug:
                    return LogSeverity.Debug;
                case EventBusLogSeverity.Info:
                    return LogSeverity.Info;
                case EventBusLogSeverity.Warning:
                    return LogSeverity.Warning;
                case EventBusLogSeverity.Error:
                    return LogSeverity.Error;
                default:
                    return LogSeverity.Info;
            }
        }
    }
}
