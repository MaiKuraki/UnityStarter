using System;
using System.Threading;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AssetPatchRecoveryService
    {
        private readonly IAssetPackage _package;
        private readonly IAssetPatchJournalStore _journalStore;
        private readonly AssetPatchRecoveryPolicy _policy;
        private readonly IAssetPatchProviderReconciler _providerReconciler;

        public AssetPatchRecoveryService(
            IAssetPackage package,
            IAssetPatchJournalStore journalStore,
            AssetPatchRecoveryPolicy policy = default,
            IAssetPatchProviderReconciler providerReconciler = null)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _journalStore = journalStore ?? throw new ArgumentNullException(nameof(journalStore));
            _policy = policy;
            _providerReconciler = providerReconciler ?? package as IAssetPatchProviderReconciler;
        }

        public async UniTask<AssetPatchRecoveryResult> RecoverAsync(CancellationToken cancellationToken = default)
        {
            if (!_journalStore.TryRead(out AssetPatchJournalRecord record, out string readError))
            {
                return new AssetPatchRecoveryResult(
                    string.IsNullOrEmpty(readError) ? AssetPatchRecoveryStatus.NoJournal : AssetPatchRecoveryStatus.JournalUnreadable,
                    AssetPatchRecoveryAction.None,
                    default,
                    default,
                    false,
                    false,
                    false,
                    false,
                    readError);
            }

            AssetPatchRecoveryRecommendation recommendation = AssetPatchJournalRecovery.Analyze(in record);

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                if (record.Status == AssetPatchJournalStatus.Succeeded)
                {
                    if (!TryClearJournalIf(_policy.ClearSucceededJournal, out bool cleared, out string clearError))
                    {
                        return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, false, false, clearError);
                    }

                    return CreateResult(AssetPatchRecoveryStatus.NoActionRequired, in record, recommendation, cleared, false, false, null);
                }

                if (record.Status == AssetPatchJournalStatus.Cancelled)
                {
                    if (!TryClearJournalIf(_policy.ClearCancelledJournal, out bool cleared, out string clearError))
                    {
                        return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, false, false, clearError);
                    }

                    return CreateResult(AssetPatchRecoveryStatus.NoActionRequired, in record, recommendation, cleared, false, false, null);
                }

                if (recommendation.Action == AssetPatchRecoveryAction.RollbackManifest &&
                    _policy.RollbackFailedJournalWithVersion &&
                    !string.IsNullOrEmpty(record.RollbackVersion))
                {
                    if (!CanUseVersionedManifestUpdate())
                    {
                        return await ReconcileProviderAsync(record, recommendation, cancellationToken);
                    }

                    if (_policy.ClearUnusedCacheAfterRollback && !CanUseCacheCleanupMode(ClearCacheMode.Unused))
                    {
                        return await ReconcileProviderAsync(record, recommendation, cancellationToken);
                    }

                    return await RollbackManifestAsync(record, recommendation, cancellationToken);
                }

                if (recommendation.Action == AssetPatchRecoveryAction.ResumeOrRestartDownload &&
                    _policy.ClearCacheForInterruptedDownload)
                {
                    if (!CanUseCacheCleanupMode(_policy.InterruptedDownloadClearMode))
                    {
                        return await ReconcileProviderAsync(record, recommendation, cancellationToken);
                    }

                    if (_policy.InterruptedDownloadClearMode == ClearCacheMode.ByTags &&
                        _policy.InterruptedDownloadClearParam == null)
                    {
                        return CreateResult(
                            AssetPatchRecoveryStatus.Failed,
                            in record,
                            recommendation,
                            false,
                            false,
                            false,
                            "Tag-scoped interrupted download cache cleanup requires a non-null clear parameter.");
                    }

                    bool cacheCleared = await _package.ClearCacheFilesAsync(
                        _policy.InterruptedDownloadClearMode,
                        _policy.InterruptedDownloadClearParam,
                        cancellationToken);
                    if (!cacheCleared)
                    {
                        return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, false, false, "Failed to clear interrupted patch cache.");
                    }

                    if (!TryClearJournalIf(_policy.ClearJournalAfterSuccessfulRecovery, out bool journalCleared, out string clearError))
                    {
                        return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, false, true, clearError);
                    }

                    return CreateResult(AssetPatchRecoveryStatus.CacheCleanupCompleted, in record, recommendation, journalCleared, false, true, null);
                }

                if (recommendation.RequiresProviderReconciliation && _providerReconciler != null)
                {
                    return await ReconcileProviderAsync(record, recommendation, cancellationToken);
                }

                return CreateResult(AssetPatchRecoveryStatus.RequiresOwnerAction, in record, recommendation, false, false, false, null);
            }
            catch (OperationCanceledException ex)
            {
                return CreateResult(AssetPatchRecoveryStatus.Cancelled, in record, recommendation, false, false, false, ex.Message);
            }
            catch (Exception ex) when (ex is InvalidOperationException || ex is NotSupportedException || ex is UnauthorizedAccessException || ex is System.IO.IOException)
            {
                return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, false, false, ex.Message);
            }
        }

        private async UniTask<AssetPatchRecoveryResult> RollbackManifestAsync(
            AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            CancellationToken cancellationToken)
        {
            bool rolledBack = await _package.UpdatePackageManifestAsync(
                record.RollbackVersion,
                _policy.ManifestUpdateTimeoutSeconds,
                cancellationToken);
            if (!rolledBack)
            {
                return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, false, false, "Failed to roll back patch manifest.");
            }

            bool cacheCleared = false;
            if (_policy.ClearUnusedCacheAfterRollback)
            {
                cacheCleared = await _package.ClearCacheFilesAsync(ClearCacheMode.Unused, cancellationToken: cancellationToken);
                if (!cacheCleared)
                {
                    return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, true, false, "Failed to clear unused cache after patch rollback.");
                }
            }

            if (!TryClearJournalIf(_policy.ClearJournalAfterSuccessfulRecovery, out bool journalCleared, out string clearError))
            {
                return CreateResult(AssetPatchRecoveryStatus.Failed, in record, recommendation, false, true, cacheCleared, clearError);
            }

            return CreateResult(AssetPatchRecoveryStatus.RollbackCompleted, in record, recommendation, journalCleared, true, cacheCleared, null);
        }

        private async UniTask<AssetPatchRecoveryResult> ReconcileProviderAsync(
            AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            CancellationToken cancellationToken)
        {
            if (_providerReconciler == null)
            {
                return CreateResult(AssetPatchRecoveryStatus.RequiresOwnerAction, in record, recommendation, false, false, false, null);
            }

            AssetPatchProviderReconciliationResult providerResult =
                await _providerReconciler.ReconcileAsync(record, recommendation, cancellationToken);

            if (providerResult.Status == AssetPatchProviderReconciliationStatus.Failed)
            {
                return CreateResult(
                    AssetPatchRecoveryStatus.Failed,
                    in record,
                    recommendation,
                    false,
                    false,
                    false,
                    providerResult.Error,
                    providerResult);
            }

            AssetPatchRecoveryStatus status =
                providerResult.Status == AssetPatchProviderReconciliationStatus.NoActionRequired
                    ? AssetPatchRecoveryStatus.NoActionRequired
                    : AssetPatchRecoveryStatus.RequiresOwnerAction;

            return CreateResult(status, in record, recommendation, false, false, false, null, providerResult);
        }

        private bool CanUseVersionedManifestUpdate()
        {
            return _providerReconciler == null ||
                   _providerReconciler.Capabilities.SupportsVersionedManifestUpdate;
        }

        private bool CanUseCacheCleanupMode(ClearCacheMode clearMode)
        {
            if (_providerReconciler == null)
            {
                return true;
            }

            AssetPatchProviderReconciliationCapabilities capabilities = _providerReconciler.Capabilities;
            if (!capabilities.SupportsExplicitCacheCleanup)
            {
                return false;
            }

            if (clearMode == ClearCacheMode.Unused && !capabilities.SupportsUnusedCacheCleanup)
            {
                return false;
            }

            if (clearMode == ClearCacheMode.ByTags && !capabilities.SupportsTagScopedCacheCleanup)
            {
                return false;
            }

            return true;
        }

        private bool TryClearJournalIf(bool shouldClear, out bool journalCleared, out string error)
        {
            journalCleared = false;
            error = null;

            if (!shouldClear)
            {
                return true;
            }

            if (_journalStore.TryClear(out error))
            {
                journalCleared = true;
                return true;
            }

            return false;
        }

        private static AssetPatchRecoveryResult CreateResult(
            AssetPatchRecoveryStatus status,
            in AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            bool journalCleared,
            bool manifestRolledBack,
            bool cacheCleared,
            string error,
            AssetPatchProviderReconciliationResult providerReconciliation = default)
        {
            return new AssetPatchRecoveryResult(
                status,
                recommendation.Action,
                record,
                recommendation,
                true,
                journalCleared,
                manifestRolledBack,
                cacheCleared,
                error,
                providerReconciliation);
        }
    }
}
