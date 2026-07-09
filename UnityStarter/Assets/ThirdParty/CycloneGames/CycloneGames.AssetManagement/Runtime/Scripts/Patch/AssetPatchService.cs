using System;
using System.Threading;

using Cysharp.Threading.Tasks;
using R3;

namespace CycloneGames.AssetManagement.Runtime
{
    public class AssetPatchService : IPatchService, IAssetPatchTransactionService
    {
        private readonly IAssetPackage _package;
        private readonly Subject<(PatchEvent, object)> _patchEvents = new Subject<(PatchEvent, object)>();
        private readonly PatchContentTrustProcessor _trustProcessor;
        private readonly AssetPatchJournalOptions _journalOptions;

        private PatchWorkflowState _currentState = PatchWorkflowState.None;
        private PatchRunOptions _pendingOptions;
        private IDownloader _downloader;
        private string _pendingPackageVersion;
        private long _journalSequence;
        private long _journalStartedUtcTicks;
        private int _lastTrustFailureCount;
        private ulong _lastTrustFingerprint;
        private bool _downloadInProgress;
        private bool _cancelRequested;
        private bool _disposed;

        public AssetPatchService(IAssetPackage package)
            : this(package, AssetPatchJournalOptions.Disabled)
        {
        }

        public AssetPatchService(IAssetPackage package, AssetPatchJournalOptions journalOptions)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _journalOptions = journalOptions;
            _trustProcessor = new PatchContentTrustProcessor(_package, SetState, PublishEvent);
        }

        public string PackageName => _package.Name;

        public Observable<(PatchEvent, object)> PatchEvents => _patchEvents;
        public string LastJournalWriteError { get; private set; }

        public async UniTask RunAsync(
            bool autoDownloadOnFoundNewVersion,
            PatchDownloadOptions downloadOptions = default,
            CancellationToken cancellationToken = default)
        {
            await RunAsync(PatchRunOptions.Legacy(autoDownloadOnFoundNewVersion, downloadOptions), cancellationToken);
        }

        public async UniTask<PatchRunResult> RunAsync(PatchRunOptions options, CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_currentState != PatchWorkflowState.None &&
                _currentState != PatchWorkflowState.Done &&
                _currentState != PatchWorkflowState.Failed &&
                _currentState != PatchWorkflowState.Cancelled)
            {
                throw new InvalidOperationException("Patch service is already running.");
            }

            options = options.Normalized();
            _pendingOptions = options;
            _pendingPackageVersion = null;
            _downloader = null;
            _journalStartedUtcTicks = DateTime.UtcNow.Ticks;
            _journalSequence = 0L;
            _lastTrustFailureCount = 0;
            _lastTrustFingerprint = options.TrustOptions.Enabled ? options.TrustOptions.Manifest.ComputeFingerprint() : 0UL;
            LastJournalWriteError = null;
            _cancelRequested = false;

