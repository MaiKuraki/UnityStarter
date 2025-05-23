using System.Runtime.CompilerServices;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Provides pre-cached string representations for LogLevel values.
    /// </summary>
    internal static class LogLevelStrings
    {
        private static readonly string[] _levelStrings = new string[(int)LogLevel.None + 1];

        static LogLevelStrings()
        {
            _levelStrings[(int)LogLevel.Trace] = "TRACE";
            _levelStrings[(int)LogLevel.Debug] = "DEBUG";
            _levelStrings[(int)LogLevel.Info] = "INFO";
            _levelStrings[(int)LogLevel.Warning] = "WARNING";
            _levelStrings[(int)LogLevel.Error] = "ERROR";
            _levelStrings[(int)LogLevel.Fatal] = "FATAL";
            _levelStrings[(int)LogLevel.None] = "NONE"; // Should ideally not be logged
        }

        /// <summary>
        /// Gets the uppercase string representation of the specified log level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Get(LogLevel level)
        {
            // Assumes level is a valid enum member.
            // Add bounds checking if necessary, though enums should be type-safe.
            return _levelStrings[(int)level];
        }
    }
}