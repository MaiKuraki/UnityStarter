using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Allocation-free category handle for the module-local diagnostic seam. The channel owns no
    /// sink lifetime and isolates ordinary sink failures from DataTable control flow.
    /// </summary>
    public readonly struct DataTableDiagnosticChannel
    {
        private readonly string _category;
        private readonly IDataTableDiagnostics _diagnostics;

        private DataTableDiagnosticChannel(
            string category,
            IDataTableDiagnostics diagnostics)
        {
            _category = category;
            _diagnostics = diagnostics;
        }

        public string Category => _category;

        public bool IsConfigured => _category != null;

        public static DataTableDiagnosticChannel Create(string category)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A diagnostic category is required.", nameof(category));
            }

            return new DataTableDiagnosticChannel(category, diagnostics: null);
        }

        /// <summary>
        /// Creates a channel bound to an explicit sink. Unlike the ambient overload, this channel
        /// is unaffected by later process-level diagnostic replacement.
        /// </summary>
        public static DataTableDiagnosticChannel Create(
            string category,
            IDataTableDiagnostics diagnostics)
        {
            if (string.IsNullOrWhiteSpace(category))
            {
                throw new ArgumentException("A diagnostic category is required.", nameof(category));
            }

            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            return new DataTableDiagnosticChannel(category, diagnostics);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool IsEnabled(DataTableDiagnosticLevel level)
        {
            if (_category == null || !IsOutputLevel(level))
            {
                return false;
            }

            IDataTableDiagnostics diagnostics = ResolveDiagnostics();
            try
            {
                return diagnostics.IsEnabled(level, _category);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        public bool TryWrite(
            DataTableDiagnosticLevel level,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (_category == null || !IsOutputLevel(level))
            {
                return false;
            }

            IDataTableDiagnostics diagnostics = ResolveDiagnostics();
            try
            {
                if (!diagnostics.IsEnabled(level, _category))
                {
                    return false;
                }

                diagnostics.Write(level, _category, message, filePath, lineNumber, memberName);
                return true;
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        public bool TryWriteException(
            DataTableDiagnosticLevel level,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (_category == null || !IsOutputLevel(level))
            {
                return false;
            }

            IDataTableDiagnostics diagnostics = ResolveDiagnostics();
            try
            {
                if (!diagnostics.IsEnabled(level, _category))
                {
                    return false;
                }

                diagnostics.WriteException(
                    level,
                    _category,
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOutputLevel(DataTableDiagnosticLevel level)
        {
            return (byte)level <= (byte)DataTableDiagnosticLevel.Fatal;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private IDataTableDiagnostics ResolveDiagnostics()
        {
            return _diagnostics ?? DataTableDiagnostics.Current;
        }
    }
}
