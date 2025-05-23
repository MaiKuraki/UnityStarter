using System;
using System.IO;
using System.Runtime.CompilerServices;
using System.Diagnostics; // For StackTraceHidden
using System.Text;
using CycloneGames.Logger; // For StringBuilderPool

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to the standard console output and error streams.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        private static readonly object _consoleLock = new();

        public void LogTrace(in LogMessage logMessage) => LogInternal("TRACE", logMessage, Console.Out);
        public void LogDebug(in LogMessage logMessage) => LogInternal("DEBUG", logMessage, Console.Out);
        public void LogInfo(in LogMessage logMessage) => LogInternal("INFO", logMessage, Console.Out);
        public void LogWarning(in LogMessage logMessage) => LogInternal("WARNING", logMessage, Console.Out); // Warnings can go to Console.Out or Console.Error depending on preference.
        public void LogError(in LogMessage logMessage) => LogInternal("ERROR", logMessage, Console.Error);
        public void LogFatal(in LogMessage logMessage) => LogInternal("FATAL", logMessage, Console.Error);

        private static void LogInternal(string levelString, in LogMessage logMessage, TextWriter writer)
        {
            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                sb.Append(levelString); // Using pre-supplied level string. Could also use LogLevelStrings.Get(logMessage.Level)
                sb.Append(": ");
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append("[");
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }
                sb.Append(logMessage.OriginalMessage);
                // FilePath and LineNumber are typically not logged to console by default, but could be added.
                // if (!string.IsNullOrEmpty(logMessage.FilePath))
                // {
                //    sb.Append($" (at {Path.GetFileName(logMessage.FilePath)}:{logMessage.LineNumber})");
                // }

                lock (_consoleLock)
                {
                    writer.WriteLine(sb.ToString());
                }
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        public void Dispose() { /* No unmanaged resources to dispose for ConsoleLogger. */ }
    }
}