            try
            {
                SetState(PatchWorkflowState.Initialize);
                ThrowIfCancellationRequested();

                SetState(PatchWorkflowState.CheckVersion);
                string packageVersion;
                try
                {
                    packageVersion = await _package.RequestPackageVersionAsync(
                        options.AppendTimeTicks,
                        options.DownloadOptions.RequestTimeoutSeconds,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return PublishProviderFailure(
                        PatchFailureKind.PackageVersionRequestFailed,
                        options,
                        null,
                        null,
                        "Failed to request package version",
                        ex.Message);
                }
                ThrowIfCancellationRequested();

                SetState(PatchWorkflowState.UpdateManifest);
                bool manifestUpdated;
                try
                {
                    manifestUpdated = await _package.UpdatePackageManifestAsync(
                        packageVersion,
                        options.DownloadOptions.RequestTimeoutSeconds,
                        cancellationToken);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    return PublishProviderFailure(
                        PatchFailureKind.ManifestUpdateFailed,
                        options,
                        packageVersion,
                        null,
                        "Failed to update package manifest",
                        ex.Message);
                }
                ThrowIfCancellationRequested();
                if (!manifestUpdated)
                {
                    return PublishProviderFailure(
                        PatchFailureKind.ManifestUpdateFailed,
                        options,
                        packageVersion,
                        null,
                        "Failed to update package manifest",
                        null);
                }

                try
                {
                    _downloader = _package.CreateDownloaderForAll(
                        options.DownloadOptions.MaxConcurrentDownloads,
                        options.DownloadOptions.FailedRetryCount);
                }
                catch (Exception ex)
                {
                    return PublishProviderFailure(
                        PatchFailureKind.DownloaderCreationFailed,
                        options,
                        packageVersion,
                        null,
                        "Patch downloader was not created",
                        ex.Message);
                }
                if (_downloader == null)
                {
                    return PublishProviderFailure(
                        PatchFailureKind.DownloaderCreationFailed,
                        options,
                        packageVersion,
                        null,
                        "Patch downloader was not created",
                        null);
                }

                _pendingPackageVersion = packageVersion;
                WriteJournalCheckpoint(_currentState, GetJournalStatusForState(_currentState), null);

                if (_downloader.TotalDownloadCount == 0)
                {
                    return await CompleteAfterDownloadAsync(options, packageVersion, 0, 0L, cancellationToken);
                }

                var foundArgs = new FoundNewVersionEventArgs
                {
                    PackageVersion = packageVersion,
                    TotalDownloadSizeBytes = _downloader.TotalDownloadBytes
                };
                PublishEvent(PatchEvent.FoundNewVersion, foundArgs);
                ThrowIfCancellationRequested();

                if (options.AutoDownloadOnFoundNewVersion)
                {
                    return await DownloadAsync(cancellationToken);
                }

                SetState(PatchWorkflowState.WaitingForDownload);
                return CreateResult(
                    PatchRunStatus.PendingDownload,
                    PatchFailureKind.None,
                    packageVersion,
                    null,
                    _downloader.TotalDownloadCount,
                    _downloader.TotalDownloadBytes,
                    options.TrustOptions.Enabled,
                    0,
                    0UL,
                    null);
            }
            catch (OperationCanceledException ex)
            {
                return PublishCancellation(ex, options, _pendingPackageVersion, _downloader);
            }
            catch (Exception ex)
            {
                PublishFailure(ex);
                throw;
            }
        }

        public async UniTask<PatchRunResult> DownloadAsync(CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_downloader == null)
            {
                throw new InvalidOperationException("Downloader is not created. Call RunAsync first.");
            }

            if (_downloadInProgress)
            {
                throw new InvalidOperationException("Patch download is already running.");
            }

            if (_cancelRequested)
            {
                return PublishCancellation(
                    new OperationCanceledException("Patch operation was cancelled."),
                    _pendingOptions,
                    _pendingPackageVersion,
                    _downloader);
            }

