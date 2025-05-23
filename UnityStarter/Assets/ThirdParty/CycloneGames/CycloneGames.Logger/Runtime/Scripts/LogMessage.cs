using System;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Defines the severity levels for log messages.
    /// </summary>
    public enum LogLevel : byte
    {
        Trace,   // Detailed diagnostic information.
        Debug,   // Information useful for debugging.
        Info,    // General operational information.
        Warning, // Indicates a potential issue.
        Error,   // Indicates a recoverable error.
        Fatal,   // Indicates a critical, non-recoverable error.
        None     // Special level to disable logging.
    }

    /// <summary>
    /// Defines filter modes for categorized logging.
    /// </summary>
    public enum LogFilter : byte
    {
        LogAll,         // All categories are logged.
        LogWhiteList,   // Only categories in the whitelist are logged.
        LogNoBlackList  // Categories in the blacklist are not logged.
    }

    /// <summary>
    /// Represents a single log entry. Designed to be a struct for performance.
    /// Passed by 'in' parameter to minimize copying.
    /// </summary>
    public readonly struct LogMessage
    {
        public readonly DateTime Timestamp;
        public readonly LogLevel Level;
        public readonly string OriginalMessage; // Content of the log.
        public readonly string Category;        // Optional category for filtering.
        public readonly string FilePath;        // Source file of the log call.
        public readonly int LineNumber;         // Source line number.
        public readonly string MemberName;      // Source member name.

        public LogMessage(DateTime timestamp, LogLevel level, string originalMessage, string category, string filePath, int lineNumber, string memberName)
        {
            Timestamp = timestamp;
            Level = level;
            OriginalMessage = originalMessage ?? string.Empty; // Ensure not null
            Category = category; // Allow null category
            FilePath = filePath;
            LineNumber = lineNumber;
            MemberName = memberName;
        }
    }
}