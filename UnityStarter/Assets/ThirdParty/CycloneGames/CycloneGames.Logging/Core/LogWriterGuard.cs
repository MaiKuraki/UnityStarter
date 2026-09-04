using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Contains failures raised by a logging backend so diagnostics cannot change producer behavior.
    /// Caller contract violations and <see cref="OutOfMemoryException"/> remain observable.
    /// </summary>
    public static class LogWriterGuard
    {
        public static bool IsEnabled(ILogWriter writer, LogSeverity severity, string category)
        {
            ValidateWriter(writer);
            ValidateCategory(category);

            return IsEmittable(severity) && IsEnabledValidated(writer, severity, category);
        }

        public static bool TryWrite(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ValidateWriter(writer);
            ValidateCategory(category);

            return IsEmittable(severity) &&
                   TryWriteValidated(writer, severity, category, message, filePath, lineNumber, memberName);
        }

        public static bool TryWrite(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ValidateWriter(writer);
            ValidateCategory(category);

            if (messageBuilder == null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            return IsEmittable(severity) &&
                   TryWriteValidated(writer, severity, category, messageBuilder, filePath, lineNumber, memberName);
        }

        public static bool TryWrite<TState>(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ValidateWriter(writer);
            ValidateCategory(category);

            if (messageBuilder == null)
            {
                throw new ArgumentNullException(nameof(messageBuilder));
            }

            return IsEmittable(severity) &&
                   TryWriteValidated(
                       writer,
                       severity,
                       category,
                       state,
                       messageBuilder,
                       filePath,
                       lineNumber,
                       memberName);
        }

        public static bool TryWriteException(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            ValidateWriter(writer);
            ValidateCategory(category);

            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            return IsEmittable(severity) &&
                   TryWriteExceptionValidated(
                       writer,
                       severity,
                       category,
                       exception,
                       message,
                       filePath,
                       lineNumber,
                       memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        /// <summary>
        /// True when the configured level would emit <paramref name="severity"/>. Call this before
        /// formatting a message so a hot path pays for the message only when it will actually be
        /// written; the Write overloads that take a builder defer the same decision.
        /// </summary>
        public static bool IsEmittable(LogSeverity severity)
        {
            return (uint)severity < (uint)LogSeverity.None;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool IsEnabledValidated(ILogWriter writer, LogSeverity severity, string category)
        {
            if (ReferenceEquals(writer, NullLogWriter.Instance))
            {
                return false;
            }

            return IsEnabledProtected(writer, severity, category);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryWriteValidated(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            if (ReferenceEquals(writer, NullLogWriter.Instance))
            {
                return false;
            }

            return TryWriteProtected(
                writer,
                severity,
                category,
                message,
                filePath,
                lineNumber,
                memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryWriteValidated(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath,
            int lineNumber,
            string memberName)
        {
            if (ReferenceEquals(writer, NullLogWriter.Instance))
            {
                return false;
            }

            return TryWriteProtected(
                writer,
                severity,
                category,
                messageBuilder,
                filePath,
                lineNumber,
                memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryWriteValidated<TState>(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath,
            int lineNumber,
            string memberName)
        {
            if (ReferenceEquals(writer, NullLogWriter.Instance))
            {
                return false;
            }

            return TryWriteProtected(
                writer,
                severity,
                category,
                state,
                messageBuilder,
                filePath,
                lineNumber,
                memberName);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static bool TryWriteExceptionValidated(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            Exception exception,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            if (ReferenceEquals(writer, NullLogWriter.Instance))
            {
                return false;
            }

            return TryWriteExceptionProtected(
                writer,
                severity,
                category,
                exception,
                message,
                filePath,
                lineNumber,
                memberName);
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool IsEnabledProtected(ILogWriter writer, LogSeverity severity, string category)
        {
            try
            {
                return writer.IsEnabled(severity, category);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryWriteProtected(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            try
            {
                writer.Write(severity, category, message, filePath, lineNumber, memberName);
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryWriteProtected(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            Action<StringBuilder> messageBuilder,
            string filePath,
            int lineNumber,
            string memberName)
        {
            try
            {
                writer.Write(severity, category, messageBuilder, filePath, lineNumber, memberName);
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryWriteProtected<TState>(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            TState state,
            Action<TState, StringBuilder> messageBuilder,
            string filePath,
            int lineNumber,
            string memberName)
        {
            try
            {
                writer.Write(
                    severity,
                    category,
                    state,
                    messageBuilder,
                    filePath,
                    lineNumber,
                    memberName);
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        [MethodImpl(MethodImplOptions.NoInlining)]
        private static bool TryWriteExceptionProtected(
            ILogWriter writer,
            LogSeverity severity,
            string category,
            Exception exception,
            string message,
            string filePath,
            int lineNumber,
            string memberName)
        {
            try
            {
                writer.WriteException(
                    severity,
                    category,
                    exception,
                    message,
                    filePath,
                    lineNumber,
                    memberName);
                return true;
            }
            catch (Exception writerException) when (!(writerException is OutOfMemoryException))
            {
                return false;
            }
        }

        private static void ValidateWriter(ILogWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }
        }

        private static void ValidateCategory(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A logging category is required.", nameof(category));
            }
        }
    }
}
