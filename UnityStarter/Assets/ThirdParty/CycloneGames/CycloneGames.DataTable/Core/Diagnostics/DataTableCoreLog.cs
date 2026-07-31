using System;
using CycloneGames.Logging;

namespace CycloneGames.DataTable
{
    internal static class DataTableCoreLog
    {
        internal const string Category = "CycloneGames.DataTable";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        /// <summary>
        /// Best-effort logging for paths whose authoritative state transition has already committed.
        /// A writer failure must not make the completed transition appear to have failed.
        /// </summary>
        internal static void CommittedInfoNoThrow(string message)
        {
            try
            {
                Channel.Info(message);
            }
            catch (Exception exception)
            {
                try
                {
                    Channel.Error(
                        exception,
                        "An installed log writer threw after a committed state transition.");
                }
                catch (Exception)
                {
                    // Diagnostics are deliberately best-effort after the authoritative commit.
                }
            }
        }
    }
}
