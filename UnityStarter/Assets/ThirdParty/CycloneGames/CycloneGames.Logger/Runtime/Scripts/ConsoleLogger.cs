using System;
using System.IO;
using System.Text;
using CycloneGames.Logger.Util;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Logs messages to standard console output and error streams.
    /// Zero-alloc output path: formats into a pooled StringBuilder, then writes
    /// through a shared char buffer under a static lock to avoid both interleaving and ToString() allocation.
    /// </summary>
    public sealed class ConsoleLogger : ILogger
    {
        private static readonly object _consoleLock = new();
        private static readonly char[] _charBuffer = new char[1024];

        public void Log(LogMessage logMessage)
        {
            TextWriter writer = logMessage.Level >= LogLevel.Error ? Console.Error : Console.Out;

            StringBuilder sb = StringBuilderPool.Get();
            try
            {
                sb.Append(LogLevelStrings.Get(logMessage.Level));
                sb.Append(": ");
                if (!string.IsNullOrEmpty(logMessage.Category))
                {
                    sb.Append('[');
                    sb.Append(logMessage.Category);
                    sb.Append("] ");
                }

                if (logMessage.MessageBuilder != null)
                {
                    var mb = logMessage.MessageBuilder;
                    for (int i = 0; i < mb.Length; i++)
                    {
                        sb.Append(mb[i]);
                    }
                }
                else if (logMessage.OriginalMessage != null)
                {
                    sb.Append(logMessage.OriginalMessage);
                }

                if (!string.IsNullOrEmpty(logMessage.FilePath))
                {
                    sb.Append(" (at ");
                    string src = logMessage.FilePath;
                    for (int i = 0; i < src.Length; i++)
                    {
                        char c = src[i];
                        sb.Append(c == '\\' ? '/' : c);
                    }
                    sb.Append(':');
                    sb.Append(logMessage.LineNumber);
                    sb.Append(')');
                }

                lock (_consoleLock)
                {
                    int length = sb.Length;
                    int offset = 0;
                    while (offset < length)
                    {
                        int count = Math.Min(_charBuffer.Length, length - offset);
                        sb.CopyTo(offset, _charBuffer, 0, count);
                        writer.Write(_charBuffer, 0, count);
                        offset += count;
                    }
                    writer.WriteLine();
                }
            }
            finally
            {
                StringBuilderPool.Return(sb);
            }
        }

        public void Dispose() { }
    }
}