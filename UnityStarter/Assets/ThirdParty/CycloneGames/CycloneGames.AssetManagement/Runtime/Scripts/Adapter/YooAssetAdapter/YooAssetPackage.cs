#if YOOASSET_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetPackage : IAssetPackage
    {
        private readonly ResourcePackage _rawPackage;
        public string Name => _rawPackage.PackageName;
        private int _nextId = 1;

        private readonly Cache.AssetCacheService _cacheService;

        public YooAssetPackage(ResourcePackage rawPackage)
        {
            _rawPackage = rawPackage;
            _cacheService = new Cache.AssetCacheService(this);
        }

        public async UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            if (options.ProviderOptions is not InitializeParameters yooOptions)
            {
                CLogger.LogError("[YooAssetPackage] Invalid provider options provided for initialization.");
                return false;
            }
            var op = _rawPackage.InitializeAsync(yooOptions);
            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public UniTask DestroyAsync()
        {
            _cacheService.Dispose();
            YooAssets.RemovePackage(Name);
            return UniTask.CompletedTask;
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
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset));
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
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset));
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAssetHandle<TAsset>)cached;

            var handle = _rawPackage.LoadAssetAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAssetHandle<TAsset>.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AssetAsync {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
            return wrapped;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset));
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAllAssetsHandle<TAsset>)cached;

            var handle = _rawPackage.LoadAllAssetsAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAllAssetsHandle<TAsset>.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AllAssets {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
            return wrapped;
        }

        public IRawFileHandle LoadRawFileSync(string location, string bucket = null, string tag = null, string owner = null)
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, null);
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
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, null);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IRawFileHandle)cached;

            var handle = _rawPackage.LoadRawFileAsync(location);
            var id = RegisterHandle();
            var wrapped = YooRawFileHandle.Create(id, cacheKey, handle, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"RawFileAsync : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, wrapped);
            return wrapped;
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, string bucket = null)
        {
            var handle = _rawPackage.LoadSceneSync(sceneLocation, loadMode);
            var id = RegisterHandle();
            // SceneHandle is not cached; pass null key.
            var wrapped = YooSceneHandle.Create(id, handle, (_, h) => ((YooSceneHandle)h).DisposeInternal());
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"SceneSync : {sceneLocation}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100, string bucket = null)
        {
            var handle = _rawPackage.LoadSceneAsync(sceneLocation, loadMode, suspendLoad: !activateOnLoad, priority: (uint)priority);
            var id = RegisterHandle();
            // SceneHandle is not cached; pass null key.
            var wrapped = YooSceneHandle.Create(id, handle, (_, h) => ((YooSceneHandle)h).DisposeInternal());
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"SceneAsync : {sceneLocation}");
            return wrapped;
        }

        public async UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is YooSceneHandle yooHandle)
            {
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

        private async UniTask<IDownloader> CreatePreDownloaderInternal(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken, string[] tags = null)
        {
            var updateOp = _rawPackage.UpdatePackageManifestAsync(packageVersion, 30);
            await updateOp.WithCancellation(cancellationToken);

            if (updateOp.Status != EOperationStatus.Succeed)
            {
                CLogger.LogError($"[YooAssetPackage] Failed to update manifest for pre-downloading version {packageVersion}. Error: {updateOp.Error}");
                return null;
            }

            var downloaderOp = tags == null
                ? _rawPackage.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain)
                : _rawPackage.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain);

            return YooDownloader.Create(downloaderOp);
        }

        public async UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return await CreatePreDownloaderInternal(packageVersion, downloadingMaxNumber, failedTryAgain, cancellationToken);
        }

        public async UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return await CreatePreDownloaderInternal(packageVersion, downloadingMaxNumber, failedTryAgain, cancellationToken, tags);
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Pre-downloading by locations is not supported by the YooAsset provider."));
        }

        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle is YooAssetHandle<GameObject> yooHandle && yooHandle.Raw.IsDone)
            {
                return yooHandle.Raw.InstantiateSync(parent, worldPositionStays);
            }
            CLogger.LogError("[YooAssetPackage] InstantiateSync failed: Handle is not valid or not complete.");
            return null;
        }
        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            if (handle is not YooAssetHandle<GameObject> yooHandle)
            {
                CLogger.LogError("[YooAssetPackage] Invalid handle type passed to InstantiateAsync.");
                return null;
            }

            var op = yooHandle.Raw.InstantiateAsync(parent, worldPositionStays);
            var id = RegisterHandle();
            // InstantiateHandle is not cached; pass null key.
            var wrapped = YooInstantiateHandle.Create(id, op, (_, h) => ((YooInstantiateHandle)h).DisposeInternal());
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"InstantiateAsync : {yooHandle.Raw.GetAssetInfo().AssetPath}");
            return wrapped;
        }
        public async UniTask UnloadUnusedAssetsAsync()
        {
            _cacheService.ClearAll();
            await _rawPackage.UnloadUnusedAssetsAsync();
        }

        public void ClearBucket(string bucket)
        {
            _cacheService.ClearBucket(bucket);
        }

        private int RegisterHandle()
        {
            return Interlocked.Increment(ref _nextId);
        }
    }
}
#endif