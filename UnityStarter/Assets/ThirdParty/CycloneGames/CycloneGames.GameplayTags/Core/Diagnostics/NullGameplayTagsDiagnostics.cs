using System;

namespace CycloneGames.GameplayTags.Core
{
    public sealed class NullGameplayTagsDiagnostics : IGameplayTagsDiagnostics
    {
        public static readonly NullGameplayTagsDiagnostics Instance = new NullGameplayTagsDiagnostics();

        private NullGameplayTagsDiagnostics()
        {
        }

        public bool IsEnabled(GameplayTagsDiagnosticLevel level, string category) => false;

        public void Write(
            GameplayTagsDiagnosticLevel level,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void WriteException(
            GameplayTagsDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }
    }
}
