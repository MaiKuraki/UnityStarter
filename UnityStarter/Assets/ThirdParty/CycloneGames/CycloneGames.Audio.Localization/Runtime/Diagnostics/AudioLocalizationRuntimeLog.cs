using System;
using CycloneGames.Logging;

namespace CycloneGames.Audio.Runtime.Integrations.Localization
{
    internal static class AudioLocalizationRuntimeLog
    {
        internal const string Category = "CycloneGames.Audio.Localization";

        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
