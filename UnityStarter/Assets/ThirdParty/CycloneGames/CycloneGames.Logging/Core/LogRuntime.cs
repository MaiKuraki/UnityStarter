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

            if (ReferenceEquals(writer, NullLogWriter.Instance))
            {
                return false;
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref _writer, writer, NullLogWriter.Instance),
                NullLogWriter.Instance);
        }

        /// <summary>
        /// Atomically replaces the process writer only when the expected writer is still installed.
        /// Neither writer is owned or disposed by this operation.
        /// </summary>
        public static bool TryReplaceWriter(ILogWriter expectedWriter, ILogWriter replacementWriter)
        {
            if (expectedWriter == null)
            {
                throw new ArgumentNullException(nameof(expectedWriter));
            }

            if (replacementWriter == null)
            {
                throw new ArgumentNullException(nameof(replacementWriter));
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref _writer, replacementWriter, expectedWriter),
                expectedWriter);
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

            if (ReferenceEquals(expectedWriter, NullLogWriter.Instance))
            {
                return false;
            }

            return TryReplaceWriter(expectedWriter, NullLogWriter.Instance);
        }
    }
}
