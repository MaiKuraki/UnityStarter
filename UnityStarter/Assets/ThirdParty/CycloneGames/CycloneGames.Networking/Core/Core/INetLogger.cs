using System;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Minimal logging interface for Networking.Core.
    /// Abstracts the underlying logging implementation to keep Core engine-agnostic.
    /// </summary>
    public interface INetLogger
    {
        bool IsLogLevelEnabled(LogLevel level);
        void Log(LogLevel level, string message, string category = null);
    }

    public enum LogLevel
    {
        Debug,
        Info,
        Warning,
        Error
    }

    /// <summary>
    /// No-op logger for Core layer when no implementation is provided.
    /// </summary>
    public sealed class NoopNetLogger : INetLogger
    {
        public static readonly NoopNetLogger Instance = new NoopNetLogger();

        public bool IsLogLevelEnabled(LogLevel level) => false;
        public void Log(LogLevel level, string message, string category = null) { }
    }

    /// <summary>
    /// Default implementation that logs to console.
    /// Use UnityNetLogger in production for integration with CycloneGames.Logger.
    /// </summary>
    public sealed class DefaultNetLogger : INetLogger
    {
        private LogLevel _minLevel = LogLevel.Warning;

        public LogLevel MinLevel
        {
            get => _minLevel;
            set => _minLevel = value;
        }

        public bool IsLogLevelEnabled(LogLevel level) => level >= _minLevel;

        public void Log(LogLevel level, string message, string category = null)
        {
            if (!IsLogLevelEnabled(level)) return;

            string prefix = category != null ? $"[{category}] " : "";
            switch (level)
            {
                case LogLevel.Warning:
                case LogLevel.Error:
                    Console.Error.WriteLine($"{prefix}{level}: {message}");
                    break;
                default:
                    Console.Out.WriteLine($"{prefix}{message}");
                    break;
            }
        }
    }
}