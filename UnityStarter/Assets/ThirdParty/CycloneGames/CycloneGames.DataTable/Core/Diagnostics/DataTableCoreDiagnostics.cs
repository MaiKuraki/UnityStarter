using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DataTable
{
    internal static class DataTableCoreDiagnostics
    {
        internal static bool TryGetEnabled(
            DataTableDiagnosticLevel level,
            string category,
            out IDataTableDiagnostics diagnostics)
        {
            if (!IsOutputLevel(level))
            {
                diagnostics = null;
                return false;
            }

            diagnostics = DataTableDiagnostics.Current;
            try
            {
                if (diagnostics.IsEnabled(level, category))
                {
                    return true;
                }
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
            }

            diagnostics = null;
            return false;
        }

        internal static bool TryWrite(
            IDataTableDiagnostics diagnostics,
            DataTableDiagnosticLevel level,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (diagnostics == null || !IsOutputLevel(level))
            {
                return false;
            }

            try
            {
                diagnostics.Write(level, category, message, filePath, lineNumber, memberName);
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        internal static bool TryWriteException(
            IDataTableDiagnostics diagnostics,
            DataTableDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (diagnostics == null || !IsOutputLevel(level))
            {
                return false;
            }

            try
            {
                diagnostics.WriteException(
                    level,
                    category,
                    exception,
                    message,
                    filePath,
                    lineNumber,
                    memberName);
                return true;
            }
            catch (Exception sinkException) when (!(sinkException is OutOfMemoryException))
            {
                return false;
            }
        }

        /// <summary>
        /// Emits best-effort diagnostics after the authoritative registry transition has committed.
        /// Ordinary sink failures cannot make the completed transition appear to have failed.
        /// </summary>
        internal static void CommittedRegistryPublish(long generation, int tableCount)
        {
            if (!TryGetEnabled(
                    DataTableDiagnosticLevel.Info,
                    DataTableDiagnosticCategories.Root,
                    out IDataTableDiagnostics diagnostics))
            {
                return;
            }

            TryWrite(
                diagnostics,
                DataTableDiagnosticLevel.Info,
                DataTableDiagnosticCategories.Root,
                $"DataTableRegistry published generation {generation} ({tableCount} tables).");
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOutputLevel(DataTableDiagnosticLevel level)
        {
            return (byte)level <= (byte)DataTableDiagnosticLevel.Fatal;
        }
    }
}
