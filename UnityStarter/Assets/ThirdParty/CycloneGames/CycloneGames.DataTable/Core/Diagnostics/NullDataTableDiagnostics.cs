using System;

namespace CycloneGames.DataTable
{
    public sealed class NullDataTableDiagnostics : IDataTableDiagnostics
    {
        public static readonly NullDataTableDiagnostics Instance = new NullDataTableDiagnostics();

        private NullDataTableDiagnostics()
        {
        }

        public bool IsEnabled(DataTableDiagnosticLevel level, string category) => false;

        public void Write(
            DataTableDiagnosticLevel level,
            string category,
            string message,
            string filePath = "",
            int lineNumber = 0,
            string memberName = "")
        {
        }

        public void WriteException(
            DataTableDiagnosticLevel level,
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
