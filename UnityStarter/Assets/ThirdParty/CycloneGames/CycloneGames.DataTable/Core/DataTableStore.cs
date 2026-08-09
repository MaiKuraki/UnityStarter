using System;
using System.Threading;

namespace CycloneGames.DataTable
{
    /// <summary>
    /// Instance-owned atomic publication boundary for immutable table generations. A store owns
    /// every successfully published candidate until all readers have left that generation.
    /// </summary>
    /// <remarks>
    /// Register, publish, refresh, reset, and disposal transitions are serialized by one cold-path
    /// lock. Table lookup does not take that lock and allocates no managed memory. A registered
    /// reader pins exactly one generation, preventing the read-current/increment race that occurs
    /// when lifetime acquisition is separate from snapshot observation.
    ///
    /// A resource owner's Dispose method is invoked on whichever thread performs the last reader
    /// Refresh/Dispose, or the publication/reset that finds no readers. Unity-thread-affine owners
    /// must therefore be wrapped by an adapter whose Dispose transfers ownership to, or
    /// synchronously marshals disposal onto, the owning thread. No external owner or diagnostics
    /// callback is invoked while the transition lock is held.
    /// </remarks>
    public sealed class DataTableStore : IDisposable
    {
        private static readonly DataTableDiagnosticChannel DefaultDiagnostics =
            DataTableDiagnosticChannel.Create(DataTableDiagnosticCategories.Root);

        internal sealed class RetirementWorkItem
        {
            internal RetirementWorkItem(IDisposable resourceOwner)
            {
                ResourceOwner = resourceOwner;
            }

            internal readonly IDisposable ResourceOwner;
            // This node must never reference GenerationState, DataTableSnapshot, or a catalog.
            internal RetirementWorkItem Next;
            internal bool IsQueued;
        }

        internal sealed class GenerationState
        {
            internal GenerationState(DataTableSnapshot snapshot, IDisposable resourceOwner)
            {
                Snapshot = snapshot;
                Retirement = resourceOwner == null
                    ? null
                    : new RetirementWorkItem(resourceOwner);
                IsCurrent = true;
            }

            internal RetirementWorkItem Retirement;
            internal readonly DataTableSnapshot Snapshot;
            internal int ReaderCount;
            internal bool IsCurrent;

            internal bool HasResourceOwner => Retirement != null;
        }

        private readonly object _syncRoot = new object();
        private readonly DataTableDiagnosticChannel _diagnostics;
        private GenerationState _current;
        private RetirementWorkItem _failedRetirementHead;
        private int _failedRetirementCount;
        private int _activeReaderCount;
        private long _revisionSequenceHighWatermark;
        private bool _disposed;

        /// <param name="revisionSequenceFloor">
        /// Persisted anti-rollback floor from a trusted source. A candidate must have a sequence
        /// greater than this value. Use zero only when no prior revision has ever been accepted.
        /// </param>
        public DataTableStore(long revisionSequenceFloor = 0)
            : this(revisionSequenceFloor, DefaultDiagnostics)
        {
        }

        /// <summary>
        /// Creates a store with a domain-specific diagnostic channel while preserving the shared
        /// module-local sink contract.
        /// </summary>
        public DataTableStore(
            long revisionSequenceFloor,
            DataTableDiagnosticChannel diagnostics)
        {
            if (revisionSequenceFloor < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(revisionSequenceFloor));
            }

            if (!diagnostics.IsConfigured)
            {
                throw new ArgumentException(
                    "A configured data-table diagnostic channel is required.",
                    nameof(diagnostics));
            }

            _current = new GenerationState(DataTableSnapshot.Initial, resourceOwner: null);
            _revisionSequenceHighWatermark = revisionSequenceFloor;
            _diagnostics = diagnostics;
        }

        public bool IsDisposed
        {
            get
            {
                lock (_syncRoot)
                {
                    return _disposed;
                }
            }
        }

