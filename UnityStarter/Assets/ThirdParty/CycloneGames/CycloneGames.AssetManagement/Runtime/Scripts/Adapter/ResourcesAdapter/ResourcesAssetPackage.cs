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
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync()
        {
            return UniTask.CompletedTask;
        }

        public UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult("N/A");
        }

        public UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<bool>(new NotSupportedException("Resources does not support manifest updates."));
        }

        public UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object tags = null, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(true);
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotSupportedException("Resources does not support downloading.");
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotSupportedException("Resources does not support downloading.");
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotSupportedException("Resources does not support downloading.");
        }

        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Resources does not support pre-downloading."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Resources does not support pre-downloading."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Resources does not support pre-downloading."));
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var asset = Resources.Load<TAsset>(location);
            var id = RegisterHandle();
            var handle = ResourcesAssetHandle<TAsset>.Create(id, asset);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AssetSync {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var request = Resources.LoadAsync<TAsset>(location);
            var id = RegisterHandle();
            var handle = ResourcesAssetHandle<TAsset>.Create(id, request, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var assets = Resources.LoadAll<TAsset>(location);
            var id = RegisterHandle();
            var handle = ResourcesAllAssetsHandle<TAsset>.Create(id, assets);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            return handle;
        }

        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle?.Asset != null)
            {
                return GameObject.Instantiate(handle.Asset, parent, worldPositionStays);
            }
            return null;
        }

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            GameObject instance = null;
            if (handle?.Asset != null)
            {
                instance = GameObject.Instantiate(handle.Asset, parent, worldPositionStays);
                if (instance != null) instance.SetActive(setActive);
            }
            var id = RegisterHandle();
            var wrapped = ResourcesInstantiateHandle.Create(id, instance);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"InstantiateAsync : {handle?.AssetObject?.name ?? "null"}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            throw new NotSupportedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            throw new NotSupportedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            return UniTask.FromException(new NotSupportedException("Unloading scenes from Resources is not supported via this API."));
        }

        public UniTask UnloadUnusedAssetsAsync()
        {
            Debug.LogWarning("[ResourcesAssetPackage] UnloadUnusedAssetsAsync is not recommended for Resources. Assets loaded from Resources cannot be unloaded individually and this call can cause performance hitches.");
            return UniTask.CompletedTask;
        }

        private int RegisterHandle()
        {
            return Interlocked.Increment(ref nextId);
        }
    }
}
