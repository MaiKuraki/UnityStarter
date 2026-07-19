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

}
