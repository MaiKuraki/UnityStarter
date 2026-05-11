using System;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Internal logging bridge. Core provides defaults (Console.WriteLine).
    /// Consumers can override delegates to route to their own logging system.
    /// </summary>
    public static class DeterministicMathLogger
    {
        private static void DefaultWarning(string msg) => Console.WriteLine($"[DeterministicMath] WARNING: {msg}");
        private static void DefaultError(string msg) => Console.Error.WriteLine($"[DeterministicMath] ERROR: {msg}");

        public static Action<string> LogWarning { get; set; } = DefaultWarning;
        public static Action<string> LogError { get; set; } = DefaultError;

        /// <summary>
        /// True if the delegates are still at their Core defaults.
        /// </summary>
        public static bool IsDefault => LogWarning == (Action<string>)DefaultWarning;
    }
}
