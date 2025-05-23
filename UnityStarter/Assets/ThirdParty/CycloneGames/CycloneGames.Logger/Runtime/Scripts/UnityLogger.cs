using UnityEngine; // For UnityEngine.Debug
using System.Text;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to the Unity Editor Console.
    /// Includes file path and line number for click-to-source functionality.
    /// </summary>
    public sealed class UnityLogger : ILogger
    {
        private void LogToUnity(in LogMessage logMessage)
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
                    // Unity typically expects " (at Assets/Path/To/File.cs:LINE)"
                    sb.Append($"\n(at {logMessage.FilePath.Replace("\\", "/")}:{logMessage.LineNumber})");
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

        public void LogTrace(in LogMessage logMessage) => LogToUnity(logMessage);
        public void LogDebug(in LogMessage logMessage) => LogToUnity(logMessage);
        public void LogInfo(in LogMessage logMessage) => LogToUnity(logMessage);
        public void LogWarning(in LogMessage logMessage) => LogToUnity(logMessage);
        public void LogError(in LogMessage logMessage) => LogToUnity(logMessage);
        public void LogFatal(in LogMessage logMessage) => LogToUnity(logMessage);

        public void Dispose() { /* No unmanaged resources specific to UnityLogger. */ }
    }
}