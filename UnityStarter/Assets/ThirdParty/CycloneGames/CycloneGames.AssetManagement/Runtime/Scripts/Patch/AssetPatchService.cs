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

        private PatchWorkflowState _currentState = PatchWorkflowState.None;
        private PatchRunOptions _pendingOptions;
        private IDownloader _downloader;
        private string _pendingPackageVersion;
        private bool _downloadInProgress;
        private bool _disposed;

        public AssetPatchService(IAssetPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
            _trustProcessor = new PatchContentTrustProcessor(_package, SetState, PublishEvent);
        }

        public string PackageName => _package.Name;

        public Observable<(PatchEvent, object)> PatchEvents => _patchEvents;

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

            try
            {
                SetState(PatchWorkflowState.Initialize);

                SetState(PatchWorkflowState.CheckVersion);
                string packageVersion = await _package.RequestPackageVersionAsync(
                    options.AppendTimeTicks,
                    options.DownloadOptions.RequestTimeoutSeconds,
                    cancellationToken);

                SetState(PatchWorkflowState.UpdateManifest);
                bool manifestUpdated = await _package.UpdatePackageManifestAsync(
                    packageVersion,
                    options.DownloadOptions.RequestTimeoutSeconds,
                    cancellationToken);
                if (!manifestUpdated)
                {
                    throw new InvalidOperationException("Failed to update package manifest.");
                }

                _downloader = _package.CreateDownloaderForAll(
                    options.DownloadOptions.MaxConcurrentDownloads,
                    options.DownloadOptions.FailedRetryCount);
                if (_downloader == null)
                {
                    throw new InvalidOperationException("Patch downloader was not created.");
                }

                _pendingPackageVersion = packageVersion;

                if (_downloader.TotalDownloadCount == 0)
                {
                    return await CompleteAfterDownloadAsync(options, packageVersion, 0, 0L, cancellationToken);
                }

                var foundArgs = new FoundNewVersionEventArgs
                {
                    PackageVersion = packageVersion,
                    TotalDownloadSizeBytes = _downloader.TotalDownloadBytes
                };
                _patchEvents.OnNext((PatchEvent.FoundNewVersion, foundArgs));

                if (options.AutoDownloadOnFoundNewVersion)
                {
                    return await DownloadAsync(cancellationToken);
                }

                SetState(PatchWorkflowState.WaitingForDownload);
                return CreateResult(
                    PatchRunStatus.PendingDownload,
                    packageVersion,
                    null,
                    _downloader.TotalDownloadCount,
                    _downloader.TotalDownloadBytes,
                    options.TrustOptions.Enabled,
                    0,
                    0UL,
                    null);
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

            try
            {
                return await DownloadInternalAsync(_pendingOptions, _pendingPackageVersion, cancellationToken);
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
            _downloader?.Cancel();
            SetState(PatchWorkflowState.Cancelled);
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

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
                    cancellationToken.ThrowIfCancellationRequested();

                    progressArgs.TotalDownloadCount = _downloader.TotalDownloadCount;
                    progressArgs.CurrentDownloadCount = _downloader.CurrentDownloadCount;
                    progressArgs.TotalDownloadSizeBytes = _downloader.TotalDownloadBytes;
                    progressArgs.CurrentDownloadSizeBytes = _downloader.CurrentDownloadBytes;

                    _patchEvents.OnNext((PatchEvent.DownloadProgress, progressArgs));
                    await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
                }

                if (!_downloader.Succeed)
                {
                    throw new InvalidOperationException($"Download failed: {_downloader.Error}");
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
                PatchTrustVerificationEventArgs trustArgs = _trustProcessor.Verify(options.TrustOptions, packageVersion);
                trustFailureCount = trustArgs.FailureCount;
                trustFingerprint = trustArgs.ManifestFingerprint;
                PublishEvent(PatchEvent.ContentTrustVerified, trustArgs);

                if (trustFailureCount > 0)
                {
                    trustArgs = await _trustProcessor.HandleFailureAsync(options, PackageName, packageVersion, trustArgs, cancellationToken);
                    trustFailureCount = trustArgs.FailureCount;
                    trustFingerprint = trustArgs.ManifestFingerprint;
                }
            }

            SetState(PatchWorkflowState.Done);
            PatchRunResult result = CreateResult(
                PatchRunStatus.Succeeded,
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
                totalDownloadCount,
                totalDownloadBytes,
                trustEnabled,
                trustFailureCount,
                trustFingerprint,
                error);
        }

        private void PublishFailure(Exception ex)
        {
            if (ex is OperationCanceledException)
            {
                SetState(PatchWorkflowState.Cancelled);
                PublishEvent(PatchEvent.PatchFailed, ex);
                return;
            }

            SetState(PatchWorkflowState.Failed);
            PublishEvent(PatchEvent.PatchFailed, ex);
        }

        private void SetState(PatchWorkflowState newState)
        {
            _currentState = newState;
            PublishEvent(PatchEvent.PatchStatesChanged, newState);
        }

        private void PublishEvent(PatchEvent patchEvent, object args)
        {
            _patchEvents.OnNext((patchEvent, args));
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
