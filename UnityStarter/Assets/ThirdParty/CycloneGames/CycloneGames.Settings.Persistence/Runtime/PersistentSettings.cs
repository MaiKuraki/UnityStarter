using System;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;

namespace CycloneGames.Settings.Persistence
{
    public sealed class PersistentSettings<T>
    {
        private readonly SettingsState<T> _state;
        private readonly PersistenceStore<T> _persistence;
        private int _operationState;
        private long _startedLoadCount;
        private long _startedSaveCount;
        private long _startedDeleteCount;
        private long _concurrentOperationRejectionCount;

        public PersistentSettings(
            SettingsState<T> state,
            PersistenceStore<T> persistence)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));
        }

        public PersistentSettingsMemorySnapshot GetMemorySnapshot()
        {
            PersistenceStoreMemorySnapshot persistence = _persistence.GetMemorySnapshot();
            return new PersistentSettingsMemorySnapshot(
                Volatile.Read(ref _operationState) != 0,
                _state.Revision,
                persistence.IsOperationActive,
                persistence.MaximumPayloadBytes,
                persistence.MaximumRecordBytes,
                Interlocked.Read(ref _startedLoadCount),
                Interlocked.Read(ref _startedSaveCount),
                Interlocked.Read(ref _startedDeleteCount),
                Interlocked.Read(ref _concurrentOperationRejectionCount),
                persistence.LastRecordBytes,
                persistence.PeakRecordBytes);
        }

        public Task<PersistentSettingsLoadResult> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            Interlocked.Increment(ref _startedLoadCount);
            try
            {
                return LoadCoreAsync(cancellationToken);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        public Task<PersistenceOperationResult> SaveAsync(
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            Interlocked.Increment(ref _startedSaveCount);
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    EndOperation();
                    return Task.FromResult(PersistenceOperationResult.Failure(
                        PersistenceErrorCode.Cancelled,
                        new OperationCanceledException(cancellationToken)));
                }

                var snapshot = _state.Snapshot();
                return SaveCoreAsync(snapshot, cancellationToken);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        public Task<PersistenceOperationResult> DeleteStoredValueAsync(
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
            Interlocked.Increment(ref _startedDeleteCount);
            try
            {
                return DeleteCoreAsync(cancellationToken);
            }
            catch
            {
                EndOperation();
                throw;
            }
        }

        // The revision captured before I/O is the compare-and-commit guard. A state change during
        // the read wins; the loaded candidate is discarded without touching the authoritative value.
        private async Task<PersistentSettingsLoadResult> LoadCoreAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    return PersistentSettingsLoadResult.PersistenceFailure(
                        PersistenceErrorCode.Cancelled,
                        new OperationCanceledException(cancellationToken));
                }

                long expectedRevision = _state.Revision;
                PersistenceLoadResult<T> loadResult = await _persistence.LoadAsync(cancellationToken)
                    .ConfigureAwait(false);

                if (loadResult.IsMissing)
                {
                    return PersistentSettingsLoadResult.Missing();
                }

                if (!loadResult.IsSuccess)
                {
                    return PersistentSettingsLoadResult.PersistenceFailure(
                        loadResult.ErrorCode,
                        loadResult.Exception);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var candidate = loadResult.Value;
                SettingsUpdateResult commitResult = _state.TryApplyLoaded(in candidate, expectedRevision);
                if (!commitResult.Succeeded)
                {
                    return PersistentSettingsLoadResult.StateCommitFailure(in commitResult);
                }

                return PersistentSettingsLoadResult.Loaded(
                    commitResult.ObserverFailureCount,
                    commitResult.FirstObserverException);
            }
            catch (OperationCanceledException exception)
                when (cancellationToken.IsCancellationRequested)
            {
                return PersistentSettingsLoadResult.PersistenceFailure(
                    PersistenceErrorCode.Cancelled,
                    exception);
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task<PersistenceOperationResult> SaveCoreAsync(
            T snapshot,
            CancellationToken cancellationToken)
        {
            try
            {
                return await _persistence.SaveAsync(in snapshot, cancellationToken)
                    .ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        private async Task<PersistenceOperationResult> DeleteCoreAsync(
            CancellationToken cancellationToken)
        {
            try
            {
                return await _persistence.DeleteAsync(cancellationToken).ConfigureAwait(false);
            }
            finally
            {
                EndOperation();
            }
        }

        // The guard covers this integration instance. Its dependencies keep their own guards.
        private void BeginOperation()
        {
            if (Interlocked.CompareExchange(ref _operationState, 1, 0) != 0)
            {
                Interlocked.Increment(ref _concurrentOperationRejectionCount);
                throw new InvalidOperationException(
                    "A PersistentSettings instance permits only one active operation.");
            }
        }

        private void EndOperation()
        {
            Volatile.Write(ref _operationState, 0);
        }
    }
}
