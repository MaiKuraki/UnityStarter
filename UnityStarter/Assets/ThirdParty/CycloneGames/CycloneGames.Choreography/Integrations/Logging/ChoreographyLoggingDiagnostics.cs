using System;
using CycloneGames.Choreography.Core;
using CycloneGames.Logging;

namespace CycloneGames.Choreography
{
    /// <summary>
    /// Optional bridge from the engine-independent Choreography diagnostic port to CycloneGames.Logging.
    /// </summary>
    public sealed class ChoreographyLoggingDiagnostics : IChoreographyDiagnostics
    {
        public static readonly ChoreographyLoggingDiagnostics Ambient = new ChoreographyLoggingDiagnostics();

        private readonly ILogWriter _writer;

        public ChoreographyLoggingDiagnostics()
        {
        }

        public ChoreographyLoggingDiagnostics(ILogWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public bool IsEnabled(ChoreographyDiagnosticLevel level, string category)
        {
            if (!TryMap(level, out LogSeverity severity))
            {
                return false;
            }

            return LogWriterGuard.IsEnabled(ResolveWriter(), severity, category);
        }

        public void Write(
            ChoreographyDiagnosticLevel level,
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
            ChoreographyDiagnosticLevel level,
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

        private static bool TryMap(ChoreographyDiagnosticLevel level, out LogSeverity severity)
        {
            switch (level)
            {
                case ChoreographyDiagnosticLevel.Trace:
                    severity = LogSeverity.Trace;
                    return true;
                case ChoreographyDiagnosticLevel.Debug:
                    severity = LogSeverity.Debug;
                    return true;
                case ChoreographyDiagnosticLevel.Info:
                    severity = LogSeverity.Info;
                    return true;
                case ChoreographyDiagnosticLevel.Warning:
                    severity = LogSeverity.Warning;
                    return true;
                case ChoreographyDiagnosticLevel.Error:
                    severity = LogSeverity.Error;
                    return true;
                case ChoreographyDiagnosticLevel.Fatal:
                    severity = LogSeverity.Fatal;
                    return true;
                default:
                    severity = LogSeverity.None;
                    return false;
            }
        }
    }
}
