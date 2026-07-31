using System;
using System.Runtime.CompilerServices;
using System.Text;
using CycloneGames.Logging;

namespace CycloneGames.Logger.Tests.Editor
{
    internal static class LogWriterTestExtensions
    {
        internal static void Write(
            this CLogger logger,
            LogSeverity severity,
            string message,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ((ILogWriter)logger).Write(severity, category, message, filePath, lineNumber, memberName);
        }

        internal static void Write(
            this CLogger logger,
            LogSeverity severity,
            Action<StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ((ILogWriter)logger).Write(severity, category, messageBuilder, filePath, lineNumber, memberName);
        }

        internal static void Write<TState>(
            this CLogger logger,
            LogSeverity severity,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ((ILogWriter)logger).Write(severity, category, state, messageBuilder, filePath, lineNumber, memberName);
        }
    }
}
