using System;
using System.Text;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Narrow, BCL-only logging port used by the EventBus Core for cold-path diagnostics.
    /// It is intentionally shaped like CycloneGames.Logging's writer surface but stays free of any
    /// concrete logging dependency; a separate integration assembly adapts this to a real backend.
    /// </summary>
    public interface IEventBusLogSink
    {
        bool IsEnabled(EventBusLogSeverity severity, string category);

        void Write(
            EventBusLogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder);

        void WriteException(
            EventBusLogSeverity severity,
            string category,
            Exception exception,
            string message);
    }
}
