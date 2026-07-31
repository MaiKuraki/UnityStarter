using System;

namespace CycloneGames.Choreography.Core
{
    public sealed class NullChoreographyDiagnostics : IChoreographyDiagnostics
    {
        public static readonly NullChoreographyDiagnostics Instance = new NullChoreographyDiagnostics();

        private NullChoreographyDiagnostics()
        {
        }

        public bool IsEnabled(ChoreographyDiagnosticLevel level, string category) => false;

        public void Write(
            ChoreographyDiagnosticLevel level,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void WriteException(
            ChoreographyDiagnosticLevel level,
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
