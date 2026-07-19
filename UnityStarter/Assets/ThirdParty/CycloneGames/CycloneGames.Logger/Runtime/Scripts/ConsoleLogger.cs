using System;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Synchronous stdout/stderr sink suitable for CLI and dedicated server processes.
    /// Output is serialized to prevent record interleaving.
    /// </summary>
    public sealed class ConsoleLogger : ILogger, IFlushableLogger, IIdempotentLoggerSinkDisposal
    {
        private static readonly object ConsoleLock = new object();
        private static readonly char[] CharacterBuffer = new char[1024];

        private readonly LogSourcePathMode _sourcePathMode;

        public ConsoleLogger(LogSourcePathMode sourcePathMode = LogSourcePathMode.FileName)
        {
            if (sourcePathMode < LogSourcePathMode.FileName || sourcePathMode > LogSourcePathMode.FullPath)
            {
                throw new ArgumentOutOfRangeException(nameof(sourcePathMode));
            }

            _sourcePathMode = sourcePathMode;
        }

        public void Log(LogMessage logMessage)
        {
            if (logMessage == null)
            {
                throw new ArgumentNullException(nameof(logMessage));
            }

            TextWriter writer = logMessage.Level >= LogLevel.Error ? Console.Error : Console.Out;
            StringBuilder builder = StringBuilderPool.Get();
            try
            {
                builder.Append(LogLevelStrings.Get(logMessage.Level));
                builder.Append(": ");
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    builder.Append('[');
                    AppendEscaped(builder, logMessage.Category);
                    builder.Append("] ");
                }

                logMessage.AppendMessageTo(builder, true);
                AppendSourceLocation(builder, logMessage.FilePath, logMessage.LineNumber, _sourcePathMode);

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
            try
            {
                lock (ConsoleLock)
                {
                    Console.Out.Flush();
                    Console.Error.Flush();
                }

                return true;
            }
            catch
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
