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
        public void Log(LogMessage logMessage)
        {
            StringBuilder sb = StringBuilderPool.Get();
            string unityMessage;
            try
            {
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append('[');
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
                // ToString() allocation is unavoidable here — Debug.LogFormat requires a string argument.
                unityMessage = sb.ToString();
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }

            LoggerUpdater.EnqueueUnityLog(logMessage.Level, unityMessage);
        }

        public void Dispose() { }
    }
}