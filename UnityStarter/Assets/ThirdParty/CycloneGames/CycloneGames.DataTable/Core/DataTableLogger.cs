using System;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Process-wide logging bridge. A composition root may replace the delegates. Unity adapters
    /// should reset and inject them during subsystem registration so domain-reload-disabled play
    /// sessions do not retain stale delegates.
    /// </summary>
    public static class DataTableLogger
    {
        private static Action<string> _logWarning = DefaultWarning;
        private static Action<string> _logError = DefaultError;
        private static Action<string> _logInfo = DefaultInfo;

        private static void DefaultWarning(string message)
        {
            Console.WriteLine($"[DataTable] WARNING: {message}");
        }

        private static void DefaultError(string message)
        {
            Console.Error.WriteLine($"[DataTable] ERROR: {message}");
        }

        private static void DefaultInfo(string message)
        {
            Console.WriteLine($"[DataTable] {message}");
        }

        public static Action<string> LogWarning
        {
            get => _logWarning;
            set => _logWarning = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static Action<string> LogError
        {
            get => _logError;
            set => _logError = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static Action<string> LogInfo
        {
            get => _logInfo;
            set => _logInfo = value ?? throw new ArgumentNullException(nameof(value));
        }

        public static bool IsDefault =>
            LogWarning == (Action<string>)DefaultWarning &&
            LogError == (Action<string>)DefaultError &&
            LogInfo == (Action<string>)DefaultInfo;

        public static void ResetToDefaults()
        {
            LogWarning = DefaultWarning;
            LogError = DefaultError;
            LogInfo = DefaultInfo;
        }

        /// <summary>
        /// Best-effort logging for paths whose authoritative state transition has already committed.
        /// A diagnostic adapter failure must not make the completed transition appear to have failed.
        /// </summary>
        internal static void LogCommittedInfoNoThrow(string message)
        {
            try
            {
                LogInfo(message);
            }
            catch (Exception exception)
            {
                try
                {
                    DefaultError(
                        $"An injected info logger threw after a committed state transition. " +
                        $"LoggerException={exception.GetType().FullName}: {exception.Message}");
                }
                catch (Exception)
                {
                    // Diagnostics are deliberately best-effort after the authoritative commit.
                }
            }
        }
    }
}
