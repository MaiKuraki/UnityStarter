using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.Networking
{
    public enum NetworkingDiagnosticLevel : byte
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
    /// Engine-independent diagnostic port owned by Networking Core. Core treats implementations as
    /// a best-effort side channel and isolates ordinary sink failures; resource exhaustion remains
    /// visible to the host.
    /// </summary>
    public interface INetworkingDiagnostics
    {
        bool IsEnabled(NetworkingDiagnosticLevel level, string category);

        void Write(
            NetworkingDiagnosticLevel level,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");

        void WriteException(
            NetworkingDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "");
    }

    /// <summary>
    /// Failure boundary for optional diagnostics. Ordinary sink failures never alter networking behavior;
    /// resource exhaustion remains visible to the host so it can apply its process-level failure policy.
    /// </summary>
    internal static class NetworkingDiagnosticsGuard
    {
        public static bool IsEnabled(
            INetworkingDiagnostics diagnostics,
            NetworkingDiagnosticLevel level,
            string category)
        {
            if (diagnostics == null || !IsOutputLevel(level))
            {
                return false;
            }

            try
            {
                return diagnostics.IsEnabled(level, category);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
                return false;
            }
        }

        public static void Write(
            INetworkingDiagnostics diagnostics,
            NetworkingDiagnosticLevel level,
            string category,
            string message,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (diagnostics == null || !IsOutputLevel(level))
            {
                return;
            }

            try
            {
                diagnostics.Write(level, category, message, filePath, lineNumber, memberName);
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
            }
        }

        public static void WriteException(
            INetworkingDiagnostics diagnostics,
            NetworkingDiagnosticLevel level,
            string category,
            Exception exception,
            string message = null,
            [CallerFilePath] string filePath = "",
            [CallerLineNumber] int lineNumber = 0,
            [CallerMemberName] string memberName = "")
        {
            if (diagnostics == null || !IsOutputLevel(level))
            {
                return;
            }

            try
            {
                diagnostics.WriteException(level, category, exception, message, filePath, lineNumber, memberName);
            }
            catch (Exception sinkException) when (!(sinkException is OutOfMemoryException))
            {
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOutputLevel(NetworkingDiagnosticLevel level)
        {
            return (byte)level <= (byte)NetworkingDiagnosticLevel.Fatal;
        }
    }
}
