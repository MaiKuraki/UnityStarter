using System;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;

namespace CycloneGames.Settings.Persistence
{
    /// <summary>
    /// Coordinates settings state with one persistence store without owning either dependency.
    /// </summary>
    public sealed class PersistentSettings<T>
    {
        private readonly SettingsState<T> _state;
        private readonly SettingsMigrationPipeline<T> _migrations;
        private readonly PersistenceStore<T> _persistence;
        private int _operationState;

        public PersistentSettings(
            SettingsState<T> state,
            SettingsMigrationPipeline<T> migrations,
            PersistenceStore<T> persistence)
        {
            _state = state ?? throw new ArgumentNullException(nameof(state));
            _migrations = migrations ?? throw new ArgumentNullException(nameof(migrations));
            _persistence = persistence ?? throw new ArgumentNullException(nameof(persistence));

            if (state.CurrentVersion != migrations.CurrentVersion)
            {
                throw new ArgumentException(
                    "The settings state and migration pipeline must use the same current version.",
                    nameof(migrations));
            }

            if (!ReferenceEquals(state.Schema, migrations.Schema))
            {
                throw new ArgumentException(
                    "The settings state and migration pipeline must share one schema authority.",
                    nameof(migrations));
            }
        }

        public Task<PersistentSettingsLoadResult> LoadAsync(
            CancellationToken cancellationToken = default)
        {
            BeginOperation();
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
                var loadResult = await _persistence.LoadAsync(
                    _state.CurrentVersion,
                    cancellationToken).ConfigureAwait(false);

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
                var migrationResult = _migrations.Migrate(
                    loadResult.ContentVersion,
                    ref candidate,
                    cancellationToken);
                if (!migrationResult.Succeeded)
                {
                    return PersistentSettingsLoadResult.MigrationFailure(in migrationResult);
                }

                cancellationToken.ThrowIfCancellationRequested();
                var commitResult = _state.TryApplyLoaded(in candidate, expectedRevision);
                if (!commitResult.Succeeded)
                {
                    return PersistentSettingsLoadResult.StateCommitFailure(in commitResult);
                }

                return PersistentSettingsLoadResult.Loaded(
                    migrationResult.MigrationApplied,
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
                return await _persistence.SaveAsync(
                    in snapshot,
                    _state.CurrentVersion,
                    cancellationToken).ConfigureAwait(false);
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

        private void BeginOperation()
        {
            if (Interlocked.CompareExchange(ref _operationState, 1, 0) != 0)
            {
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
