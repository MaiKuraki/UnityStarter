using System;
using CycloneGames.Logging;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Optional bridge from the engine-independent Networking diagnostic port to CycloneGames.Logging.
    /// </summary>
    public sealed class NetworkingLoggingDiagnostics : INetworkingDiagnostics
    {
        public static readonly NetworkingLoggingDiagnostics Ambient = new NetworkingLoggingDiagnostics();

        private readonly ILogWriter _writer;

        public NetworkingLoggingDiagnostics()
        {
        }

        public NetworkingLoggingDiagnostics(ILogWriter writer)
        {
            _writer = writer ?? throw new ArgumentNullException(nameof(writer));
        }

        public bool IsEnabled(NetworkingDiagnosticLevel level, string category)
        {
            if (!TryMap(level, out LogSeverity severity))
            {
                return false;
            }

            return LogWriterGuard.IsEnabled(ResolveWriter(), severity, category);
        }

        public void Write(
            NetworkingDiagnosticLevel level,
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
            NetworkingDiagnosticLevel level,
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

        private static bool TryMap(NetworkingDiagnosticLevel level, out LogSeverity severity)
        {
            switch (level)
            {
                case NetworkingDiagnosticLevel.Trace:
                    severity = LogSeverity.Trace;
                    return true;
                case NetworkingDiagnosticLevel.Debug:
                    severity = LogSeverity.Debug;
                    return true;
                case NetworkingDiagnosticLevel.Info:
                    severity = LogSeverity.Info;
                    return true;
                case NetworkingDiagnosticLevel.Warning:
                    severity = LogSeverity.Warning;
                    return true;
                case NetworkingDiagnosticLevel.Error:
                    severity = LogSeverity.Error;
                    return true;
                case NetworkingDiagnosticLevel.Fatal:
                    severity = LogSeverity.Fatal;
                    return true;
                default:
                    severity = LogSeverity.None;
                    return false;
            }
        }
    }
}
