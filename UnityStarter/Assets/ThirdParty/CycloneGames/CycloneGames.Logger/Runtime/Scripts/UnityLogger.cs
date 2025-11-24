using System;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to the Unity Console.
    /// Includes file path and line number in a format recognized by Unity for click-to-source.
    /// Designed to avoid extra allocations by formatting into a pooled StringBuilder.
    /// </summary>
    public sealed class UnityLogger : ILogger
    {
        private void LogToUnity(LogMessage logMessage)
        {
            StringBuilder sb = StringBuilderPool.Get();
            string unityMessage;
            try
            {
                // Optional: Prepend level string for Trace/Debug if Unity's icons aren't enough.
                // if (logMessage.Level == LogLevel.Trace) sb.Append("[TRACE] ");
                // else if (logMessage.Level == LogLevel.Debug) sb.Append("[DEBUG] ");

                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append("[");
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }

                if (logMessage.MessageBuilder != null)
                {
                    var mb = logMessage.MessageBuilder;
                    for (int i = 0; i < mb.Length; i++)
                    {
                        sb.Append(mb[i]);
                    }
                }
                else if (logMessage.OriginalMessage != null)
                {
                    sb.Append(logMessage.OriginalMessage);
                }

                // Append clickable file path and line number for Unity Console without extra string allocations.
                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    sb.Append('\n');
                    sb.Append("(at ");

                    // Try to make path relative from Assets for better Unity Console click-through.
                    // Use IndexOf (no allocation) and then append characters manually to avoid Substring allocations.
                    string sourcePath = logMessage.FilePath;
                    int assetsIndex = sourcePath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                    int startIndex = assetsIndex >= 0 ? assetsIndex + 1 : 0;
                    for (int i = startIndex; i < sourcePath.Length; i++)
                    {
                        char c = sourcePath[i];
                        sb.Append(c == '\\' ? '/' : c);
                    }

                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(')');
                }
                unityMessage = sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }

            switch (logMessage.Level)
            {
                // Trace and Debug often map to Log to differentiate from Info if needed,
                // or if specific Trace/Debug behavior (like conditional compilation) isn't handled by CLogger.
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage);
                    break;
                case LogLevel.Warning:
                    LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal: // Fatal errors are typically logged as errors in Unity.
                    LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage);
                    break;
                    // LogLevel.None should be filtered by CLogger.ShouldLog and not reach here.
            }
        }

        public void LogTrace(LogMessage logMessage) => LogToUnity(logMessage);
        public void LogDebug(LogMessage logMessage) => LogToUnity(logMessage);
        public void LogInfo(LogMessage logMessage) => LogToUnity(logMessage);
        public void LogWarning(LogMessage logMessage) => LogToUnity(logMessage);
        public void LogError(LogMessage logMessage) => LogToUnity(logMessage);
        public void LogFatal(LogMessage logMessage) => LogToUnity(logMessage);

        public void Dispose() { }
    }
}