            try
            {
                return await DownloadInternalAsync(_pendingOptions, _pendingPackageVersion, cancellationToken);
            }
            catch (OperationCanceledException ex)
            {
                return PublishCancellation(ex, _pendingOptions, _pendingPackageVersion, _downloader);
            }
            catch (Exception ex)
            {
                PublishFailure(ex);
                throw;
            }
        }

        public void Download()
        {
            DownloadAndPublishAsync(CancellationToken.None).Forget();
        }

        public void Cancel()
        {
            CancelDownloader(markCancelRequested: true, publishState: true);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CancelDownloader(markCancelRequested: true, publishState: false);
            _disposed = true;
            _patchEvents.Dispose();
        }

        private async UniTask DownloadAndPublishAsync(CancellationToken cancellationToken)
        {
            try
            {
                await DownloadAsync(cancellationToken);
            }
            catch
            {
                // DownloadAsync already publishes failures. The legacy fire-and-forget API cannot return them.
            }
        }

        private async UniTask<PatchRunResult> DownloadInternalAsync(
            PatchRunOptions options,
            string packageVersion,
            CancellationToken cancellationToken)
        {
            _downloadInProgress = true;
            try
            {
                SetState(PatchWorkflowState.Download);
                _downloader.Begin();

                var progressArgs = new DownloadProgressEventArgs();
                while (!_downloader.IsDone)
                {
                    ThrowIfCancellationRequested();
                    cancellationToken.ThrowIfCancellationRequested();

                    progressArgs.TotalDownloadCount = _downloader.TotalDownloadCount;
                    progressArgs.CurrentDownloadCount = _downloader.CurrentDownloadCount;
                    progressArgs.TotalDownloadSizeBytes = _downloader.TotalDownloadBytes;
                    progressArgs.CurrentDownloadSizeBytes = _downloader.CurrentDownloadBytes;

                    PublishEvent(PatchEvent.DownloadProgress, progressArgs);
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                ThrowIfCancellationRequested();
                if (!_downloader.Succeed)
                {
                    return PublishDownloadFailure(options, packageVersion, _downloader);
                }

                return await CompleteAfterDownloadAsync(
                    options,
                    packageVersion,
                    _downloader.TotalDownloadCount,
                    _downloader.TotalDownloadBytes,
                    cancellationToken);
            }
            finally
            {
                _downloadInProgress = false;
            }
        }

        private PatchRunResult PublishDownloadFailure(
            PatchRunOptions options,
            string packageVersion,
            IDownloader downloader)
        {
            string providerError = downloader == null ? null : downloader.Error;
            return PublishProviderFailure(
                PatchFailureKind.ProviderDownloadFailed,
                options,
                packageVersion,
                downloader,
                "Download failed",
                providerError);
        }

        private PatchRunResult PublishProviderFailure(
            PatchFailureKind failureKind,
            PatchRunOptions options,
            string packageVersion,
            IDownloader downloader,
            string message,
            string providerError)
        {
            _pendingPackageVersion = packageVersion;
            if (downloader != null)
            {
                _downloader = downloader;
            }

            string error = BuildProviderFailureMessage(message, providerError);
            SetState(PatchWorkflowState.Failed);
            PatchRunResult result = CreateResult(
                PatchRunStatus.Failed,
                failureKind,
                packageVersion,
                null,
                downloader?.TotalDownloadCount ?? 0,
                downloader?.TotalDownloadBytes ?? 0L,
                options.TrustOptions.Enabled,
                0,
                0UL,
                error);
            WriteJournalCheckpoint(PatchWorkflowState.Failed, AssetPatchJournalStatus.Failed, error);
            PublishEvent(PatchEvent.PatchFailed, result);
            return result;
        }

        private static string BuildProviderFailureMessage(string message, string providerError)
        {
            if (string.IsNullOrEmpty(providerError))
            {
                return message;
            }

            return $"{message}: {providerError}";
        }

        private async UniTask<PatchRunResult> CompleteAfterDownloadAsync(
            PatchRunOptions options,
            string packageVersion,
            int totalDownloadCount,
            long totalDownloadBytes,
            CancellationToken cancellationToken)
        {
            int trustFailureCount = 0;
            ulong trustFingerprint = 0UL;

            if (options.TrustOptions.Enabled)
            {
                SetState(PatchWorkflowState.VerifyContentTrust);
                ThrowIfCancellationRequested();
                PatchTrustVerificationEventArgs trustArgs = _trustProcessor.Verify(options.TrustOptions, packageVersion);
                ThrowIfCancellationRequested();
                trustFailureCount = trustArgs.FailureCount;
                trustFingerprint = trustArgs.ManifestFingerprint;
                _lastTrustFailureCount = trustFailureCount;
                _lastTrustFingerprint = trustFingerprint;
                PublishEvent(PatchEvent.ContentTrustVerified, trustArgs);
                WriteJournalCheckpoint(PatchWorkflowState.VerifyContentTrust, AssetPatchJournalStatus.InProgress, null);
                ThrowIfCancellationRequested();

                if (trustFailureCount > 0)
                {
                    trustArgs = await _trustProcessor.HandleFailureAsync(options, PackageName, packageVersion, trustArgs, cancellationToken);
                    ThrowIfCancellationRequested();
                    trustFailureCount = trustArgs.FailureCount;
                    trustFingerprint = trustArgs.ManifestFingerprint;
                    _lastTrustFailureCount = trustFailureCount;
                    _lastTrustFingerprint = trustFingerprint;
                    WriteJournalCheckpoint(_currentState, GetJournalStatusForState(_currentState), null);
                }
            }

            SetState(PatchWorkflowState.Done);
            PatchRunResult result = CreateResult(
                PatchRunStatus.Succeeded,
                PatchFailureKind.None,
                packageVersion,
                null,
                totalDownloadCount,
                totalDownloadBytes,
                options.TrustOptions.Enabled,
                trustFailureCount,
                trustFingerprint,
                null);
            PublishEvent(PatchEvent.PatchDone, result);
            return result;
        }

        private PatchRunResult CreateResult(
            PatchRunStatus status,
            PatchFailureKind failureKind,
            string packageVersion,
            string rollbackVersion,
            int totalDownloadCount,
            long totalDownloadBytes,
            bool trustEnabled,
            int trustFailureCount,
            ulong trustFingerprint,
            string error)
        {
            return new PatchRunResult(
                PackageName,
                packageVersion,
                rollbackVersion,
                status,
                failureKind,
                totalDownloadCount,
                totalDownloadBytes,
                trustEnabled,
                trustFailureCount,
                trustFingerprint,
                error);
        }

        private void PublishFailure(Exception ex)
        {
            SetState(PatchWorkflowState.Failed);
            WriteJournalCheckpoint(PatchWorkflowState.Failed, AssetPatchJournalStatus.Failed, ex.Message);
            PublishEvent(PatchEvent.PatchFailed, ex);
        }

        private PatchRunResult PublishCancellation(
            OperationCanceledException ex,
            PatchRunOptions options,
            string packageVersion,
            IDownloader downloader)
        {
            CancelDownloader(markCancelRequested: true, publishState: true);
            string error = string.IsNullOrEmpty(ex.Message) ? "Patch operation was cancelled." : ex.Message;

            PatchRunResult result = CreateResult(
                PatchRunStatus.Cancelled,
                PatchFailureKind.Cancelled,
                packageVersion,
                null,
                downloader?.TotalDownloadCount ?? 0,
                downloader?.TotalDownloadBytes ?? 0L,
                options.TrustOptions.Enabled,
                0,
                0UL,
                error);
            WriteJournalCheckpoint(PatchWorkflowState.Cancelled, AssetPatchJournalStatus.Cancelled, error);
            PublishEvent(PatchEvent.PatchFailed, result);
            return result;
        }

        private void SetState(PatchWorkflowState newState)
        {
            if (_currentState == newState)
            {
                return;
            }

            _currentState = newState;
            PublishEvent(PatchEvent.PatchStatesChanged, newState);
            WriteJournalCheckpoint(newState, GetJournalStatusForState(newState), null);
        }

        private void PublishEvent(PatchEvent patchEvent, object args)
        {
            if (_disposed)
            {
                return;
            }

            _patchEvents.OnNext((patchEvent, args));
        }

        private void CancelDownloader(bool markCancelRequested, bool publishState)
        {
            if (markCancelRequested)
            {
                _cancelRequested = true;
            }

            _downloader?.Cancel();
            if (publishState && CanTransitionToCancelled(_currentState))
            {
                SetState(PatchWorkflowState.Cancelled);
            }
        }

        private void ThrowIfCancellationRequested()
        {
            if (_cancelRequested)
            {
                throw new OperationCanceledException("Patch operation was cancelled.");
            }
        }

        private void WriteJournalCheckpoint(PatchWorkflowState stage, AssetPatchJournalStatus status, string error)
        {
            if (!_journalOptions.Enabled)
            {
                return;
            }

            try
            {
                IDownloader downloader = _downloader;
                var record = new AssetPatchJournalRecord(
                    ++_journalSequence,
                    PackageName,
                    _pendingPackageVersion,
                    GetRollbackVersion(_pendingOptions),
                    stage,
                    status,
                    downloader?.TotalDownloadCount ?? 0,
                    downloader?.TotalDownloadBytes ?? 0L,
                    _pendingOptions.TrustOptions.Enabled,
                    _lastTrustFailureCount,
                    _lastTrustFingerprint,
                    _journalStartedUtcTicks,
                    DateTime.UtcNow.Ticks,
                    error);
                _journalOptions.Store.Write(in record);
                LastJournalWriteError = null;
            }
            catch (Exception ex) when (_journalOptions.WriteFailurePolicy == AssetPatchJournalWriteFailurePolicy.ContinueWithoutJournal)
            {
                LastJournalWriteError = ex.Message;
            }
        }

        private static AssetPatchJournalStatus GetJournalStatusForState(PatchWorkflowState state)
        {
            switch (state)
            {
                case PatchWorkflowState.WaitingForDownload:
                    return AssetPatchJournalStatus.PendingDownload;
                case PatchWorkflowState.Done:
                    return AssetPatchJournalStatus.Succeeded;
                case PatchWorkflowState.Failed:
                    return AssetPatchJournalStatus.Failed;
                case PatchWorkflowState.Cancelled:
                    return AssetPatchJournalStatus.Cancelled;
                default:
                    return AssetPatchJournalStatus.InProgress;
            }
        }

        private static string GetRollbackVersion(PatchRunOptions options)
        {
            if (!string.IsNullOrEmpty(options.TrustOptions.RollbackVersionOverride))
            {
                return options.TrustOptions.RollbackVersionOverride;
            }

            return options.TrustOptions.Manifest.RollbackVersion;
        }

        private static bool CanTransitionToCancelled(PatchWorkflowState state)
        {
            return state != PatchWorkflowState.None &&
                   state != PatchWorkflowState.Done &&
                   state != PatchWorkflowState.Failed &&
                   state != PatchWorkflowState.Cancelled;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AssetPatchService));
            }
        }
    }
}
