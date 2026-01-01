using System.Diagnostics;
using System.Runtime.CompilerServices;
using System.Text;
using CycloneGames.Logger;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Centralized logging utility for the GameplayAbilities system.
    /// Uses the [GAS] category tag for easy filtering in the CLogger system.
    /// All logging methods use conditional compilation to eliminate overhead in Release.
    /// </summary>
    public static class GASLog
    {
        private const string GAS_CATEGORY = "GAS";
        
        /// <summary>
        /// Logs a trace message (most verbose). Compiled out in Release.
        /// </summary>
        [Conditional("DEBUG"), Conditional("ENABLE_GAS_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogTrace(message, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Logs a debug message. Compiled out in Release.
        /// </summary>
        [Conditional("DEBUG"), Conditional("ENABLE_GAS_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogDebug(message, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Logs an info message. Compiled out in Release.
        /// </summary>
        [Conditional("DEBUG"), Conditional("ENABLE_GAS_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogInfo(message, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Logs a warning (always active, includes Release).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Warning(string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogWarning(message, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Logs an error (always active, includes Release).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Error(string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogError(message, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Logs a fatal error (always active, includes Release).
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Fatal(string message, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogFatal(message, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        #region Zero-GC StringBuilder Overloads
        
        /// <summary>
        /// Zero-GC trace logging using StringBuilder builder pattern.
        /// </summary>
        [Conditional("DEBUG"), Conditional("ENABLE_GAS_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Trace(System.Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogTrace(messageBuilder, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Zero-GC debug logging using StringBuilder builder pattern.
        /// </summary>
        [Conditional("DEBUG"), Conditional("ENABLE_GAS_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Debug(System.Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogDebug(messageBuilder, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        /// <summary>
        /// Zero-GC info logging using StringBuilder builder pattern.
        /// </summary>
        [Conditional("DEBUG"), Conditional("ENABLE_GAS_LOGGING")]
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void Info(System.Action<StringBuilder> messageBuilder, [CallerFilePath] string filePath = "", [CallerLineNumber] int lineNumber = 0, [CallerMemberName] string memberName = "")
        {
            CLogger.LogInfo(messageBuilder, GAS_CATEGORY, filePath, lineNumber, memberName);
        }
        
        #endregion
    }
}
