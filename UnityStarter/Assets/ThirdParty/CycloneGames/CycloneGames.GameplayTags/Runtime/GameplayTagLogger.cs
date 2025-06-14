using System;

namespace CycloneGames.GameplayTags.Runtime
{
    /// <summary>
    /// Provides a simple, pluggable logger for the GameplayTags system.
    /// This allows the core library to remain engine-agnostic.
    /// A consumer can assign their own logging implementation, for example:
    /// GameplayTagLogger.LogWarning = MyEngine.Logger.Warning;
    /// </summary>
    public static class GameplayTagLogger
    {
        /// <summary>
        /// Action to execute for warning messages.
        /// </summary>
        public static Action<string> LogWarning = message => { }; // Default implementation does nothing.
    }
}