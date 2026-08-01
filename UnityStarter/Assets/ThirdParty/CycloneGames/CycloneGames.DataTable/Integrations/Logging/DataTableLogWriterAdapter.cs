using System;
using CycloneGames.Logging;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Optional bridge from the engine-independent DataTable diagnostic port to CycloneGames.Logging.
    /// </summary>
    public sealed class DataTableLogWriterAdapter : IDataTableDiagnostics
    {
        public static readonly DataTableLogWriterAdapter Ambient = new DataTableLogWriterAdapter();

        private readonly ILogWriter _writer;

        public DataTableLogWriterAdapter()
        {
        }

        public DataTableLogWriterAdapter(ILogWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public bool IsEnabled(DataTableDiagnosticLevel level, string category)
        {
            if (!TryMap(level, out LogSeverity severity))
            {
                return false;
            }

            return LogWriterGuard.IsEnabled(ResolveWriter(), severity, category);
        }

        public void Write(
            DataTableDiagnosticLevel level,
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
            DataTableDiagnosticLevel level,
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

        private static bool TryMap(DataTableDiagnosticLevel level, out LogSeverity severity)
        {
            switch (level)
            {
                case DataTableDiagnosticLevel.Trace:
                    severity = LogSeverity.Trace;
                    return true;
                case DataTableDiagnosticLevel.Debug:
                    severity = LogSeverity.Debug;
                    return true;
                case DataTableDiagnosticLevel.Info:
                    severity = LogSeverity.Info;
                    return true;
                case DataTableDiagnosticLevel.Warning:
                    severity = LogSeverity.Warning;
                    return true;
                case DataTableDiagnosticLevel.Error:
                    severity = LogSeverity.Error;
                    return true;
                case DataTableDiagnosticLevel.Fatal:
                    severity = LogSeverity.Fatal;
                    return true;
                case DataTableDiagnosticLevel.None:
                default:
                    severity = LogSeverity.None;
                    return false;
            }
        }
    }
}
