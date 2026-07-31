using System;
using System.Text;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Allocation-free default writer used when a host has not installed a logging backend.
    /// </summary>
    public sealed class NullLogWriter : ILogWriter
    {
        public static readonly NullLogWriter Instance = new NullLogWriter();

        private NullLogWriter()
        {
        }

        public bool IsEnabled(LogSeverity severity, string category) => false;

        public void Write(
            LogSeverity severity,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void Write(
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void Write<TState>(
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void WriteException(
            LogSeverity severity,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }
    }
}
