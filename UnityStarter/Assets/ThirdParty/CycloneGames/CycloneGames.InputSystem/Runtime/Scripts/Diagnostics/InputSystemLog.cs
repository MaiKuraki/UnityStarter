using System;
using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class InputSystemLog
    {
        internal const string Category = "CycloneGames.InputSystem";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
