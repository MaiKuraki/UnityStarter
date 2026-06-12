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
        void AddLogger(ILogger logger);
        void AddLoggerUnique(ILogger logger);
        void RemoveLogger(ILogger logger);
        void ClearLoggers();
        void Pump(int maxItems = 256);
        LogProcessingStatistics GetProcessingStatistics();

        void Log(LogLevel level, string message, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "");
        void Log(LogLevel level, Action<StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "");
        void Log<T>(LogLevel level, T state, Action<T, StringBuilder> messageBuilder, string category = null, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "");
    }
}
