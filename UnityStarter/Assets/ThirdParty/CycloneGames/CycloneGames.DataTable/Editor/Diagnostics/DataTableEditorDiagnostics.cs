using System;
using System.Collections.Generic;
using System.Threading;
using UnityEditor;

namespace CycloneGames.DataTable.Unity.Editor
{
    /// <summary>
    /// Main-thread dispatch for Editor diagnostics produced by the pipeline worker.
    /// </summary>
    [InitializeOnLoad]
    internal static class DataTableEditorDiagnostics
    {
        private const int MaximumPendingDiagnostics = 128;
        private static readonly object PendingSync = new object();
        private static readonly Queue<PendingDiagnostic> Pending =
            new Queue<PendingDiagnostic>(MaximumPendingDiagnostics);
        private static readonly int MainThreadId;
        private static int _droppedCount;

        internal const string Category = "CycloneGames.DataTable.Editor.Luban";

        internal static readonly DataTableDiagnosticChannel Channel =
            DataTableDiagnosticChannel.Create(Category);

        static DataTableEditorDiagnostics()
        {
            MainThreadId = Thread.CurrentThread.ManagedThreadId;
            EditorApplication.update += DrainPendingOnMainThread;
            AssemblyReloadEvents.beforeAssemblyReload += DrainPendingOnMainThread;
            EditorApplication.quitting += DrainPendingOnMainThread;
        }

        internal static void Publish(DataTableDiagnosticLevel level, string message)
        {
            if (Thread.CurrentThread.ManagedThreadId == MainThreadId)
            {
                Channel.TryWrite(level, message ?? string.Empty);
                return;
            }

            Enqueue(new PendingDiagnostic(level, message, exception: null));
        }

        internal static void PublishException(
            DataTableDiagnosticLevel level,
            Exception exception,
            string message)
        {
            if (exception == null)
            {
                throw new ArgumentNullException(nameof(exception));
            }

            if (Thread.CurrentThread.ManagedThreadId == MainThreadId)
            {
                Channel.TryWriteException(level, exception, message);
                return;
            }

            Enqueue(new PendingDiagnostic(level, message, exception));
        }

        internal static void DrainPendingOnMainThread()
        {
            if (Thread.CurrentThread.ManagedThreadId != MainThreadId)
            {
                return;
            }

            int processed = 0;
            while (processed < MaximumPendingDiagnostics &&
                   TryDequeue(out PendingDiagnostic pending))
            {
                processed++;
                if (pending.Exception == null)
                {
                    Channel.TryWrite(pending.Level, pending.Message);
                }
                else
                {
                    Channel.TryWriteException(
                        pending.Level,
                        pending.Exception,
                        pending.Message);
                }
            }

            int dropped = Interlocked.Exchange(ref _droppedCount, 0);
            if (dropped > 0)
            {
                Channel.TryWrite(
                    DataTableDiagnosticLevel.Warning,
                    "DataTable Editor diagnostics dropped " + dropped +
                    " lifecycle message(s) after the bounded queue reached capacity.");
            }
        }

        private static void Enqueue(PendingDiagnostic pending)
        {
            lock (PendingSync)
            {
                if (Pending.Count >= MaximumPendingDiagnostics)
                {
                    Interlocked.Increment(ref _droppedCount);
                    return;
                }

                Pending.Enqueue(pending);
            }
        }

        private static bool TryDequeue(out PendingDiagnostic pending)
        {
            lock (PendingSync)
            {
                if (Pending.Count == 0)
                {
                    pending = default;
                    return false;
                }

                pending = Pending.Dequeue();
                return true;
            }
        }

        private readonly struct PendingDiagnostic
        {
            internal PendingDiagnostic(
                DataTableDiagnosticLevel level,
                string message,
                Exception exception)
            {
                Level = level;
                Message = message ?? string.Empty;
                Exception = exception;
            }

            internal DataTableDiagnosticLevel Level { get; }
            internal string Message { get; }
            internal Exception Exception { get; }
        }
    }
}
