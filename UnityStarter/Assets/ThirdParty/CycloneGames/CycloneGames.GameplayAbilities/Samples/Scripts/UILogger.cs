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
        private readonly string prefix;
        private readonly int maxLogLines;
        private readonly Queue<string> logQueue;
        private readonly StringBuilder stringBuilder = new StringBuilder(); // Re-use StringBuilder

        public UILogger(Text textComponent, string prefix = "", int maxLines = 2) // Default to 2 lines
        {
            this.logTextComponent = textComponent;
            this.prefix = prefix;
            this.maxLogLines = Mathf.Max(1, maxLines); // Ensure at least 1 line
            this.logQueue = new Queue<string>(this.maxLogLines);
            if (this.logTextComponent != null)
            {
                this.logTextComponent.text = string.Empty;
            }
        }

        public void Dispose() { }

        // All log levels now call the central AddLog method
        public void LogTrace(in LogMessage logMessage) => AddLog(logMessage);
        public void LogDebug(in LogMessage logMessage) => AddLog(logMessage);
        public void LogInfo(in LogMessage logMessage) => AddLog(logMessage);
        public void LogWarning(in LogMessage logMessage) => AddLog(logMessage);
        public void LogError(in LogMessage logMessage) => AddLog(logMessage);
        public void LogFatal(in LogMessage logMessage) => AddLog(logMessage);

        private void AddLog(in LogMessage logMessage)
        {
            if (logTextComponent == null) return;

            // If the queue is full, remove the oldest log
            while (logQueue.Count >= maxLogLines)
            {
                logQueue.Dequeue();
            }

            logQueue.Enqueue($"{prefix}[{logMessage.Level}] {logMessage.OriginalMessage}");

            // Efficiently build the final string
            stringBuilder.Clear();
            foreach (string line in logQueue)
            {
                stringBuilder.AppendLine(line);
            }

            logTextComponent.text = stringBuilder.ToString();

            // Force the layout to rebuild to ensure the UI updates
            if (logTextComponent.transform is RectTransform rectTransform)
            {
                LayoutRebuilder.ForceRebuildLayoutImmediate(rectTransform);
            }
        }
    }
}