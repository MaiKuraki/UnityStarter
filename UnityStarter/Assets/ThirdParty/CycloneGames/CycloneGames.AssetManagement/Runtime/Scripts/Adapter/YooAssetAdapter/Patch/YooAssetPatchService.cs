#if YOOASSET_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using R3;

namespace CycloneGames.AssetManagement.Runtime
{
    public class YooAssetPatchService : IPatchService
    {
        private enum EPatchState
        {
            None,
            Initialize,
            CheckVersion,
            UpdateManifest,
            Download,
            Done
        }

        private readonly IAssetPackage _package;
        private readonly Subject<(PatchEvent, object)> _patchEvents = new Subject<(PatchEvent, object)>();
        private EPatchState _currentState = EPatchState.None;
        private IDownloader _downloader;

        public string PackageName => _package.Name;
        public Observable<(PatchEvent, object)> PatchEvents => _patchEvents;

        public YooAssetPatchService(IAssetPackage package)
        {
            _package = package ?? throw new ArgumentNullException(nameof(package));
        }

        public async UniTask RunAsync(bool autoDownloadOnFoundNewVersion, CancellationToken cancellationToken = default)
        {
            if (_currentState != EPatchState.None)
            {
                throw new InvalidOperationException("Patch service is already running.");
            }

            try
            {
                SetState(EPatchState.Initialize);
                
                SetState(EPatchState.CheckVersion);
                string packageVersion = await _package.RequestPackageVersionAsync(cancellationToken: cancellationToken);
                
                var updateManifestOperation = await _package.UpdatePackageManifestAsync(packageVersion, 60, cancellationToken);
                if (!updateManifestOperation)
                {
                    throw new Exception("Failed to update package manifest.");
                }

                SetState(EPatchState.Download);
                _downloader = _package.CreateDownloaderForAll(10, 3);

                if (_downloader.TotalDownloadCount == 0)
                {
                    SetState(EPatchState.Done);
                    _patchEvents.OnNext((PatchEvent.PatchDone, null));
                    return;
                }

                var args = new FoundNewVersionEventArgs
                {
                    PackageVersion = packageVersion,
                    TotalDownloadSizeBytes = _downloader.TotalDownloadBytes
                };
                _patchEvents.OnNext((PatchEvent.FoundNewVersion, args));

                if (autoDownloadOnFoundNewVersion)
                {
                    await DownloadInternal(cancellationToken);
                }
            }
            catch (Exception e)
            {
                _patchEvents.OnNext((PatchEvent.PatchFailed, e));
                throw;
            }
        }

        public void Download()
        {
            DownloadInternal(CancellationToken.None).Forget();
        }

        public void Cancel()
        {
            _downloader?.Cancel();
        }

        private async UniTask DownloadInternal(CancellationToken cancellationToken)
        {
            if (_downloader == null)
            {
                throw new InvalidOperationException("Downloader is not created. Call RunAsync first.");
            }

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

            if (_downloader.Succeed)
            {
                SetState(EPatchState.Done);
                _patchEvents.OnNext((PatchEvent.PatchDone, null));
            }
            else
            {
                throw new Exception($"Download failed: {_downloader.Error}");
            }
        }

        private void SetState(EPatchState newState)
        {
            _currentState = newState;
            _patchEvents.OnNext((PatchEvent.PatchStatesChanged, newState));
        }

        public void Dispose()
        {
            _patchEvents?.Dispose();
        }
    }
}
#endif
