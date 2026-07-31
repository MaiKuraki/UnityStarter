using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DataTable
{
    public enum DataTableDiagnosticLevel : byte
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
    /// Engine-independent diagnostic port owned by DataTable Core. Implementations should not throw;
    /// Core nevertheless isolates ordinary sink failures at every call site.
    /// </summary>
    public interface IDataTableDiagnostics
    {
        bool IsEnabled(DataTableDiagnosticLevel level, string category);

        void Write(
            DataTableDiagnosticLevel level,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");

        void WriteException(
            DataTableDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");
    }
}
