#if CYCLONEGAMES_HAS_YOOASSET
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using Cysharp.Threading.Tasks;
using YooAsset;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetPackage : IAssetPackage, IAssetCatalogQuery, IAssetRuntimeDiagnostics, IAssetPatchProviderReconciler
    {
        private readonly ResourcePackage _rawPackage;
        public string Name => _rawPackage.PackageName;
        private int _nextId = 1;

        private readonly Cache.AssetCacheService _cacheService;
        private static readonly AssetPatchProviderReconciliationCapabilities ReconciliationCapabilities =
            new AssetPatchProviderReconciliationCapabilities(
                "YooAsset",
                supportsVersionedManifestUpdate: true,
                supportsExplicitCacheCleanup: true,
                supportsUnusedCacheCleanup: true,
                supportsTagScopedCacheCleanup: true,
                supportsProviderManagedDownloadCache: true,
                supportsIsolatedVersionPreDownload: false,
                requiresMainThreadAccess: true);

        // Cached delegates to avoid per-call lambda allocation for non-cached handle types.
        private static readonly Action<string, IReferenceCounted> _sceneReleaseCallback =
            (_, h) => ((YooSceneHandle)h).DisposeInternal();
        private static readonly Action<string, IReferenceCounted> _instantiateReleaseCallback =
            (_, h) => ((YooInstantiateHandle)h).DisposeInternal();

        public AssetPatchProviderReconciliationCapabilities Capabilities => ReconciliationCapabilities;

        public YooAssetPackage(ResourcePackage rawPackage)
        {
            _rawPackage = rawPackage;
            _cacheService = new Cache.AssetCacheService(this);
        }

        public UniTask<AssetPatchProviderReconciliationResult> ReconcileAsync(
            AssetPatchJournalRecord record,
            AssetPatchRecoveryRecommendation recommendation,
            CancellationToken cancellationToken = default)
        {
            cancellationToken.ThrowIfCancellationRequested();

            switch (recommendation.Action)
            {
                case AssetPatchRecoveryAction.None:
                    return UniTask.FromResult(
                        AssetPatchProviderReconciliationResult.NoActionRequired(
                            ReconciliationCapabilities,
                            "YooAsset has no provider-side recovery work for this journal."));
                case AssetPatchRecoveryAction.ResumeOrRestartDownload:
                case AssetPatchRecoveryAction.VerifyContentTrust:
                case AssetPatchRecoveryAction.RepairContent:
                    return UniTask.FromResult(
                        AssetPatchProviderReconciliationResult.ReadyToRestartPatch(
                            ReconciliationCapabilities,
                            "YooAsset owns download cache validation; recreate the patch downloader against the active manifest and keep the journal until the retry completes."));
                case AssetPatchRecoveryAction.RollbackManifest:
                    return UniTask.FromResult(
                        AssetPatchProviderReconciliationResult.RequiresOwnerAction(
                            ReconciliationCapabilities,
                            "YooAsset supports versioned manifest updates, but rollback must be explicitly enabled by recovery policy or performed by the product owner."));
                case AssetPatchRecoveryAction.ClearCacheAndRetry:
                case AssetPatchRecoveryAction.InspectTerminalFailure:
                default:
                    return UniTask.FromResult(
                        AssetPatchProviderReconciliationResult.RequiresOwnerAction(
                            ReconciliationCapabilities,
                            "Inspect the YooAsset patch journal and choose whether to clear cache, roll back the manifest, or restart the patch."));
            }
        }

        public async UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            if (options.IdleMemoryBudgetBytesOverride.HasValue)
                _cacheService.SetIdleMemoryBudget(options.IdleMemoryBudgetBytesOverride.Value);

            if (options.ProviderOptions is not InitializeParameters yooOptions)
            {
                CLogger.LogError("[YooAssetPackage] Invalid provider options provided for initialization.");
                return false;
            }

            // Bundle-loading concurrency precedence: explicit per-package override > user-provided value
            // > platform-aware default. This prevents YooAsset's int.MaxValue default from causing IO
            // thrash and memory spikes on mobile/WebGL when the caller did not tune it.
            if (options.BundleLoadingMaxConcurrencyOverride.HasValue)
            {
                yooOptions.BundleLoadingMaxConcurrency = Math.Max(1, options.BundleLoadingMaxConcurrencyOverride.Value);
            }
            else if (yooOptions.BundleLoadingMaxConcurrency <= 0 || yooOptions.BundleLoadingMaxConcurrency == int.MaxValue)
            {
                yooOptions.BundleLoadingMaxConcurrency = AssetPlatformDefaults.BundleLoadingMaxConcurrency;
            }

            var op = _rawPackage.InitializeAsync(yooOptions);
            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public async UniTask DestroyAsync()
        {
            _cacheService.Dispose();
            var op = _rawPackage.DestroyAsync();
            await op.WithCancellation(CancellationToken.None);
            YooAssets.RemovePackage(Name);
        }

        public async UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = _rawPackage.RequestPackageVersionAsync(appendTimeTicks, timeoutSeconds);
            await op.WithCancellation(cancellationToken);
            return op.PackageVersion;
        }

        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = _rawPackage.UpdatePackageManifestAsync(packageVersion, timeoutSeconds);
            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public async UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object tags = null, CancellationToken cancellationToken = default)
        {
            ClearCacheFilesOperation op;
            switch (clearMode)
            {
                case ClearCacheMode.All:
                    op = _rawPackage.ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles);
                    break;
                case ClearCacheMode.Unused:
                    op = _rawPackage.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                    break;
                case ClearCacheMode.ByTags:
                    if (tags is string[] or System.Collections.Generic.List<string>)
                    {
                        op = _rawPackage.ClearCacheFilesAsync(EFileClearMode.ClearBundleFilesByTags, tags);
                    }
                    else
                    {
                        CLogger.LogError("[YooAssetPackage] ClearByTags requires a string array or List<string> parameter.");
                        return false;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(clearMode), clearMode, null);
            }

            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string bucket = null, string tag = null, string owner = null) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), Cache.AssetCacheOperationKind.Asset);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAssetHandle<TAsset>)cached;

            var handle = _rawPackage.LoadAssetSync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAssetHandle<TAsset>.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, CancellationToken.None);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AssetSync {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
            return wrapped;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), Cache.AssetCacheOperationKind.Asset);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAssetHandle<TAsset>)cached;

            var handle = _rawPackage.LoadAssetAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAssetHandle<TAsset>.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AssetAsync {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, location);
