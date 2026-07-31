using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Category-bound logging entry point. A channel can use an explicitly supplied writer or
    /// resolve the process fallback on every call so backend replacement is observed atomically.
    /// </summary>
    public readonly struct LogChannel
    {
        private readonly ILogWriter _writer;

        public string Category { get; }

        private LogChannel(string category, ILogWriter writer)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A logging category is required.", nameof(category));
            }

            Category = category;
            _writer = writer;
        }

        public static LogChannel Create(string category)
        {
            return new LogChannel(category, null);
        }

        public static LogChannel Create(string category, ILogWriter writer)
        {
            return new LogChannel(category, writer ?? throw new ArgumentNullException(nameof(writer)));
        }

        public bool IsEnabled(LogSeverity severity)
        {
            return ResolveWriter().IsEnabled(severity, Category);
        }

        public void Write(
            LogSeverity severity,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ResolveWriter().Write(severity, Category, message, filePath, lineNumber, memberName);
        }

        public void Write(
            LogSeverity severity,
            Action<StringBuilder> messageBuilder,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ResolveWriter().Write(severity, Category, messageBuilder, filePath, lineNumber, memberName);
        }

        public void Write<TState>(
            LogSeverity severity,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ResolveWriter().Write(severity, Category, state, messageBuilder, filePath, lineNumber, memberName);
        }

        public void WriteException(
            LogSeverity severity,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ResolveWriter().WriteException(severity, Category, exception, message, filePath, lineNumber, memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private ILogWriter ResolveWriter()
        {
            return _writer ?? LogRuntime.Writer;
        }
    }
}
