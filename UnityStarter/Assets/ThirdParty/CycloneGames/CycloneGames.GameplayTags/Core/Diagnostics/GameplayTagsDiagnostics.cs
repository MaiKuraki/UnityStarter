using System;
using System.Runtime.CompilerServices;
using System.Threading;

namespace CycloneGames.GameplayTags.Core
{
    /// <summary>
    /// Process-level GameplayTags diagnostic port for static Core entry points. It owns no sink lifetime.
    /// </summary>
    public static class GameplayTagsDiagnostics
    {
        private static IGameplayTagsDiagnostics s_current = NullGameplayTagsDiagnostics.Instance;

        private static int s_threadAffinityChecksEnabled;

        /// <summary>
        /// When true, a container records the managed thread that first mutated it and throws if another
        /// thread then mutates it. Off by default and costing nothing while off; an editor host turns it
        /// on so cross-thread writes surface as an exception at the write site instead of as torn state
        /// discovered frames later.
        /// </summary>
        /// <remarks>
        /// Reads are never checked. A container is safe to read from any thread while no thread is
        /// mutating it, and asserting the reader's thread would forbid the legitimate pattern of mutating
        /// on the main thread and reading on a worker.
        /// </remarks>
        public static bool ThreadAffinityChecksEnabled
        {
            get => Volatile.Read(ref s_threadAffinityChecksEnabled) != 0;
            set => Volatile.Write(ref s_threadAffinityChecksEnabled, value ? 1 : 0);
        }

        public static IGameplayTagsDiagnostics Current => Volatile.Read(ref s_current);

        public static bool TryInstall(IGameplayTagsDiagnostics diagnostics)
        {
            if (diagnostics == null)
            {
                throw new ArgumentNullException(nameof(diagnostics));
            }

            return ReferenceEquals(
                Interlocked.CompareExchange(ref s_current, diagnostics, NullGameplayTagsDiagnostics.Instance),
                NullGameplayTagsDiagnostics.Instance);
        }

        public static IGameplayTagsDiagnostics Replace(IGameplayTagsDiagnostics diagnostics)
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
            IGameplayTagsDiagnostics expected,
            IGameplayTagsDiagnostics replacement)
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
        public static bool TryReset(IGameplayTagsDiagnostics expected)
        {
            return TryReplace(expected, NullGameplayTagsDiagnostics.Instance);
        }
    }

    /// <summary>
    /// Failure-isolating Core boundary for the untrusted process-level diagnostics sink.
    /// Out-of-memory failures remain fatal and are deliberately allowed to propagate.
    /// </summary>
    internal static class GameplayTagsCoreDiagnostics
    {
        internal static bool TryGetEnabled(
            GameplayTagsDiagnosticLevel level,
            string category,
            out IGameplayTagsDiagnostics diagnostics)
        {
            if (!IsOutputLevel(level))
            {
                diagnostics = null;
                return false;
            }

            diagnostics = GameplayTagsDiagnostics.Current;
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
            IGameplayTagsDiagnostics diagnostics,
            GameplayTagsDiagnosticLevel level,
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
            IGameplayTagsDiagnostics diagnostics,
            GameplayTagsDiagnosticLevel level,
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

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsOutputLevel(GameplayTagsDiagnosticLevel level)
        {
            return (byte)level <= (byte)GameplayTagsDiagnosticLevel.Fatal;
        }
    }
}
