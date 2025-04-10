using System;
using System.IO;
using System.Runtime.CompilerServices;

namespace CycloneGames.Logger
{
    public sealed class ConsoleLogger : ILogger
    {
        private static readonly object _consoleLock = new();

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogTrace(in string message) => LogInternal("TRACE", message, Console.Out);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogDebug(in string message) => LogInternal("DEBUG", message, Console.Out);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogInfo(in string message) => LogInternal("INFO", message, Console.Out);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogWarning(in string message) => LogInternal("WARNING", message, Console.Out);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogError(in string message) => LogInternal("ERROR", message, Console.Error);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void LogFatal(in string message) => LogInternal("FATAL", message, Console.Error);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void LogInternal(in string level, in string message, TextWriter writer)
        {
            lock (_consoleLock)
            {
                writer.Write(level);
                writer.Write(": ");
                writer.WriteLine(message);
            }
        }

        public void Dispose() { }
    }
}