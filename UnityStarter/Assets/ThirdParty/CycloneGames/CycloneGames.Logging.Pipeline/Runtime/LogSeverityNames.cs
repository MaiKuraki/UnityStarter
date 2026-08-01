using System.Runtime.CompilerServices;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Provides pre-cached string representations for LogSeverity values.
    /// </summary>
    internal static class LogSeverityNames
    {
        private static readonly string[] _levelStrings = new string[(int)LogSeverity.None + 1];

        static LogSeverityNames()
        {
            _levelStrings[(int)LogSeverity.Trace] = "TRACE";
            _levelStrings[(int)LogSeverity.Debug] = "DEBUG";
            _levelStrings[(int)LogSeverity.Info] = "INFO";
            _levelStrings[(int)LogSeverity.Warning] = "WARNING";
            _levelStrings[(int)LogSeverity.Error] = "ERROR";
            _levelStrings[(int)LogSeverity.Fatal] = "FATAL";
            _levelStrings[(int)LogSeverity.None] = "NONE";
        }

        /// <summary>
        /// Gets the uppercase string representation of the specified log level.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static string Get(LogSeverity level)
        {
            // Assumes level is a valid enum member.
            // Add bounds checking if necessary, though enums should be type-safe.
            return _levelStrings[(int)level];
        }
    }
}