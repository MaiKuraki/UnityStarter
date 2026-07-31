using System;
using System.Threading;

namespace CycloneGames.Logging
{
    /// <summary>
    /// Process-wide fallback writer for static and Unity-owned entry points. Plain C# services
    /// should prefer an explicitly supplied writer. This class never disposes installed writers.
    /// </summary>
    public static class LogRuntime
    {
        private static ILogWriter _writer = NullLogWriter.Instance;

        public static ILogWriter Writer => Volatile.Read(ref _writer);

        public static bool HasWriter => !ReferenceEquals(Writer, NullLogWriter.Instance);

        /// <summary>
        /// Installs a writer only when no backend is currently installed.
        /// </summary>
        public static bool TryInstallWriter(ILogWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref _writer, writer, NullLogWriter.Instance),
                NullLogWriter.Instance);
        }

        /// <summary>
        /// Atomically replaces the process writer and returns the previous unowned writer.
        /// The caller remains responsible for draining and disposing any backend it owns.
        /// </summary>
        public static ILogWriter ReplaceWriter(ILogWriter writer)
        {
            if (writer == null)
            {
                throw new ArgumentNullException(nameof(writer));
            }

            return Interlocked.Exchange(ref _writer, writer);
        }

        /// <summary>
        /// Resets the process writer only when the expected writer is still installed.
        /// </summary>
        public static bool TryResetWriter(ILogWriter expectedWriter)
        {
            if (expectedWriter == null)
            {
                throw new ArgumentNullException(nameof(expectedWriter));
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref _writer, NullLogWriter.Instance, expectedWriter),
                expectedWriter);
        }

        /// <summary>
        /// Unconditionally resets the process writer and returns the previous unowned writer.
        /// </summary>
        public static ILogWriter ResetWriter()
        {
            return Interlocked.Exchange(ref _writer, NullLogWriter.Instance);
        }
    }
}
