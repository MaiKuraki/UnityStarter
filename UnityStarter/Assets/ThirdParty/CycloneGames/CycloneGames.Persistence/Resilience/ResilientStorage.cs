using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Hash.Core;
using CycloneGames.Persistence;

namespace CycloneGames.Persistence.Resilience
{
    public readonly struct RecoveryReport
    {
        internal RecoveryReport(
            int pending,
            int committed,
            int restored,
            int corrupt,
            int unrecoverable)
        {
            PendingCount = pending;
            CommittedCount = committed;
            RestoredCount = restored;
            CorruptCount = corrupt;
            UnrecoverableCount = unrecoverable;
        }

        public int PendingCount { get; }

        public int CommittedCount { get; }

        public int RestoredCount { get; }

        public int CorruptCount { get; }

        public int UnrecoverableCount { get; }
    }

    public readonly struct ResilienceDiagnostics
    {
        internal ResilienceDiagnostics(
            int backupCount,
            int pendingCount,
            int gateWaitingDepth,
            long gateRejectedCount,
            long gateAcquiredCount,
            long leaseConflictCount,
            long commitRejectedCount,
            long eventsEmitted)
        {
            BackupCount = backupCount;
            PendingCount = pendingCount;
            GateWaitingDepth = gateWaitingDepth;
            GateRejectedCount = gateRejectedCount;
            GateAcquiredCount = gateAcquiredCount;
            LeaseConflictCount = leaseConflictCount;
            CommitRejectedCount = commitRejectedCount;
            EventsEmitted = eventsEmitted;
        }

        public int BackupCount { get; }

        public int PendingCount { get; }

        public int GateWaitingDepth { get; }

        public long GateRejectedCount { get; }

        public long GateAcquiredCount { get; }

        public long LeaseConflictCount { get; }

        public long CommitRejectedCount { get; }

        public long EventsEmitted { get; }
    }

    /// <summary>
    /// Storage decorator that adds bounded admission, serial commits, a cross-process lease, a
    /// write-ahead marker, backup rotation, and deterministic recovery.

    /// </summary>
    public sealed class ResilientStorage : IPersistenceStorage
    {
        private const string LockFileName = "owner.lock";
        private const string BackupPrefix = "backup-";
        private const string BackupExtension = ".bak";
        private const string BackupSearchPattern = "backup-*.bak";

        private static readonly StringComparer BackupNameOrder = StringComparer.Ordinal;

        private readonly IPersistenceStorage _inner;
        private readonly ResilienceOptions _options;
        private readonly PendingMarkerLog _wal;
        private readonly BoundedWriteGate _gate;
        private readonly SemaphoreSlim _commitGate;
        private readonly FileProcessLease _lease;
        private long _backupGeneration;
        private long _commitRejected;
        private long _eventsEmitted;

        public ResilientStorage(IPersistenceStorage inner, ResilienceOptions options)
        {
            _inner = inner ?? throw new ArgumentNullException(nameof(inner));
            _options = options ?? throw new ArgumentNullException(nameof(options));
            Directory.CreateDirectory(options.WalDirectory);
            _wal = new PendingMarkerLog(options.WalDirectory);
            _gate = new BoundedWriteGate(
                options.WriteGateCapacity,
                options.WriteGateTimeout,
                options.EventSink);
            _commitGate = new SemaphoreSlim(1, 1);
            _lease = new FileProcessLease(
                Path.Combine(options.WalDirectory, LockFileName),
                options.LeaseExpiry,
                options.LeaseAcquireAttempts,
                options.EventSink);
        }

        public string Location => _inner.Location;

        public Task<PersistenceStorageReadResult> ReadAsync(
            int maxByteCount,
            CancellationToken cancellationToken = default)
        {
            return _inner.ReadAsync(maxByteCount, cancellationToken);
        }

