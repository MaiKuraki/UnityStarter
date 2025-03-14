using System;

namespace CycloneGames.Logger
{
    public class ConsoleLogger : ILogger
    {
        public void LogInfo(string message)
        {
            Console.WriteLine($"INFO: {message}");
        }

        public void LogWarning(string message)
        {
            Console.WriteLine($"WARNING: {message}");
        }

        public void LogError(string message)
        {
            Console.Error.WriteLine($"ERROR: {message}");
        }

        public void LogTrace(string message)
        {
            Console.WriteLine($"TRACE: {message}");
        }

        public void LogDebug(string message)
        {
            Console.WriteLine($"DEBUG: {message}");
        }

        public void LogFatal(string message)
        {
            Console.Error.WriteLine($"FATAL: {message}");
        }
    }
}