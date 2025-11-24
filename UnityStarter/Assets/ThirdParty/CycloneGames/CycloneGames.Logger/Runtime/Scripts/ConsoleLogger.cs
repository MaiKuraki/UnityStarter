using System;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to standard console output and error streams.
    /// Uses a shared lock to avoid interleaved writes across threads.
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
                sb.Append(levelString);
                sb.Append(": ");
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append("[");
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }
                if (logMessage.OriginalMessage != null) sb.Append(logMessage.OriginalMessage);
                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    sb.Append(" (at ");
                    string src = logMessage.FilePath;
                    for (int i = 0; i < src.Length; i++)
                    {
                        char c = src[i];
                        sb.Append(c == '\\' ? '/' : c);
                    }
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(')');
                }

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