using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Narrow, BCL-only logging port for cold-path diagnostics, free of any concrete logging
    /// dependency; an integration assembly adapts it to a real backend.
    ///
    /// <see cref="Write"/> takes a finished string rather than a builder: every message the bus
    /// emits is a constant literal, so deferred formatting would only add a closure allocation.
    /// </summary>
    public interface IEventBusLogSink
    {
        /// <summary>
        /// Whether <paramref name="severity"/> is enabled for <paramref name="category"/>. Called
        /// before <see cref="Write"/> on every log attempt, so it must be cheap and side-effect free.
        /// </summary>
        bool IsEnabled(EventBusLogSeverity severity, string category);

        void Write(EventBusLogSeverity severity, string category, string message);

        void WriteException(
            EventBusLogSeverity severity,
            string category,
            Exception exception,
            string message);
    }
}
