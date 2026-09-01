using System;

namespace CycloneGames.EventBus.Core
{
    /// <summary>
    /// Narrow, BCL-only logging port used by the EventBus Core for cold-path diagnostics.
    /// It is intentionally free of any concrete logging dependency; a separate integration assembly
    /// adapts this to a real backend.
    ///
    /// <see cref="Write"/> takes a finished string rather than an
    /// <c>Action&lt;StringBuilder&gt;</c> builder. A builder would let the caller defer formatting
    /// until after the enable check, which is a real win for interpolated messages — but every
    /// message the bus emits is a constant literal, so the deferral bought nothing while costing a
    /// closure display class on every subscribe and unsubscribe. Measured: 24 bytes per call,
    /// allocated even when the sink was disabled, because Roslyn hoists the display class to the top
    /// of the enclosing method, ahead of the guard.
    ///
    /// A sink that needs to enrich a message can do so on its side of the boundary.
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
