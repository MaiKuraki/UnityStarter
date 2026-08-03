using System;
using System.Runtime.CompilerServices;
using System.Text;
using CycloneGames.Logging;
using CycloneGames.Logging.Pipeline;

namespace CycloneGames.Logging.Pipeline.Tests.Editor
{
    internal static class LogWriterTestExtensions
    {
        internal static void Write(
            this LogPipeline pipeline,
            LogSeverity severity,
            string message,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ((ILogWriter)pipeline).Write(severity, category, message, filePath, lineNumber, memberName);
        }

        internal static void Write(
            this LogPipeline pipeline,
            LogSeverity severity,
            Action<StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ((ILogWriter)pipeline).Write(severity, category, messageBuilder, filePath, lineNumber, memberName);
        }

        internal static void Write<TState>(
            this LogPipeline pipeline,
            LogSeverity severity,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string category = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ((ILogWriter)pipeline).Write(severity, category, state, messageBuilder, filePath, lineNumber, memberName);
        }
    }
}
