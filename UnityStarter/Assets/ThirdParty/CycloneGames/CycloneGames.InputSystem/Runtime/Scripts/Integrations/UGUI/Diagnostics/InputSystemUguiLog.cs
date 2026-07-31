using System;
using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Runtime
{
    internal static class InputSystemUguiLog
    {
        internal const string Category = "CycloneGames.InputSystem.UGUI";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
