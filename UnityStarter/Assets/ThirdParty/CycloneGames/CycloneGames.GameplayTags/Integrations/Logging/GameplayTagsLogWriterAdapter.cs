using System;
using CycloneGames.GameplayTags.Core;
using CycloneGames.Logging;

namespace CycloneGames.GameplayTags
{
    /// <summary>
    /// Optional bridge from the engine-independent GameplayTags diagnostic port to CycloneGames.Logging.
    /// </summary>
    public sealed class GameplayTagsLogWriterAdapter : IGameplayTagsDiagnostics
    {
        public static readonly GameplayTagsLogWriterAdapter Ambient = new GameplayTagsLogWriterAdapter();

        private readonly ILogWriter _writer;

        public GameplayTagsLogWriterAdapter()
        {
        }

        public GameplayTagsLogWriterAdapter(ILogWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public bool IsEnabled(GameplayTagsDiagnosticLevel level, string category)
        {
            if (!TryMap(level, out LogSeverity severity))
            {
                return false;
            }

            return LogWriterGuard.IsEnabled(ResolveWriter(), severity, category);
        }

        public void Write(
            GameplayTagsDiagnosticLevel level,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (!TryMap(level, out LogSeverity severity))
            {
                return;
            }

            LogWriterGuard.TryWrite(
                ResolveWriter(),
                severity,
                category,
                message,
                filePath,
                lineNumber,
                memberName);
        }

        public void WriteException(
            GameplayTagsDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (!TryMap(level, out LogSeverity severity))
            {
                return;
            }

            LogWriterGuard.TryWriteException(
                ResolveWriter(),
                severity,
                category,
                exception,
                message,
                filePath,
                lineNumber,
                memberName);
        }

        private ILogWriter ResolveWriter() => _writer ?? LogRuntime.Writer;

        private static bool TryMap(GameplayTagsDiagnosticLevel level, out LogSeverity severity)
        {
            switch (level)
            {
                case GameplayTagsDiagnosticLevel.Trace:
                    severity = LogSeverity.Trace;
                    return true;
                case GameplayTagsDiagnosticLevel.Debug:
                    severity = LogSeverity.Debug;
                    return true;
                case GameplayTagsDiagnosticLevel.Info:
                    severity = LogSeverity.Info;
                    return true;
                case GameplayTagsDiagnosticLevel.Warning:
                    severity = LogSeverity.Warning;
                    return true;
                case GameplayTagsDiagnosticLevel.Error:
                    severity = LogSeverity.Error;
                    return true;
                case GameplayTagsDiagnosticLevel.Fatal:
                    severity = LogSeverity.Fatal;
                    return true;
                case GameplayTagsDiagnosticLevel.None:
                default:
                    severity = LogSeverity.None;
                    return false;
            }
        }
    }
}
