using System;

namespace CycloneGames.Logger
{
    public class ConsoleLogger : ILogger
    {
        private static readonly object _consoleLock = new object();

        public void LogInfo(string message)
        {
            lock (_consoleLock) Console.WriteLine($"INFO: {message}");
        }

        public void LogWarning(string message)
        {
            lock (_consoleLock) Console.WriteLine($"WARNING: {message}");
        }

        public void LogError(string message)
        {
            lock (_consoleLock) Console.Error.WriteLine($"ERROR: {message}");
        }

        public void LogTrace(string message)
        {
            lock (_consoleLock) Console.WriteLine($"TRACE: {message}");
        }

        public void LogDebug(string message)
        {
            lock (_consoleLock) Console.WriteLine($"DEBUG: {message}");
        }

        public void LogFatal(string message)
        {
            lock (_consoleLock) Console.Error.WriteLine($"FATAL: {message}");
        }

        public void Dispose()
        {
            
        }
    }
}