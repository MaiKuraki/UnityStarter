#if ADDRESSABLES_PRESENT
using System;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class AddressablesAssetPackage : IAssetPackage
    {
        private readonly string packageName;
        private int nextId = 1;

        public string Name => packageName;

        public AddressablesAssetPackage(string name)
        {
            packageName = name;
        }

        public Task<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            // Addressables initializes globally, so package-level initialization is a no-op.
            return Task.FromResult(true);
        }

        public Task DestroyAsync()
        {
            // Addressables doesn't have a package-level destroy concept.
            return Task.CompletedTask;
        }

        public Task<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            // Addressables does not have a direct equivalent to YooAsset's package versioning.
            // This could be implemented with custom catalog versioning if needed.
            return Task.FromResult(string.Empty);
        }

        public Task<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            // This corresponds to Addressables.UpdateCatalogs
            // For simplicity, we'll assume auto-updates or manual updates via Unity's tools.
            // A more complex implementation could be added here.
            return Task.FromResult(true);
        }

        public Task<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.ClearAll, object clearParam = null, CancellationToken cancellationToken = default)
        {
            // Addressables' caching is global. Caching.ClearCache() clears everything.
            // There's no built-in way to clear by tag or only unused assets.
            if (clearMode == ClearCacheMode.ClearByTags)
            {
                Debug.LogWarning("[AddressablesAssetPackage] ClearCacheFilesAsync by tags is not supported by Addressables. All cache will be cleared.");
            }
            
            return Task.FromResult(Caching.ClearCache());
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotImplementedException("Addressables does not support creating a downloader for 'all' assets in this manner. Use tags or locations.");
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            if (tags == null || tags.Length == 0) return new AddressableDownloader(default(AsyncOperationHandle));
            AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(tags, Addressables.MergeMode.Union);
            return new AddressableDownloader(handle);
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            if (locations == null || locations.Length == 0) return new AddressableDownloader(default(AsyncOperationHandle));
            AsyncOperationHandle handle = Addressables.DownloadDependenciesAsync(locations, Addressables.MergeMode.Union);
            return new AddressableDownloader(handle);
        }

        public Task<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Addressables does not support pre-downloading for a specific, non-active catalog version in this direct way.");
        }

        public Task<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Addressables does not support pre-downloading for a specific, non-active catalog version in this direct way.");
        }

        public Task<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            throw new NotImplementedException("Addressables does not support pre-downloading for a specific, non-active catalog version in this direct way.");
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetAsync<TAsset>(location);
            var wrapped = new AddressableAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, packageName, $"AssetSync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetAsync<TAsset>(location);
            // Note: Addressables handles don't directly take a CancellationToken.
            // Cancellation can be managed by releasing the handle, which cancels the underlying operation.
            var wrapped = new AddressableAssetHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetsAsync<TAsset>(location, null);
            var wrapped = new AddressableAllAssetsHandle<TAsset>(RegisterHandle(out int id), id, handle);
            HandleTracker.Register(id, packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle is AddressableAssetHandle<GameObject> h && h.Raw.IsDone)
            {
                return GameObject.Instantiate(h.Asset, parent, worldPositionStays);
            }
            return null;
        }

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            var op = Addressables.InstantiateAsync(handle.AssetObject, parent, worldPositionStays, setActive);
            var wrapped = new AddressableInstantiateHandle(RegisterHandle(out int id), id, op);
            HandleTracker.Register(id, packageName, $"InstantiateAsync : {handle.AssetObject.name}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            var op = Addressables.LoadSceneAsync(sceneLocation, loadMode, activateOnLoad, priority);
            var h = new AddressableSceneHandle(RegisterHandle(out int id), id, op);
            HandleTracker.Register(id, packageName, $"SceneAsync : {sceneLocation}");
            return h;
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            var op = Addressables.LoadSceneAsync(sceneLocation, loadMode, true);
            var h = new AddressableSceneHandle(RegisterHandle(out int id), id, op);
            HandleTracker.Register(id, packageName, $"SceneSync : {sceneLocation}");
            return h;
        }

        public async Task UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is AddressableSceneHandle sh && sh.Raw.IsValid())
            {
                await Addressables.UnloadSceneAsync(sh.Raw).Task;
            }
        }

        public async Task UnloadUnusedAssetsAsync()
        {
            var op = Resources.UnloadUnusedAssets();
            while (!op.isDone) await Task.Yield();
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
#endif // ADDRESSABLES_PRESENT
