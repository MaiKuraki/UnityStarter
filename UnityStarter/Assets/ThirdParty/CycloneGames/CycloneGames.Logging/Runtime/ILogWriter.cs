using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Producer-only logging contract. Implementations own admission and delivery, but callers
    /// never own or dispose the writer through this interface.
    /// </summary>
    public interface ILogWriter
    {
        bool IsEnabled(LogSeverity severity, string category);

        void Write(
            LogSeverity severity,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");

        void Write(
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");

        void Write<TState>(
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");

        void WriteException(
            LogSeverity severity,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");
    }
}
