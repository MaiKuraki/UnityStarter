using Cysharp.Threading.Tasks;
using System;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class ResourcesAssetPackage : IAssetPackage
    {
        private readonly string packageName;
        private int nextId = 1;

        public string Name => packageName;

        public ResourcesAssetPackage(string name)
        {
            packageName = name;
        }

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            // Resources don't require package-level initialization.
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync()
        {
            // No-op
            return UniTask.CompletedTask;
        }

        public UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult("N/A");
        }

        public UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<bool>(new NotImplementedException("Resources does not support manifest updates."));
        }

        public UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.ClearAll, object tags = null, CancellationToken cancellationToken = default)
        {
            // No-op, Resources are built-in and have no cache to clear.
            return UniTask.FromResult(true);
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotImplementedException("Resources does not support downloading.");
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotImplementedException("Resources does not support downloading.");
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotImplementedException("Resources does not support downloading.");
        }

        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Resources does not support pre-downloading."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Resources does not support pre-downloading."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Resources does not support pre-downloading."));
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var asset = Resources.Load<TAsset>(location);
            var handle = new ResourcesAssetHandle<TAsset>(RegisterHandle(out int id), id, asset);
            HandleTracker.Register(id, packageName, $"AssetSync {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            // Use LoadAsync to better align with the async nature of the interface.
            var request = Resources.LoadAsync<TAsset>(location);
            var handle = new ResourcesAssetHandle<TAsset>(RegisterHandle(out int id), id, request);
            HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            // CancellationToken is ignored as Resources.LoadAll is synchronous.
            var assets = Resources.LoadAll<TAsset>(location);
            var handle = new ResourcesAllAssetsHandle<TAsset>(RegisterHandle(out int id), id, assets);
            HandleTracker.Register(id, packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle.Asset)
            {
                return GameObject.Instantiate(handle.Asset, parent, worldPositionStays);
            }
            return null;
        }

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            GameObject instance = null;
            if (handle.Asset)
            {
                instance = GameObject.Instantiate(handle.Asset, parent, worldPositionStays);
                instance.SetActive(setActive);
            }
            var wrapped = new ResourcesInstantiateHandle(RegisterHandle(out int id), id, instance);
            HandleTracker.Register(id, packageName, $"InstantiateAsync : {handle.AssetObject.name}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            throw new NotImplementedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            throw new NotImplementedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            return UniTask.FromException(new NotImplementedException("Unloading scenes from Resources is not supported via this API."));
        }

        public async UniTask UnloadUnusedAssetsAsync()
        {
            await Resources.UnloadUnusedAssets();
        }

        private Action<int> RegisterHandle(out int id)
        {
            id = Interlocked.Increment(ref nextId);
            return UnregisterHandle;
        }

        private void UnregisterHandle(int id)
        {
            // No-op
        }
    }
}