#endif
            return wrapped;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), Cache.AssetCacheOperationKind.AllAssets);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAllAssetsHandle<TAsset>)cached;

            var handle = _rawPackage.LoadAllAssetsAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAllAssetsHandle<TAsset>.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AllAssets {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, location);
#endif
            return wrapped;
        }

        public IRawFileHandle LoadRawFileSync(string location, string bucket = null, string tag = null, string owner = null)
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, null, Cache.AssetCacheOperationKind.RawFile);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IRawFileHandle)cached;

            var handle = _rawPackage.LoadRawFileSync(location);
            var id = RegisterHandle();
            var wrapped = YooRawFileHandle.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, CancellationToken.None);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"RawFileSync : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
            return wrapped;
        }

        public IRawFileHandle LoadRawFileAsync(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default)
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, null, Cache.AssetCacheOperationKind.RawFile);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IRawFileHandle)cached;

            var handle = _rawPackage.LoadRawFileAsync(location);
            var id = RegisterHandle();
            var wrapped = YooRawFileHandle.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"RawFileAsync : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, location);
#endif
            return wrapped;
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, string bucket = null)
        {
            var handle = _rawPackage.LoadSceneSync(sceneLocation, loadMode);
            var id = RegisterHandle();
            // SceneHandle is not cached; pass null key.
            var wrapped = YooSceneHandle.Create(id, handle, activateOnLoad: true, isActivated: true, _sceneReleaseCallback);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"SceneSync : {sceneLocation}");
            SceneTracker.Register(id, Name, "YooAsset", sceneLocation, bucket, loadMode, wrapped);
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode, SceneActivationMode activationMode, int priority = 100, string bucket = null)
        {
            return LoadSceneAsync(sceneLocation, loadMode, activationMode == SceneActivationMode.ActivateOnLoad, priority, bucket);
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100, string bucket = null)
        {
            var handle = _rawPackage.LoadSceneAsync(sceneLocation, loadMode, suspendLoad: !activateOnLoad, priority: (uint)priority);
            var id = RegisterHandle();
            // SceneHandle is not cached; pass null key.
            var wrapped = YooSceneHandle.Create(id, handle, activateOnLoad, isActivated: false, _sceneReleaseCallback);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"SceneAsync : {sceneLocation}");
            SceneTracker.Register(id, Name, "YooAsset", sceneLocation, bucket, loadMode, wrapped);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, sceneLocation);
