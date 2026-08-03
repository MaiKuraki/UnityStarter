using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Uniform severity-specific entry points for every CycloneGames package.
    /// </summary>
    public static class LogChannelExtensions
    {
        public static void Trace(this LogChannel channel, string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Trace, message, filePath, lineNumber, memberName);
        public static void Debug(this LogChannel channel, string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Debug, message, filePath, lineNumber, memberName);
        public static void Info(this LogChannel channel, string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Info, message, filePath, lineNumber, memberName);
        public static void Warning(this LogChannel channel, string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Warning, message, filePath, lineNumber, memberName);
        public static void Error(this LogChannel channel, string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Error, message, filePath, lineNumber, memberName);
        public static void Fatal(this LogChannel channel, string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Fatal, message, filePath, lineNumber, memberName);

        public static void Trace(this LogChannel channel, Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Trace, messageBuilder, filePath, lineNumber, memberName);
        public static void Debug(this LogChannel channel, Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Debug, messageBuilder, filePath, lineNumber, memberName);
        public static void Info(this LogChannel channel, Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Info, messageBuilder, filePath, lineNumber, memberName);
        public static void Warning(this LogChannel channel, Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Warning, messageBuilder, filePath, lineNumber, memberName);
        public static void Error(this LogChannel channel, Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Error, messageBuilder, filePath, lineNumber, memberName);
        public static void Fatal(this LogChannel channel, Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Fatal, messageBuilder, filePath, lineNumber, memberName);

        public static void Trace<TState>(this LogChannel channel, TState state, Action<TState, StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Trace, state, messageBuilder, filePath, lineNumber, memberName);
        public static void Debug<TState>(this LogChannel channel, TState state, Action<TState, StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Debug, state, messageBuilder, filePath, lineNumber, memberName);
        public static void Info<TState>(this LogChannel channel, TState state, Action<TState, StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Info, state, messageBuilder, filePath, lineNumber, memberName);
        public static void Warning<TState>(this LogChannel channel, TState state, Action<TState, StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Warning, state, messageBuilder, filePath, lineNumber, memberName);
        public static void Error<TState>(this LogChannel channel, TState state, Action<TState, StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Error, state, messageBuilder, filePath, lineNumber, memberName);
        public static void Fatal<TState>(this LogChannel channel, TState state, Action<TState, StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.Write(LogSeverity.Fatal, state, messageBuilder, filePath, lineNumber, memberName);

        public static void Trace(this LogChannel channel, Exception exception, string message = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.WriteException(LogSeverity.Trace, exception, message, filePath, lineNumber, memberName);
        public static void Debug(this LogChannel channel, Exception exception, string message = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.WriteException(LogSeverity.Debug, exception, message, filePath, lineNumber, memberName);
        public static void Info(this LogChannel channel, Exception exception, string message = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.WriteException(LogSeverity.Info, exception, message, filePath, lineNumber, memberName);
        public static void Warning(this LogChannel channel, Exception exception, string message = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.WriteException(LogSeverity.Warning, exception, message, filePath, lineNumber, memberName);
        public static void Error(this LogChannel channel, Exception exception, string message = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.WriteException(LogSeverity.Error, exception, message, filePath, lineNumber, memberName);
        public static void Fatal(this LogChannel channel, Exception exception, string message = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "") => channel.WriteException(LogSeverity.Fatal, exception, message, filePath, lineNumber, memberName);
    }
}
