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

                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    string sourcePath = logMessage.FilePath;
                    int assetsIndex = sourcePath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                    if (assetsIndex < 0)
                    {
                        assetsIndex = sourcePath.IndexOf("\\Assets\\", StringComparison.OrdinalIgnoreCase);
                    }
                    int startIndex = assetsIndex >= 0 ? assetsIndex + 1 : 0;

#if UNITY_EDITOR
                    // Editor: use clickable hyperlink with custom attributes
                    // Extra newline hides the hyperlink from Console single-line preview
                    sb.Append("\n\n<a path=\"");
                    for (int i = startIndex; i < sourcePath.Length; i++)
                    {
                        sb.Append(sourcePath[i] == '\\' ? '/' : sourcePath[i]);
                    }
                    sb.Append("\" line=\"");
                    sb.Append(logMessage.LineNumber);
                    sb.Append("\">(at ");
                    for (int i = startIndex; i < sourcePath.Length; i++)
                    {
                        sb.Append(sourcePath[i] == '\\' ? '/' : sourcePath[i]);
                    }
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(")</a>");
#else
                    // Runtime: plain text without rich text tags
                    sb.Append("\n(at ");
                    for (int i = startIndex; i < sourcePath.Length; i++)
                    {
                        sb.Append(sourcePath[i] == '\\' ? '/' : sourcePath[i]);
                    }
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(')');
#endif
                }
                unityMessage = sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }

            switch (logMessage.Level)
            {
                case LogLevel.Trace:
                case LogLevel.Debug:
                case LogLevel.Info:
                    LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage, logMessage.FilePath, logMessage.LineNumber);
                    break;
                case LogLevel.Warning:
                    LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage, logMessage.FilePath, logMessage.LineNumber);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal:
                    LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage, logMessage.FilePath, logMessage.LineNumber);
                    break;
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