#endif
            return wrapped;
        }

        public async UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is YooSceneHandle yooHandle)
            {
                SceneTracker.MarkUnloadRequested(yooHandle.DebugId);
                var raw = yooHandle.Raw;
                // Null out Raw BEFORE awaiting to prevent DisposeInternal from calling UnloadAsync a second time.
                yooHandle.Raw = null;
                if (raw != null)
                {
                    await raw.UnloadAsync();
                    // Release the YooAsset provider reference after the scene is fully unloaded.
                    // Without this, Provider.RefCount never reaches 0, preventing YooAsset's
                    // ResourceManager from destroying the provider and releasing its bundles.
                    if (raw.IsValid) raw.Dispose();
                }
                yooHandle.DisposeInternal();
            }
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            var op = _rawPackage.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain);
            return YooDownloader.Create(op);
        }
        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            var op = _rawPackage.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain);
            return YooDownloader.Create(op);
        }
        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            var op = _rawPackage.CreateBundleDownloader(locations, recursiveDownload, downloadingMaxNumber, failedTryAgain);
            return YooDownloader.Create(op);
        }

        private UniTask<IDownloader> CreatePreDownloaderNotSupported(string packageVersion)
        {
            return UniTask.FromException<IDownloader>(
                new NotSupportedException(
                    $"YooAsset provider does not support isolated pre-downloading for manifest version '{packageVersion}' without mutating the active manifest. " +
                    "Use UpdatePackageManifestAsync(...) explicitly before creating normal downloaders, or keep the current manifest active and avoid version-specific pre-download requests."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return CreatePreDownloaderNotSupported(packageVersion);
        }

        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return CreatePreDownloaderNotSupported(packageVersion);
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return CreatePreDownloaderNotSupported(packageVersion);
        }

        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle is YooAssetHandle<GameObject> yooHandle && yooHandle.Raw != null && yooHandle.Raw.IsDone)
            {
                return yooHandle.Raw.InstantiateSync(parent, worldPositionStays);
            }
            CLogger.LogError("[YooAssetPackage] InstantiateSync failed: Handle is not valid or not complete.");
            return null;
        }
        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            if (handle is not YooAssetHandle<GameObject> yooHandle || yooHandle.Raw == null)
            {
                CLogger.LogError("[YooAssetPackage] Invalid or disposed handle passed to InstantiateAsync.");
                return null;
            }

            var op = yooHandle.Raw.InstantiateAsync(parent, worldPositionStays);
            var id = RegisterHandle();
            // InstantiateHandle is not cached; pass null key.
            var wrapped = YooInstantiateHandle.Create(id, op, _instantiateReleaseCallback);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"InstantiateAsync : {yooHandle.Raw.GetAssetInfo().AssetPath}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, yooHandle.Raw.GetAssetInfo().AssetPath);
#endif
            return wrapped;
        }
        public async UniTask UnloadUnusedAssetsAsync()
        {
            _cacheService.ClearAll();
            await _rawPackage.UnloadUnusedAssetsAsync();
        }

        public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), Cache.AssetCacheOperationKind.Asset);
            return _cacheService.Contains(cacheKey);
        }

        public AssetRuntimeCacheSnapshot GetRuntimeCacheSnapshot()
        {
            return _cacheService.CreateRuntimeSnapshot(Name, "YooAsset");
        }

        public UniTask<bool> TryGetAssetLocationsByTagAsync(string tag, List<string> results, CancellationToken cancellationToken = default)
        {
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            cancellationToken.ThrowIfCancellationRequested();

            results.Clear();
            if (string.IsNullOrEmpty(tag))
            {
                return UniTask.FromResult(false);
            }

            AssetInfo[] assetInfos;
            try
            {
                assetInfos = _rawPackage.GetAssetInfos(tag);
            }
            catch (Exception ex)
            {
                CLogger.LogWarning($"[YooAssetPackage] Failed to query asset locations by tag '{tag}': {ex.Message}");
                return UniTask.FromResult(false);
            }

            if (assetInfos == null || assetInfos.Length == 0)
            {
                return UniTask.FromResult(false);
            }

            for (int i = 0; i < assetInfos.Length; i++)
            {
                AssetInfo assetInfo = assetInfos[i];
                if (assetInfo == null || assetInfo.IsInvalid)
                {
                    continue;
                }

                string location = assetInfo.Address;
                if (string.IsNullOrEmpty(location))
                {
                    location = assetInfo.AssetPath;
                }

                if (!string.IsNullOrEmpty(location))
                {
                    AssetCatalogQueryUtils.AddUniqueLocation(results, location);
                }
            }

            return UniTask.FromResult(results.Count > 0);
        }

        public void SetCacheIdleMemoryBudget(long maxIdleBytes)
        {
            _cacheService.SetIdleMemoryBudget(maxIdleBytes);
        }

        public int TrimIdleCache(AssetCacheRetentionPolicy policy)
        {
            return _cacheService.TrimIdle(policy);
        }

        public void ClearBucket(string bucket)
        {
            _cacheService.ClearBucket(bucket);
        }

        public void ClearBucketsByPrefix(string bucketPrefix)
        {
            _cacheService.ClearBucketsByPrefix(bucketPrefix);
        }

        private int RegisterHandle()
        {
            return Interlocked.Increment(ref _nextId);
        }
    }
}
#endif
