using System;
using CycloneGames.Logging;

namespace CycloneGames.EventBus.Runtime.Integrations.Logging
{
    /// <summary>
    /// Log facade for the EventBus logging integration. Keeps every <see cref="LogChannel.Create"/>
    /// call inside this facade (CG0050): EventBus categories are per-event (<c>typeof(T).Name</c>),
    /// so the adapter routes through <see cref="CreateForCategory"/> instead of the fixed
    /// <see cref="Channel"/>.
    /// </summary>
    internal static class EventBusLoggingLog
    {
        internal const string Category = "CycloneGames.EventBus";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        internal static LogChannel CreateForCategory(string category, ILogWriter logWriter)
        {
            return LogChannel.Create(category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
