using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Internal logging bridge. Core provides safe defaults (Console.WriteLine).
    /// <para>
    /// The Unity adapter (<see cref="CycloneGames.DataTable.Unity.DataTableUnityBootstrap"/>)
    /// routes to Unity's Debug.Log* at startup, but only if the delegates are still at
    /// their default values. To use your own logger (e.g. CycloneGames.Logger), set the
    /// delegates BEFORE Unity initializes — or at any later point; they are simple
    /// static properties and the last write wins.
    /// </para>
    /// </summary>
    public static class DataTableLogger
    {
        // Named methods instead of lambdas so the bootstrap can detect that the
        // defaults haven't been replaced yet.
        private static void DefaultWarning(string msg) => Console.WriteLine($"[DataTable] WARNING: {msg}");
        private static void DefaultError(string msg) => Console.Error.WriteLine($"[DataTable] ERROR: {msg}");
        private static void DefaultInfo(string msg) => Console.WriteLine($"[DataTable] {msg}");

        /// <summary>Log a warning.</summary>
        public static Action<string> LogWarning { get; set; } = DefaultWarning;

        /// <summary>Log an error.</summary>
        public static Action<string> LogError { get; set; } = DefaultError;

        /// <summary>Log info.</summary>
        public static Action<string> LogInfo { get; set; } = DefaultInfo;

        /// <summary>
        /// True if the logging delegates have been replaced with non-default implementations.
        /// The bootstrap uses this to avoid overwriting externally injected loggers.
        /// </summary>
        public static bool IsDefault => LogWarning == (Action<string>)DefaultWarning;
    }
}
