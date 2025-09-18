#if YOOASSET_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetPackage : IAssetPackage
    {
        internal readonly ResourcePackage Raw;
        public string Name => Raw.PackageName;
        private int nextId = 1;

        public YooAssetPackage(ResourcePackage raw)
        {
            Raw = raw;
        }

        public async UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            if (options.ProviderOptions is not InitializeParameters yooOptions)
            {
                return false;
            }
            var op = Raw.InitializeAsync(yooOptions);
            await op;
            return op.Status == EOperationStatus.Succeed;
        }

        public UniTask DestroyAsync()
        {
            // NOTE: YooAsset's package does not have an async destroy method.
            // If extensive cleanup is needed, it should be implemented here.
            return UniTask.CompletedTask;
        }

        public async UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = Raw.RequestPackageVersionAsync(appendTimeTicks, timeoutSeconds);
            await op;
            return op.PackageVersion;
        }

        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = Raw.UpdatePackageManifestAsync(packageVersion, timeoutSeconds);
            await op;
            return op.Status == EOperationStatus.Succeed;
        }

        public async UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.ClearAll, object tags = null, CancellationToken cancellationToken = default)
        {
            ClearCacheFilesOperation op;
            switch (clearMode)
            {
                case ClearCacheMode.ClearAll:
                    op = Raw.ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles);
                    break;
                case ClearCacheMode.ClearUnused:
                    op = Raw.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                    break;
                case ClearCacheMode.ClearByTags:
                    if (tags is string[] or System.Collections.Generic.List<string>)
                    {
                        op = Raw.ClearCacheFilesAsync(EFileClearMode.ClearBundleFilesByTags, tags);
                    }
                    else
                    {
                        Debug.LogError("[YooAssetPackage] ClearByTags requires a string array or List<string> parameter.");
                        return false;
                    }
                    break;
                default:
                    throw new ArgumentOutOfRangeException(nameof(clearMode), clearMode, null);
            }
            
            await op;
            return op.Status == EOperationStatus.Succeed;
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var handle = Raw.LoadAssetSync<TAsset>(location);
            var wrapped = new YooAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"AssetSync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Raw.LoadAssetAsync<TAsset>(location);
            // YooAsset handles do not natively support CancellationToken.
            // We could implement a wrapper task to poll for cancellation, but for now, we'll just pass it down conceptually.
            var wrapped = new YooAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }
        
        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Raw.LoadAllAssetsAsync<TAsset>(location);
            // YooAsset handles do not natively support CancellationToken.
            var wrapped = new YooAllAssetsHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"AllAssets {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            var handle = Raw.LoadSceneSync(sceneLocation, loadMode);
            var wrapped = new YooSceneHandle(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"SceneSync : {sceneLocation}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            var handle = Raw.LoadSceneAsync(sceneLocation, loadMode, suspendLoad: !activateOnLoad, priority: (uint)priority);
            var wrapped = new YooSceneHandle(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"SceneAsync : {sceneLocation}");
            return wrapped;
        }

        public async UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is YooSceneHandle yooHandle)
            {
                await yooHandle.Raw.UnloadAsync();
            }
        }
        
        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            var op = Raw.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain);
            return new YooDownloader(op);
        }
        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            var op = Raw.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain);
            return new YooDownloader(op);
        }
        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            var op = Raw.CreateBundleDownloader(locations, recursiveDownload, downloadingMaxNumber, failedTryAgain);
            return new YooDownloader(op);
        }
        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            // NOTE: The pre-download flow described in the README (download before manifest switch)
            // is not directly supported by a single method in YooAsset 2.3.16's public API.
            // A more complex implementation involving temporary packages or manifest manipulation would be required.
            return UniTask.FromException<IDownloader>(new NotImplementedException("Pre-downloading is not implemented for the YooAsset provider."));
        }
        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Pre-downloading is not implemented for the YooAsset provider."));
        }
        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Pre-downloading is not implemented for the YooAsset provider."));
        }
        
        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle is YooAssetHandle<GameObject> h && h.Raw.IsDone)
            {
                return GameObject.Instantiate(h.Asset, parent, worldPositionStays);
            }
            return null;
        }
        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            var yooHandle = handle as YooAssetHandle<GameObject>;
            if (yooHandle == null)
            {
                Debug.LogError("Invalid handle type passed to InstantiateAsync.");
                return null;
            }
            
            var op = yooHandle.Raw.InstantiateAsync(parent, worldPositionStays);
            var wrapped = new YooInstantiateHandle(RegisterHandle(out int id), id, op);
            HandleTracker.Register(id, Name, $"InstantiateAsync : {yooHandle.Raw.GetAssetInfo().AssetPath}");
            return wrapped;
        }
        public async UniTask UnloadUnusedAssetsAsync()
        {
            await Raw.UnloadUnusedAssetsAsync();
        }
        
        private Action<int> RegisterHandle(out int id)
        {
            id = Interlocked.Increment(ref nextId);
            return UnregisterHandle;
        }

        private void UnregisterHandle(int id)
        {
            // Can be used for internal tracking if needed
        }
    }
}
#endif
