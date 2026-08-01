using System;
using System.IO;
using System.Text;
using CycloneGames.Logging.Internal;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Synchronous stdout/stderr sink suitable for CLI and dedicated server processes.
    /// Output is serialized to prevent record interleaving.
    /// </summary>
    public sealed class ConsoleLogSink : ILogSink, IFlushableLogSink, IIdempotentLogSinkDisposal
    {
        private static readonly object ConsoleLock = new object();
        private static readonly char[] CharacterBuffer = new char[1024];

        private readonly LogSourcePathMode _sourcePathMode;

        public ConsoleLogSink(LogSourcePathMode sourcePathMode = LogSourcePathMode.FileName)
        {
            if (sourcePathMode < LogSourcePathMode.FileName || sourcePathMode > LogSourcePathMode.FullPath)
            {
                throw new ArgumentOutOfRangeException(nameof(sourcePathMode));
            }

            _sourcePathMode = sourcePathMode;
        }

        public void Emit(LogEvent logEvent)
        {
            if (logEvent == null)
            {
                throw new ArgumentNullException(nameof(logEvent));
            }

            TextWriter writer = logEvent.Severity >= LogSeverity.Error ? Console.Error : Console.Out;
            StringBuilder builder = StringBuilderPool.Get();
            try
            {
                builder.Append(LogSeverityNames.Get(logEvent.Severity));
                builder.Append(": ");
                if (!string.IsNullOrEmpty(logEvent.Category))
                {
                    builder.Append('[');
                    AppendEscaped(builder, logEvent.Category);
                    builder.Append("] ");
                }

                logEvent.AppendMessageTo(builder, true);
                AppendSourceLocation(builder, logEvent.FilePath, logEvent.LineNumber, _sourcePathMode);

                lock (ConsoleLock)
                {
                    WriteBuilder(writer, builder);
                    writer.WriteLine();
                }
            }
            finally
            {
                StringBuilderPool.Return(builder);
            }
        }

        public bool TryFlush(LogFlushMode mode)
        {
            if (mode != LogFlushMode.Buffered && mode != LogFlushMode.Durable)
            {
                throw new ArgumentOutOfRangeException(nameof(mode), "Unknown flush mode.");
            }

            try
            {
                lock (ConsoleLock)
                {
                    Console.Out.Flush();
                    Console.Error.Flush();
                }

                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        public void Dispose()
        {
            TryFlush(LogFlushMode.Buffered);
        }

        private static void WriteBuilder(TextWriter writer, StringBuilder builder)
        {
            int offset = 0;
            while (offset < builder.Length)
            {
                int count = Math.Min(CharacterBuffer.Length, builder.Length - offset);
                builder.CopyTo(offset, CharacterBuffer, 0, count);
                writer.Write(CharacterBuffer, 0, count);
                offset += count;
            }
        }

        private static void AppendSourceLocation(StringBuilder builder, string sourcePath, int lineNumber, LogSourcePathMode sourcePathMode)
        {
            if (string.IsNullOrEmpty(sourcePath) || sourcePathMode == LogSourcePathMode.None)
            {
                return;
            }

            int start = 0;
            if (sourcePathMode == LogSourcePathMode.FileName)
            {
                for (int i = 0; i < sourcePath.Length; i++)
                {
                    char value = sourcePath[i];
                    if (value == '/' || value == '\\')
                    {
                        start = i + 1;
                    }
                }
            }

            builder.Append(" (at ");
            for (int i = start; i < sourcePath.Length; i++)
            {
                char value = sourcePath[i];
                if (char.IsControl(value))
                {
                    builder.Append('_');
                }
                else
                {
                    builder.Append(value == '\\' ? '/' : value);
                }
            }

            builder.Append(':');
            InvariantText.AppendInt32(builder, lineNumber);
            builder.Append(')');
        }

        private static void AppendEscaped(StringBuilder builder, string value)
        {
            for (int i = 0; i < value.Length; i++)
            {
                char character = value[i];
                builder.Append(char.IsControl(character) ? '_' : character);
            }
        }
    }
}
