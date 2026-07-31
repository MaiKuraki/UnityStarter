using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking.Editor.Diagnostics
{
    internal static class NetworkingEditorLog
    {
        internal const string Category = "CycloneGames.Networking.Editor.Bootstrap";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
