using System;
using System.Runtime.CompilerServices;
using System.Text;

namespace CycloneGames.Logger
{
    public interface ICLogger : IDisposable
    {
        LogLevel GetLogLevel();
        void SetLogLevel(LogLevel level);
        void SetLogFilter(LogFilter filter);
        void AddToWhiteList(string category);
        void RemoveFromWhiteList(string category);
        void AddToBlackList(string category);
        void RemoveFromBlackList(string category);
        bool AddLogger(ILogger logger);
        bool AddLoggerUnique(ILogger logger);
        bool RemoveLogger(ILogger logger, int quiescenceTimeoutMs = 1000);
        void ClearLoggers();
        void Pump(int maxItems = 256);
        bool TryFlush(LogFlushMode mode = LogFlushMode.Buffered, int timeoutMs = -1);
        LogProcessingStatistics GetProcessingStatistics();
        LoggerShutdownResult ShutdownInstance(LogFlushMode flushMode = LogFlushMode.Buffered, int timeoutMs = -1);

        void Log(LogLevel level, string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "");
        void Log(LogLevel level, Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "");
        void Log<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "");
    }
}
