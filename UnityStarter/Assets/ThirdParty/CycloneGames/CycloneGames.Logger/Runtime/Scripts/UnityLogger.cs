using System;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to the Unity Editor Console.
    /// Includes file path and line number for click-to-source functionality.
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
                sb.Append(logMessage.OriginalMessage);

                // Append file path and line number for Unity's jump-to-source.
                // Using Path.GetFileName can make the console output cleaner if paths are long.
                // However, Unity might require the full path for robust click-to-source.
                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    // To make the file path clickable in the Unity Console, it must be relative to the project root (e.g., "Assets/MyFolder/MyScript.cs").
                    // The [CallerFilePath] attribute provides an absolute path. We need to convert it.
                    // A common and robust way is to find the "Assets" folder in the path and take the substring from there.
                    string filePath = logMessage.FilePath.Replace("\\", "/");
                    int assetsIndex = filePath.IndexOf("/Assets/", StringComparison.OrdinalIgnoreCase);
                    if (assetsIndex > -1)
                    {
                        filePath = filePath.Substring(assetsIndex + 1);
                    }
                    
                    // The newline character is important for matching the format of Unity's native stack traces.
                    sb.Append($"\n(at {filePath}:{logMessage.LineNumber})");
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
                    UnityEngine.Debug.Log(unityMessage);
                    break;
                case LogLevel.Warning:
                    UnityEngine.Debug.LogWarning(unityMessage);
                    break;
                case LogLevel.Error:
                case LogLevel.Fatal: // Fatal errors are typically logged as errors in Unity.
                    UnityEngine.Debug.LogError(unityMessage);
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
