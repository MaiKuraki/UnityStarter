using System;
using CycloneGames.Logging;

namespace CycloneGames.DataTable.Unity.Integrations.MessagePack
{
    internal static class DataTableMessagePackLog
    {
        internal const string Category = "CycloneGames.DataTable.MessagePack";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
