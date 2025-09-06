using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class ResourcesAssetPackage : IAssetPackage
    {
        private readonly string packageName;
        private int nextId = 1;

        public string Name => packageName;
        public bool IsAlive => true;

        public ResourcesAssetPackage(string name)
        {
            packageName = name;
        }

        public Task<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            // Resources don't require package-level initialization.
            return Task.FromResult(true);
        }

        public Task DestroyAsync()
        {
            // No-op
            return Task.CompletedTask;
        }

        public Task<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            return Task.FromResult("N/A");
        }

        public Task<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Resources does not support manifest updates.");
        }

        public Task<bool> ClearCacheFilesAsync(string clearMode, object clearParam = null, CancellationToken cancellationToken = default)
        {
            // No-op, Resources are built-in.
            return Task.FromResult(true);
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
        {
            throw new NotImplementedException("Resources does not support downloading.");
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
        {
            throw new NotImplementedException("Resources does not support downloading.");
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60)
        {
            throw new NotImplementedException("Resources does not support downloading.");
        }

        public Task<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Resources does not support pre-downloading.");
        }

        public Task<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Resources does not support pre-downloading.");
        }

        public Task<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Resources does not support pre-downloading.");
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var asset = Resources.Load<TAsset>(location);
            var handle = new ResourcesAssetHandle<TAsset>(RegisterHandle(out int id), id, asset);
            HandleTracker.Register(id, packageName, $"AssetSync {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            // Simulate async loading for Resources
            var asset = Resources.Load<TAsset>(location);
            var handle = new ResourcesAssetHandle<TAsset>(RegisterHandle(out int id), id, asset);
            HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
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

        public Task UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            throw new NotImplementedException("Unloading scenes from Resources is not supported via this API.");
        }

        public async Task UnloadUnusedAssetsAsync()
        {
            var op = Resources.UnloadUnusedAssets();
            while (!op.isDone) await Task.Yield();
        }

        private Action<int> RegisterHandle(out int id)
        {
            id = nextId++;
            return UnregisterHandle;
        }

        private void UnregisterHandle(int id)
        {
            // No-op
        }
    }
}
