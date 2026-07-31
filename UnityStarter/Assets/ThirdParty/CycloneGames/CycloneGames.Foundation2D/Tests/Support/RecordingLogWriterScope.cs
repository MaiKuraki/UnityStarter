using System;
using System.Collections.Generic;
using System.Text;
using System.Threading;
using CycloneGames.Logging;

namespace CycloneGames.Foundation2D.Tests.Support
{
    public readonly struct RecordedLogEntry
    {
        public RecordedLogEntry(
            LogSeverity severity,
            string category,
            string message,
            Exception exception)
        {
            Severity = severity;
            Category = category;
            Message = message;
            Exception = exception;
        }

        public LogSeverity Severity { get; }

        public string Category { get; }

        public string Message { get; }

        public Exception Exception { get; }
    }

    public sealed class RecordingLogWriterScope : ILogWriter, IDisposable
    {
        private readonly object _gate = new object();
        private readonly string _category;
        private readonly List<RecordedLogEntry> _entries = new List<RecordedLogEntry>();
        private readonly ILogWriter _previousWriter;
        private int _disposed;

        public RecordingLogWriterScope()
        {
            _category = null;
            _previousWriter = LogRuntime.ReplaceWriter(this);
        }

        public RecordingLogWriterScope(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A log category is required.", nameof(category));
            }

            _category = category;
            _previousWriter = LogRuntime.ReplaceWriter(this);
        }

        public bool IsEnabled(LogSeverity severity, string category)
        {
            return Volatile.Read(ref _disposed) == 0
                && (_category == null || string.Equals(_category, category, StringComparison.Ordinal));
        }

        public RecordedLogEntry[] Snapshot()
        {
            lock (_gate)
            {
                return _entries.ToArray();
            }
        }

        public void Write(
            LogSeverity severity,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (!IsEnabled(severity, category))
            {
                return;
            }

            Record(severity, category, message ?? string.Empty, null);
        }

        public void Write(
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (!IsEnabled(severity, category))
            {
                return;
            }

            if (messageBuilder == null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            var builder = new StringBuilder();
            messageBuilder(builder);
            Record(severity, category, builder.ToString(), null);
        }

        public void Write<TState>(
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (!IsEnabled(severity, category))
            {
                return;
            }

            if (messageBuilder == null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            var builder = new StringBuilder();
            messageBuilder(state, builder);
            Record(severity, category, builder.ToString(), null);
        }

        public void WriteException(
            LogSeverity severity,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
            if (!IsEnabled(severity, category))
            {
                return;
            }

            Record(severity, category, message ?? string.Empty, exception);
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (!LogRuntime.TryResetWriter(this))
            {
                return;
            }

            if (!ReferenceEquals(_previousWriter, NullLogWriter.Instance))
            {
                LogRuntime.TryInstallWriter(_previousWriter);
            }
        }

        private void Record(
            LogSeverity severity,
            string category,
            string message,
            Exception exception)
        {
            lock (_gate)
            {
                _entries.Add(new RecordedLogEntry(severity, category, message, exception));
            }
        }
    }
}
