#if YOOASSET_PRESENT
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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

        public async Task<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            if (options.ProviderOptions is not InitializeParameters yooOptions)
            {
                return false;
            }
            var op = Raw.InitializeAsync(yooOptions);
            await op.Task;
            return op.Status == EOperationStatus.Succeed;
        }

        public Task DestroyAsync()
        {
            return Task.CompletedTask;
        }

        public async Task<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = Raw.RequestPackageVersionAsync(appendTimeTicks, timeoutSeconds);
            await op.Task;
            return op.PackageVersion;
        }

        public async Task<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = Raw.UpdatePackageManifestAsync(packageVersion, timeoutSeconds);
            await op.Task;
            return op.Status == EOperationStatus.Succeed;
        }

        public async Task<bool> ClearCacheFilesAsync(string clearMode, object clearParam = null, CancellationToken cancellationToken = default)
        {
            // A more robust implementation would parse the clearMode string.
            var op = Raw.ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles);
            await op.Task;
            return op.Status == EOperationStatus.Succeed;
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var handle = Raw.LoadAssetSync<TAsset>(location);
            var wrapped = new YooAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"AssetSync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var handle = Raw.LoadAssetAsync<TAsset>(location);
            var wrapped = new YooAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, Name, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }
        
        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var handle = Raw.LoadAllAssetsAsync<TAsset>(location);
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

        public Task UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is YooSceneHandle yooHandle)
            {
                return yooHandle.Raw.UnloadAsync().Task;
            }
            return Task.CompletedTask;
        }
        
        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
        {
            var op = Raw.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain, timeoutSeconds);
            return new YooDownloader(op);
        }
        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
        {
            var op = Raw.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain, timeoutSeconds);
            return new YooDownloader(op);
        }
        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
        {
            var op = Raw.CreateBundleDownloader(locations, downloadingMaxNumber, failedTryAgain, timeoutSeconds);
            return new YooDownloader(op);
        }
        public Task<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("YooAsset's CreateResourcePreDownloader is not available in this context.");
        }
        public Task<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        public Task<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default) => throw new NotImplementedException();
        
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
            var op = (handle as YooAssetHandle<GameObject>)?.Raw.InstantiateAsync(parent, worldPositionStays);
            var wrapped = new YooInstantiateHandle(RegisterHandle(out int id), id, op);
            HandleTracker.Register(id, Name, $"InstantiateAsync : {handle.AssetObject.name}");
            return wrapped;
        }
        public Task UnloadUnusedAssetsAsync() => Raw.UnloadUnusedAssetsAsync().Task;
        
        private Action<int> RegisterHandle(out int id)
        {
            id = nextId++;
            return UnregisterHandle;
        }

        private void UnregisterHandle(int id)
        {
            // Can be used for internal tracking if needed
        }
    }
}
#endif