        /// <summary>
        /// Reads initialization, generation, revision, and the anti-rollback high-water mark from
        /// one linearizable store state. Use a registered reader for catalog access.
        /// </summary>
        public DataTableStoreMetadata Metadata
        {
            get
            {
                lock (_syncRoot)
                {
                    ThrowIfDisposed();
                    DataTableSnapshot snapshot = _current.Snapshot;
                    return new DataTableStoreMetadata(
                        snapshot.IsInitialized,
                        snapshot.Generation,
                        snapshot.Revision,
                        _revisionSequenceHighWatermark,
                        _activeReaderCount);
                }
            }
        }

        public bool IsInitialized => Metadata.IsInitialized;

        public long Generation => Metadata.Generation;

        public DataTableRevision Revision => Metadata.Revision;

        /// <summary>
        /// Gets the number of readers that must still be disposed. This remains available after
        /// store disposal so shutdown code can detect readers pinning retired generations.
        /// </summary>
        public int ActiveReaderCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _activeReaderCount;
                }
            }
        }

        /// <summary>
        /// Gets the number of resource owners whose previous disposal attempt failed and that are
        /// retained by this store for an explicit retry.
        /// </summary>
        public int FailedRetirementCount
        {
            get
            {
                lock (_syncRoot)
                {
                    return _failedRetirementCount;
                }
            }
        }

        /// <summary>
        /// Registers a long-lived reader and atomically pins the current generation. Registration
        /// allocates one reader; subsequent reads and no-op refreshes allocate no managed memory.
        /// </summary>
        public DataTableReader RegisterReader()
        {
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                GenerationState state = _current;
                int activeReaderCount = checked(_activeReaderCount + 1);
                state.ReaderCount = checked(state.ReaderCount + 1);
                try
                {
                    var reader = new DataTableReader(this, state);
                    _activeReaderCount = activeReaderCount;
                    return reader;
                }
                catch
                {
                    state.ReaderCount--;
                    throw;
                }
            }
        }

        /// <summary>
        /// Publishes a candidate only when <paramref name="expectedGeneration"/> is still current.
        /// Success transfers candidate ownership to this store. Superseded or non-monotonic
        /// results leave the candidate caller-owned and reusable.
        /// </summary>
        public DataTablePublishResult TryPublish(
            DataTableCandidate candidate,
            long expectedGeneration)
        {
            if (candidate == null)
            {
                throw new ArgumentNullException(nameof(candidate));
            }

            if (expectedGeneration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
            }

            RetirementWorkItem retirement = null;
            DataTableSnapshot committedSnapshot;
            long revisionSequenceHighWatermark;
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                lock (candidate.SyncRoot)
                {
                    candidate.GetCallerOwnedStateUnsafe(
                        out DataTableCatalog catalog,
                        out DataTableRevision revision,
                        out IDisposable resourceOwner);

                    GenerationState previous = _current;
                    if (previous.Snapshot.Generation != expectedGeneration)
                    {
                        return new DataTablePublishResult(
                            DataTablePublishStatus.Superseded,
                            expectedGeneration,
                            previous.Snapshot.Generation,
                            _revisionSequenceHighWatermark);
                    }

                    if (revision.Sequence <= _revisionSequenceHighWatermark)
                    {
                        return new DataTablePublishResult(
                            DataTablePublishStatus.NonMonotonicRevision,
                            expectedGeneration,
                            previous.Snapshot.Generation,
                            _revisionSequenceHighWatermark);
                    }

                    long generation = checked(previous.Snapshot.Generation + 1);
                    committedSnapshot = new DataTableSnapshot(
                        catalog,
                        generation,
                        revision,
                        isInitialized: true);
                    var next = new GenerationState(committedSnapshot, resourceOwner);

                    // No operation after this ownership transition can fail before publication.
                    candidate.MarkStoreOwnedUnsafe();
                    previous.IsCurrent = false;
                    _revisionSequenceHighWatermark = revision.Sequence;
                    revisionSequenceHighWatermark = _revisionSequenceHighWatermark;
                    Volatile.Write(ref _current, next);
                    retirement = TryTakeRetirementUnsafe(previous);
                }
            }

            RetireOwner(retirement);
            ReportCommittedPublication(committedSnapshot);
            return new DataTablePublishResult(
                DataTablePublishStatus.Committed,
                expectedGeneration,
                committedSnapshot.Generation,
                revisionSequenceHighWatermark);
        }

        /// <summary>
        /// Publishes an empty, uninitialized generation when the expected generation is current.
        /// Existing readers remain on their previous snapshot until they refresh or dispose.
        /// </summary>
        public DataTablePublishResult TryReset(long expectedGeneration)
        {
            if (expectedGeneration < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(expectedGeneration));
            }

            RetirementWorkItem retirement = null;
            DataTableSnapshot resetSnapshot;
            long revisionSequenceHighWatermark;
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                GenerationState previous = _current;
                if (previous.Snapshot.Generation != expectedGeneration)
                {
                    return new DataTablePublishResult(
                        DataTablePublishStatus.Superseded,
                        expectedGeneration,
                        previous.Snapshot.Generation,
                        _revisionSequenceHighWatermark);
                }

                long generation = checked(previous.Snapshot.Generation + 1);
                resetSnapshot = new DataTableSnapshot(
                    DataTableCatalog.Empty,
                    generation,
                    revision: DataTableRevision.None,
                    isInitialized: false);
                var next = new GenerationState(resetSnapshot, resourceOwner: null);
                previous.IsCurrent = false;
                Volatile.Write(ref _current, next);
                retirement = TryTakeRetirementUnsafe(previous);
                revisionSequenceHighWatermark = _revisionSequenceHighWatermark;
            }

            RetireOwner(retirement);
            ReportCommittedPublication(resetSnapshot);
            return new DataTablePublishResult(
                DataTablePublishStatus.Committed,
                expectedGeneration,
                resetSnapshot.Generation,
                revisionSequenceHighWatermark);
        }

        /// <summary>
        /// Stops future registration and publication. Readers already registered remain valid and
        /// continue pinning their generations until individually disposed. Dispose also retries
        /// every owner retained after an earlier retirement failure; calling it again performs
        /// another final retry without invalidating readers.
        /// </summary>
        public void Dispose()
        {
            RetirementWorkItem retirement = null;
            lock (_syncRoot)
            {
                if (!_disposed)
                {
                    _disposed = true;
                    GenerationState current = _current;
                    current.IsCurrent = false;
                    Volatile.Write(ref _current, null);
                    retirement = TryTakeRetirementUnsafe(current);
                }
            }

            // Retry previously failed owners before attempting the just-detached current
            // generation. A new failure remains retained for the next explicit retry/Dispose.
            try
            {
                RetryFailedRetirements();
            }
            catch
            {
                // A fatal retry failure must not strand the already-detached current owner in
                // this stack frame. Defer it without invoking another external callback while
                // the fatal exception is unwinding.
                if (retirement != null)
                {
                    lock (_syncRoot)
                    {
                        QueueFailedRetirementUnsafe(retirement);
                    }

                    retirement = null;
                }

                throw;
            }

            RetireOwner(retirement);
        }

        /// <summary>
        /// Retries each currently retained failed retirement once and returns the number still
        /// pending. The method is safe after store disposal and never invokes an owner under the
        /// store lock.
        /// </summary>
        public int RetryFailedRetirements()
        {
            RetirementWorkItem pending;
            lock (_syncRoot)
            {
                pending = _failedRetirementHead;
                _failedRetirementHead = null;
                _failedRetirementCount = 0;

                for (RetirementWorkItem item = pending; item != null; item = item.Next)
                {
                    item.IsQueued = false;
                }
            }

            try
            {
                while (pending != null)
                {
                    RetirementWorkItem current = pending;
                    pending = current.Next;
                    current.Next = null;
                    RetireOwner(current);
                }
            }
            finally
            {
                // An OutOfMemoryException from an owner or diagnostic sink is intentionally
                // propagated. Keep every not-yet-attempted owner reachable for a later retry.
                if (pending != null)
                {
                    lock (_syncRoot)
                    {
                        QueueFailedRetirementChainUnsafe(pending);
                    }
                }
            }

            lock (_syncRoot)
            {
                return _failedRetirementCount;
            }
        }

        internal bool RefreshReader(DataTableReader reader)
        {
            RetirementWorkItem retirement = null;
            lock (_syncRoot)
            {
                ThrowIfDisposed();
                ValidateReaderOwnerUnsafe(reader);

                GenerationState previous = reader.GenerationStateUnsafe;
                GenerationState current = _current;
                if (ReferenceEquals(previous, current))
                {
                    return false;
                }

                current.ReaderCount = checked(current.ReaderCount + 1);
                reader.SetGenerationStateUnsafe(current);
                previous.ReaderCount--;
                retirement = TryTakeRetirementUnsafe(previous);
            }

            RetireOwner(retirement);
            return true;
        }

        internal void ReleaseReader(DataTableReader reader)
        {
            RetirementWorkItem retirement = null;
            lock (_syncRoot)
            {
                if (!ReferenceEquals(reader.StoreUnsafe, this))
                {
                    return;
                }

                GenerationState state = reader.GenerationStateUnsafe;
                reader.MarkDisposedUnsafe();
                state.ReaderCount--;
                _activeReaderCount--;
                retirement = TryTakeRetirementUnsafe(state);
            }

            RetireOwner(retirement);
        }

        private static RetirementWorkItem TryTakeRetirementUnsafe(GenerationState state)
        {
            if (state.IsCurrent ||
                state.ReaderCount != 0 ||
                !state.HasResourceOwner)
            {
                return null;
            }

            RetirementWorkItem retirement = state.Retirement;
            state.Retirement = null;
            return retirement;
        }

        private void RetireOwner(RetirementWorkItem retirement)
        {
            if (retirement == null)
            {
                return;
            }

            try
            {
                retirement.ResourceOwner.Dispose();
            }
            catch (Exception exception)
            {
                lock (_syncRoot)
                {
                    QueueFailedRetirementUnsafe(retirement);
                }

                if (exception is OutOfMemoryException)
                {
                    throw;
                }

                ReportRetirementFailure(exception);
            }
        }

        private void QueueFailedRetirementChainUnsafe(RetirementWorkItem retirement)
        {
            while (retirement != null)
            {
                RetirementWorkItem next = retirement.Next;
                retirement.Next = null;
                QueueFailedRetirementUnsafe(retirement);
                retirement = next;
            }
        }

        private void QueueFailedRetirementUnsafe(RetirementWorkItem retirement)
        {
            if (retirement.IsQueued)
            {
                return;
            }

            retirement.IsQueued = true;
            retirement.Next = _failedRetirementHead;
            _failedRetirementHead = retirement;
            // More than Int32.MaxValue live work items cannot be materialized in practice. Keep
            // this recovery boundary non-throwing even if that environmental assumption breaks.
            if (_failedRetirementCount < int.MaxValue)
            {
                _failedRetirementCount++;
            }
        }

        private void ReportCommittedPublication(DataTableSnapshot snapshot)
        {
            // This is post-commit observability. Ordinary sink failures must never make the
            // authoritative transition appear to have failed to its caller.
            try
            {
                if (!_diagnostics.IsEnabled(DataTableDiagnosticLevel.Info))
                {
                    return;
                }

                _diagnostics.TryWrite(
                    DataTableDiagnosticLevel.Info,
                    $"DataTableStore published generation {snapshot.Generation} " +
                    $"(revision '{snapshot.Revision.Id}', sequence {snapshot.Revision.Sequence}, " +
                    $"{snapshot.Catalog.Count} tables).");
            }
            catch (Exception exception) when (!(exception is OutOfMemoryException))
            {
            }
        }

        private void ReportRetirementFailure(Exception exception)
        {
            try
            {
                _diagnostics.TryWriteException(
                    DataTableDiagnosticLevel.Error,
                    exception,
                    "A retired data-table resource owner threw while being disposed.");
            }
            catch (Exception diagnosticException) when (!(diagnosticException is OutOfMemoryException))
            {
            }
        }

        private void ValidateReaderOwnerUnsafe(DataTableReader reader)
        {
            if (reader == null)
            {
                throw new ArgumentNullException(nameof(reader));
            }

            if (!ReferenceEquals(reader.StoreUnsafe, this) || reader.GenerationStateUnsafe == null)
            {
                throw new ObjectDisposedException(nameof(DataTableReader));
            }
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(DataTableStore));
            }
        }
    }
}
