using System;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to the standard console output and error streams.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        private static readonly object _consoleLock = new();

        public void LogTrace(LogMessage logMessage) => LogInternal("TRACE", logMessage, Console.Out);
        public void LogDebug(LogMessage logMessage) => LogInternal("DEBUG", logMessage, Console.Out);
        public void LogInfo(LogMessage logMessage) => LogInternal("INFO", logMessage, Console.Out);
        public void LogWarning(LogMessage logMessage) => LogInternal("WARNING", logMessage, Console.Out);
        public void LogError(LogMessage logMessage) => LogInternal("ERROR", logMessage, Console.Error);
        public void LogFatal(LogMessage logMessage) => LogInternal("FATAL", logMessage, Console.Error);

        private static void LogInternal(string levelString, LogMessage logMessage, TextWriter writer)
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
