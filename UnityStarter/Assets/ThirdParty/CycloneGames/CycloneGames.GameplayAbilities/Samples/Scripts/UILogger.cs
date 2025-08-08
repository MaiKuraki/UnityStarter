using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.Logger;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.GameplayAbilities.Sample
{
    public class UILogger : CycloneGames.Logger.ILogger
    {
        private readonly Text logTextComponent;
        private readonly int maxLogLines;
        private readonly Queue<string> logQueue;
        private readonly StringBuilder stringBuilder = new StringBuilder();
        private readonly Action<string> updateLogAction;

        public UILogger(Action<string> UpdateLog, int maxLines = 7)
        {
            this.updateLogAction = UpdateLog ?? throw new ArgumentNullException(nameof(UpdateLog), "UpdateLog action cannot be null.");
            this.maxLogLines = Mathf.Max(1, maxLines);
            this.logQueue = new Queue<string>(this.maxLogLines);
            if (this.logTextComponent != null)
            {
                this.logTextComponent.text = string.Empty;
            }
        }

        public void Dispose() { }

        // All log levels now call the central AddLog method
        public void LogTrace(LogMessage logMessage) => AddLog(logMessage);
        public void LogDebug(LogMessage logMessage) => AddLog(logMessage);
        public void LogInfo(LogMessage logMessage) => AddLog(logMessage);
        public void LogWarning(LogMessage logMessage) => AddLog(logMessage);
        public void LogError(LogMessage logMessage) => AddLog(logMessage);
        public void LogFatal(LogMessage logMessage) => AddLog(logMessage);

        private void AddLog(LogMessage logMessage)
        {
            // If the queue is full, remove the oldest log
            while (logQueue.Count >= maxLogLines)
            {
                logQueue.Dequeue();
            }

            logQueue.Enqueue($"[{logMessage.Timestamp}] {logMessage.OriginalMessage}");

            // Efficiently build the final string
            stringBuilder.Clear();
            var logLines = logQueue.ToArray();

            for (int i = 0; i < logLines.Length; i++)
            {
                if (i == logLines.Length - 1)
                {
                    stringBuilder.AppendLine($"<color=cyan>{logLines[i]}</color>");
                }
                else
                {
                    stringBuilder.AppendLine(logLines[i]);
                }
            }

            updateLogAction(stringBuilder.ToString());
        }
    }
}
