using System;
using System.Text;

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
    /// Represents a single log entry. This is a class to enable object pooling,
    /// which is crucial for minimizing GC allocation in high-frequency logging scenarios.
    /// </summary>
    public sealed class LogMessage
    {
        public DateTime Timestamp { get; private set; }
        public LogLevel Level { get; private set; }
        public string OriginalMessage { get; private set; }
        public StringBuilder MessageBuilder { get; private set; }
        public string Category { get; private set; }
        public string FilePath { get; private set; }
        public int LineNumber { get; private set; }
        public string MemberName { get; private set; }

        // Internal parameterless constructor for object pool creation.
        internal LogMessage() { }

        /// <summary>
        /// Initializes a LogMessage instance with data. Called when an object is retrieved from the pool.
        /// </summary>
        internal void Initialize(DateTime timestamp, LogLevel level, string originalMessage, StringBuilder messageBuilder, string category, string filePath, int lineNumber, string memberName)
        {
            Timestamp = timestamp;
            Level = level;
            OriginalMessage = originalMessage;
            MessageBuilder = messageBuilder;
            Category = category;
            FilePath = filePath;
            LineNumber = lineNumber;
            MemberName = memberName;
        }

        /// <summary>
        /// Resets the object's state. Called before returning it to the pool.
        /// </summary>
        internal void Reset()
        {
            if (MessageBuilder != null)
            {
                CycloneGames.Logger.Util.StringBuilderPool.Return(MessageBuilder);
                MessageBuilder = null;
            }

            // Reset reference types to null to release references and allow GC if necessary.
            OriginalMessage = null;
            Category = null;
            FilePath = null;
            MemberName = null;

            // Reset value types to default.
            Timestamp = default;
            Level = default;
            LineNumber = 0;
        }
    }
}