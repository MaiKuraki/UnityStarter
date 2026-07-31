using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayTags.Core
{
    public enum GameplayTagsDiagnosticLevel : byte
    {
        Trace = 0,
        Debug = 1,
        Info = 2,
        Warning = 3,
        Error = 4,
        Fatal = 5,
        None = 6
    }

    /// <summary>
    /// Engine-independent diagnostic port owned by GameplayTags Core. Implementations should not throw;
    /// Core nevertheless isolates ordinary sink failures at every call site.
    /// </summary>
    public interface IGameplayTagsDiagnostics
    {
        bool IsEnabled(GameplayTagsDiagnosticLevel level, string category);

        void Write(
            GameplayTagsDiagnosticLevel level,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");

        void WriteException(
            GameplayTagsDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");
    }
}
