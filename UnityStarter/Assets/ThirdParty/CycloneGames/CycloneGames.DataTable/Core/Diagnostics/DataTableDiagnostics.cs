using System;
using System.Threading;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Process-level DataTable diagnostic port for static Core entry points. It owns no sink lifetime.
    /// </summary>
    public static class DataTableDiagnostics
    {
        private static IDataTableDiagnostics s_current = NullDataTableDiagnostics.Instance;

        public static IDataTableDiagnostics Current => Volatile.Read(ref s_current);

        public static bool TryInstall(IDataTableDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref s_current, diagnostics, NullDataTableDiagnostics.Instance),
                NullDataTableDiagnostics.Instance);
        }

        public static IDataTableDiagnostics Replace(IDataTableDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            return Interlocked.Exchange(ref s_current, diagnostics);
        }

        /// <summary>
        /// Atomically replaces the process diagnostics only while the expected instance remains installed.
        /// </summary>
        public static bool TryReplace(
            IDataTableDiagnostics expected,
            IDataTableDiagnostics replacement)
        {
            if (expected == null)
            {
                throw new ArgumentNullException(nameof(expected));
            }

            if (replacement == null)
            {
                throw new ArgumentNullException(nameof(replacement));
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref s_current, replacement, expected),
                expected);
        }

        /// <summary>
        /// Restores the silent sink only when the caller still owns the currently installed instance.
        /// </summary>
        public static bool TryReset(IDataTableDiagnostics expected)
        {
            return TryReplace(expected, NullDataTableDiagnostics.Instance);
        }
    }
}