        public async Task WriteAtomicallyAsync(
            byte[] content,
            CancellationToken cancellationToken = default)
        {
            if (content == null)
            {
                throw new ArgumentNullException(nameof(content));
            }

            using (await _gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                bool entered = await _commitGate
                    .WaitAsync(_options.WriteGateTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (!entered)
                {
                    Interlocked.Increment(ref _commitRejected);
                    Emit(PersistenceEventKind.WriteRejected, PersistenceErrorCode.WriteFailed, content.Length, 0);
                    throw new TimeoutException(
                        "The commit gate rejected the operation after its configured wait.");
                }

                try
                {
                    if (!_lease.TryAcquire())
                    {
                        throw new InvalidOperationException(
                            "Another writer holds the persistence lease.");
                    }

                    try
                    {
                        string backupName = await CreateBackupAsync(cancellationToken)
                            .ConfigureAwait(false);
                        string commitId = _wal.Begin(content, backupName);
                        await _inner.WriteAtomicallyAsync(content, cancellationToken)
                            .ConfigureAwait(false);
                        _wal.Complete(commitId);
                        PruneBackups();
                        Emit(PersistenceEventKind.SaveSucceeded, PersistenceErrorCode.None, content.Length, 0);
                    }
                    catch
                    {
                        Emit(PersistenceEventKind.SaveFailed, PersistenceErrorCode.WriteFailed, content.Length, 0);
                        throw;
                    }
                    finally
                    {
                        _lease.Release();
                    }
                }
                finally
                {
                    _commitGate.Release();
                }
            }
        }

        public async Task DeleteAsync(CancellationToken cancellationToken = default)
        {
            using (await _gate.AcquireAsync(cancellationToken).ConfigureAwait(false))
            {
                bool entered = await _commitGate
                    .WaitAsync(_options.WriteGateTimeout, cancellationToken)
                    .ConfigureAwait(false);
                if (!entered)
                {
                    Interlocked.Increment(ref _commitRejected);
                    throw new TimeoutException(
                        "The commit gate rejected the delete after its configured wait.");
                }

                try
                {
                    if (!_lease.TryAcquire())
                    {
                        throw new InvalidOperationException(
                            "Another writer holds the persistence lease.");
                    }

                    try
                    {
                        await _inner.DeleteAsync(cancellationToken).ConfigureAwait(false);
                    }
                    finally
                    {
                        _lease.Release();
                    }
                }
                finally
                {
                    _commitGate.Release();
                }
            }
        }

        /// <summary>
        /// Replays pending markers after an interrupted write. A target whose length and digest
        /// match the marker is treated as committed; otherwise the recorded backup is restored.
        /// </summary>
        public async Task<RecoveryReport> RecoverAsync(
            CancellationToken cancellationToken = default)
        {
            string[] pending = _wal.EnumeratePending();
            Array.Sort(pending, BackupNameOrder);
            int committed = 0;
            int restored = 0;
            int corrupt = 0;
            int unrecoverable = 0;

            for (int index = 0; index < pending.Length; index++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string fileName = Path.GetFileName(pending[index]);
                if (!_wal.TryRead(pending[index], out WalEntry entry))
                {
                    corrupt++;
                    Emit(PersistenceEventKind.WalCorrupt, PersistenceErrorCode.ReadFailed, 0, 0);
                    continue;
                }

                PersistenceStorageReadResult current = await _inner
                    .ReadAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
                bool targetIntact = !current.IsMissing
                    && current.Content != null
                    && current.Content.Length == entry.ContentLength
                    && XxHash64.Compute(current.Content) == entry.ContentHash;
                if (targetIntact)
                {
                    _wal.Complete(fileName);
                    committed++;
                    Emit(PersistenceEventKind.RecoveryCommitted, PersistenceErrorCode.None, entry.ContentLength, 0);
                    continue;
                }

                bool recovered = false;
                if (entry.BackupName.Length > 0)
                {
                    string backupPath = Path.Combine(_options.WalDirectory, entry.BackupName);
                    if (File.Exists(backupPath))
                    {
                        byte[] backup = File.ReadAllBytes(backupPath);
                        await _inner.WriteAtomicallyAsync(backup, cancellationToken)
                            .ConfigureAwait(false);
                        recovered = true;
                    }
                }

                if (recovered)
                {
                    restored++;
                    Emit(PersistenceEventKind.RecoveryRestored, PersistenceErrorCode.None, entry.ContentLength, 0);
                }
                else
                {
                    unrecoverable++;
                }

                _wal.Complete(fileName);
            }

            return new RecoveryReport(pending.Length, committed, restored, corrupt, unrecoverable);
        }

        public ResilienceDiagnostics GetDiagnostics()
        {
            int backups = Directory.GetFiles(_options.WalDirectory, BackupSearchPattern).Length;
            int pending = _wal.EnumeratePending().Length;
            return new ResilienceDiagnostics(
                backups,
                pending,
                _gate.WaitingDepth,
                _gate.RejectedCount,
                _gate.AcquiredCount,
                _lease.ConflictCount,
                Interlocked.Read(ref _commitRejected),
                Interlocked.Read(ref _eventsEmitted));
        }

        public void Dispose()
        {
            _gate.Dispose();
            _commitGate.Dispose();
            _lease.Dispose();
        }

        private async Task<string> CreateBackupAsync(CancellationToken cancellationToken)
        {
            PersistenceStorageReadResult current = await _inner
                .ReadAsync(int.MaxValue, cancellationToken).ConfigureAwait(false);
            if (current.IsMissing || current.Content == null)
            {
                return string.Empty;
            }

            long generation = Interlocked.Increment(ref _backupGeneration);
            string name = BackupPrefix + generation.ToString("X16") + BackupExtension;
            string path = Path.Combine(_options.WalDirectory, name);
            using (var stream = new FileStream(
                path,
                FileMode.Create,
                FileAccess.Write,
                FileShare.None))
            {
                stream.Write(current.Content, 0, current.Content.Length);
                stream.Flush();
            }

            return name;
        }

        // Generation numbers use fixed-width hexadecimal, so ordinal name order is age order.
        private void PruneBackups()
        {
            string[] files = Directory.GetFiles(_options.WalDirectory, BackupSearchPattern);
            if (files.Length <= _options.BackupGenerationCount)
            {
                return;
            }

            Array.Sort(files, BackupNameOrder);
            int removeCount = files.Length - _options.BackupGenerationCount;
            for (int index = 0; index < removeCount; index++)
            {
                try
                {
                    File.Delete(files[index]);
                    Emit(PersistenceEventKind.BackupPruned, PersistenceErrorCode.None, 0, 0);
                }
                catch (IOException)
                {
                }
                catch (UnauthorizedAccessException)
                {
                }
            }
        }

        private void Emit(
            PersistenceEventKind kind,
            PersistenceErrorCode errorCode,
            int recordBytes,
            long elapsedTicks)
        {
            Interlocked.Increment(ref _eventsEmitted);
            IPersistenceEventSink sink = _options.EventSink;
            if (sink == null)
            {
                return;
            }

            var value = new PersistenceEvent(
                kind,
                DateTime.UtcNow.Ticks,
                errorCode,
                recordBytes,
                elapsedTicks);
            sink.Record(in value);
        }
    }
}
