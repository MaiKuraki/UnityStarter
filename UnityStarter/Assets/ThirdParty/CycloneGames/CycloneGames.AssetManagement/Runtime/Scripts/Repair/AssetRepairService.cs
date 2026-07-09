using System;
using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;
using R3;

using CycloneGames.AssetManagement.Runtime.Trust;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AssetRepairService : IAssetRepairService
    {
        private readonly IAssetRepairTarget _target;
        private readonly AssetRepairPlanner _planner;
        private readonly Subject<(AssetRepairEvent, object)> _repairEvents = new Subject<(AssetRepairEvent, object)>();
        private readonly List<string> _locationWorkspace = new List<string>(16);

        private IDownloader _activeDownloader;
        private AssetRepairStage _currentStage = AssetRepairStage.None;
        private bool _running;
        private bool _cancelRequested;
        private bool _disposed;

        public AssetRepairService(IAssetPackage package)
            : this(new AssetPackageRepairTarget(package), AssetRepairPlanner.Shared)
        {
        }

        public AssetRepairService(IAssetRepairTarget target, AssetRepairPlanner planner = null)
        {
            _target = target ?? throw new ArgumentNullException(nameof(target));
            _planner = planner ?? AssetRepairPlanner.Shared;
        }

        public string PackageName => _target.PackageName;
        public Observable<(AssetRepairEvent, object)> RepairEvents => _repairEvents;

        public void Cancel()
        {
            CancelActiveDownloader(markCancelRequested: true, publishStage: true);
        }

        public UniTask<AssetRepairRunResult> RepairAsync(
            ContentTrustManifest manifest,
            IReadOnlyList<ContentTrustVerificationResult> failures,
            AssetRepairOptions options = default,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_running)
            {
                throw new InvalidOperationException("Asset repair service is already running.");
            }

            AssetRepairPlan plan = _planner.CreatePlan(PackageName, manifest, failures, _locationWorkspace);
            return RepairAsync(plan, options, cancellationToken);
        }

        public async UniTask<AssetRepairRunResult> RepairAsync(
            AssetRepairPlan plan,
            AssetRepairOptions options = default,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();
            if (_running)
            {
                throw new InvalidOperationException("Asset repair service is already running.");
            }

            _running = true;
            _activeDownloader = null;
            _currentStage = AssetRepairStage.None;
            _cancelRequested = false;
            options = options.Normalized();
            IDownloader downloader = null;

            try
            {
                SetStage(AssetRepairStage.Plan);
                ThrowIfCancellationRequested();
                PublishEvent(AssetRepairEvent.PlanCreated, new AssetRepairPlanCreatedEventArgs(plan));
                ThrowIfCancellationRequested();

                if (!plan.HasFailures)
                {
                    return Complete(plan, options, AssetRepairRunStatus.NoRepairNeeded, 0, 0L, 0, 0UL, default, null);
                }

                if (!plan.HasRepairableLocations)
                {
                    return Complete(plan, options, AssetRepairRunStatus.NoRepairableLocations, 0, 0L, 0, 0UL, default, "No repairable content locations were found.");
                }

                if (options.ClearUnusedCacheBeforeDownload)
                {
                    SetStage(AssetRepairStage.ClearCache);
                    bool cacheCleared = await _target.ClearCacheFilesAsync(ClearCacheMode.Unused, cancellationToken: cancellationToken);
                    ThrowIfCancellationRequested();
                    if (!cacheCleared)
                    {
                        return Complete(plan, options, AssetRepairRunStatus.Failed, 0, 0L, 0, 0UL, default, "Failed to clear unused cache before repair.");
                    }
                }

                SetStage(AssetRepairStage.Download);
                downloader = _target.CreateDownloaderForLocations(
                    plan.RepairLocations,
                    options.RecursiveDownloadLocations,
                    options.DownloadOptions.MaxConcurrentDownloads,
                    options.DownloadOptions.FailedRetryCount);
                if (downloader == null)
                {
                    return Complete(plan, options, AssetRepairRunStatus.Failed, 0, 0L, 0, 0UL, default, "Repair downloader was not created.");
                }

                _activeDownloader = downloader;
                await RunDownloaderAsync(downloader, cancellationToken);
                ThrowIfCancellationRequested();
                if (!downloader.Succeed)
                {
                    return Complete(plan, options, AssetRepairRunStatus.Failed, downloader.TotalDownloadCount, downloader.TotalDownloadBytes, 0, 0UL, default, downloader.Error);
                }

                int postRepairTrustFailureCount = 0;
                ulong trustFingerprint = 0UL;
                ContentTrustVerificationResult firstPostRepairFailure = default;
                if (options.VerifyAfterRepair && options.TrustOptions.Enabled)
                {
                    SetStage(AssetRepairStage.VerifyContentTrust);
                    ThrowIfCancellationRequested();
                    postRepairTrustFailureCount = options.TrustOptions.Verifier.VerifyManifestFiles(
                        options.TrustOptions.RootDirectory,
                        in options.TrustOptions.Manifest,
                        options.TrustOptions.FailureBuffer,
                        options.TrustOptions.SignatureVerifier);
                    ThrowIfCancellationRequested();
                    trustFingerprint = options.TrustOptions.Manifest.ComputeFingerprint();
                    if (postRepairTrustFailureCount > 0 && options.TrustOptions.FailureBuffer != null && options.TrustOptions.FailureBuffer.Count > 0)
                    {
                        firstPostRepairFailure = options.TrustOptions.FailureBuffer[0];
                    }

                    if (postRepairTrustFailureCount > 0)
                    {
                        return Complete(
                            plan,
                            options,
                            AssetRepairRunStatus.Failed,
                            downloader.TotalDownloadCount,
                            downloader.TotalDownloadBytes,
                            postRepairTrustFailureCount,
                            trustFingerprint,
                            firstPostRepairFailure,
                            "Content trust verification still failed after repair.");
                    }
                }

                return Complete(
                    plan,
                    options,
                    AssetRepairRunStatus.Succeeded,
                    downloader.TotalDownloadCount,
                    downloader.TotalDownloadBytes,
                    postRepairTrustFailureCount,
                    trustFingerprint,
                    firstPostRepairFailure,
                    null);
            }
            catch (OperationCanceledException ex)
            {
                CancelActiveDownloader(markCancelRequested: true, publishStage: false);
                return Complete(
                    plan,
                    options,
                    AssetRepairRunStatus.Cancelled,
                    downloader?.TotalDownloadCount ?? 0,
                    downloader?.TotalDownloadBytes ?? 0L,
                    0,
                    0UL,
                    default,
                    string.IsNullOrEmpty(ex.Message) ? "Asset repair operation was cancelled." : ex.Message);
            }
            catch (Exception ex)
            {
                CancelActiveDownloader(markCancelRequested: false, publishStage: false);
                SetStage(AssetRepairStage.Failed);
                AssetRepairRunResult result = CreateResult(plan, options, AssetRepairRunStatus.Failed, 0, 0L, 0, 0UL, default, ex.Message);
                PublishEvent(AssetRepairEvent.RepairFailed, result);
                return result;
            }
            finally
            {
                _activeDownloader = null;
                _running = false;
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            CancelActiveDownloader(markCancelRequested: true, publishStage: false);
            _disposed = true;
            _repairEvents.Dispose();
        }

        private async UniTask RunDownloaderAsync(IDownloader downloader, CancellationToken cancellationToken)
        {
            downloader.Begin();

            var progressArgs = new DownloadProgressEventArgs();
            while (!downloader.IsDone)
            {
                ThrowIfCancellationRequested();
                cancellationToken.ThrowIfCancellationRequested();

                progressArgs.TotalDownloadCount = downloader.TotalDownloadCount;
                progressArgs.CurrentDownloadCount = downloader.CurrentDownloadCount;
                progressArgs.TotalDownloadSizeBytes = downloader.TotalDownloadBytes;
                progressArgs.CurrentDownloadSizeBytes = downloader.CurrentDownloadBytes;

                PublishEvent(AssetRepairEvent.DownloadProgress, progressArgs);
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }

            ThrowIfCancellationRequested();
        }

        private AssetRepairRunResult Complete(
            AssetRepairPlan plan,
            AssetRepairOptions options,
            AssetRepairRunStatus status,
            int totalDownloadCount,
            long totalDownloadBytes,
            int postRepairTrustFailureCount,
            ulong trustFingerprint,
            ContentTrustVerificationResult firstPostRepairFailure,
            string error)
        {
            SetStage(GetTerminalStage(status));
            AssetRepairRunResult result = CreateResult(
                plan,
                options,
                status,
                totalDownloadCount,
                totalDownloadBytes,
                postRepairTrustFailureCount,
                trustFingerprint,
                firstPostRepairFailure,
                error);
            PublishEvent(IsFailureStatus(status) ? AssetRepairEvent.RepairFailed : AssetRepairEvent.RepairCompleted, result);
            return result;
        }

        private static AssetRepairStage GetTerminalStage(AssetRepairRunStatus status)
        {
            switch (status)
            {
                case AssetRepairRunStatus.Failed:
                    return AssetRepairStage.Failed;
                case AssetRepairRunStatus.Cancelled:
                    return AssetRepairStage.Cancelled;
                default:
                    return AssetRepairStage.Done;
            }
        }

        private static bool IsFailureStatus(AssetRepairRunStatus status)
        {
            return status == AssetRepairRunStatus.Failed ||
                   status == AssetRepairRunStatus.Cancelled;
        }

        private static AssetRepairRunResult CreateResult(
            AssetRepairPlan plan,
            AssetRepairOptions options,
            AssetRepairRunStatus status,
            int totalDownloadCount,
            long totalDownloadBytes,
            int postRepairTrustFailureCount,
            ulong trustFingerprint,
            ContentTrustVerificationResult firstPostRepairFailure,
            string error)
        {
            return new AssetRepairRunResult(
                plan.PackageName,
                plan.PackageVersion,
                status,
                plan.TotalFailureCount,
                plan.RepairableFailureCount,
                plan.UnrepairableFailureCount,
                plan.RepairLocationCount,
                totalDownloadCount,
                totalDownloadBytes,
                options.TrustOptions.Enabled,
                postRepairTrustFailureCount,
                trustFingerprint,
                firstPostRepairFailure,
                error);
        }

        private void SetStage(AssetRepairStage stage)
        {
            if (_currentStage == stage)
            {
                return;
            }

            _currentStage = stage;
            PublishEvent(AssetRepairEvent.StageChanged, stage);
        }

        private void PublishEvent(AssetRepairEvent repairEvent, object args)
        {
            if (_disposed)
            {
                return;
            }

            _repairEvents.OnNext((repairEvent, args));
        }

        private void CancelActiveDownloader(bool markCancelRequested, bool publishStage)
        {
            if (markCancelRequested)
            {
                _cancelRequested = true;
            }

            _activeDownloader?.Cancel();
            if (publishStage && CanTransitionToCancelled(_currentStage))
            {
                SetStage(AssetRepairStage.Cancelled);
            }
        }

        private void ThrowIfCancellationRequested()
        {
            if (_cancelRequested)
            {
                throw new OperationCanceledException("Asset repair operation was cancelled.");
            }
        }

        private static bool CanTransitionToCancelled(AssetRepairStage stage)
        {
            return stage != AssetRepairStage.None &&
                   stage != AssetRepairStage.Done &&
                   stage != AssetRepairStage.Failed &&
                   stage != AssetRepairStage.Cancelled;
        }

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(AssetRepairService));
            }
        }
    }
}
