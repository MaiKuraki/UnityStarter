using System;
using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Sample
{
    internal static class InputSystemSampleLog
    {
        internal const string Category = "CycloneGames.InputSystem.Sample";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(
                Category,
                logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
