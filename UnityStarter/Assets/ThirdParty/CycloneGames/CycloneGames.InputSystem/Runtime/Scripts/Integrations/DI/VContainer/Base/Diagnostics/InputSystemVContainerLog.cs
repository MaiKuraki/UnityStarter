using System;
using CycloneGames.Logging;

namespace CycloneGames.InputSystem.Runtime.Integrations.VContainer
{
    internal static class InputSystemVContainerLog
    {
        internal const string Category = "CycloneGames.InputSystem.VContainer";
        internal static readonly LogChannel Channel = LogChannel.Create(Category);

        internal static LogChannel Create(ILogWriter logWriter)
        {
            return LogChannel.Create(Category, logWriter ?? throw new ArgumentNullException(nameof(logWriter)));
        }
    }
}
