using System;
using CycloneGames.Logging;

namespace CycloneGames.DataTable.Unity.Editor
{
    internal static class DataTableEditorLog
    {
        internal const string Category = "CycloneGames.DataTable.Editor.Luban";
        internal const string SettingsCategory = "CycloneGames.DataTable.Editor.Settings";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);
        internal static readonly LogChannel SettingsChannel = LogChannel.Create(SettingsCategory);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }

        internal static LogChannel CreateSettings(ILogWriter logWriter)
        {
            return LogChannel.Create(
                SettingsCategory,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
