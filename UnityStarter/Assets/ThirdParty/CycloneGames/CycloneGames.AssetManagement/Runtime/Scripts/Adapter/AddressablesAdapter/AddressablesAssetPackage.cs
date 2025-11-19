#if ADDRESSABLES_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Threading;
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

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            // Addressables initializes globally and automatically.
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync()
        {
            // Addressables does not have a package-level destroy concept.
            return UniTask.CompletedTask;
        }

        public UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            Debug.LogWarning("[AddressablesAssetPackage] Package versioning is not directly supported by Addressables.");
            return UniTask.FromResult(string.Empty);
        }

        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            // This corresponds to Addressables.UpdateCatalogs.
            var handle = Addressables.UpdateCatalogs();
            await handle.WithCancellation(cancellationToken);
            var success = handle.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(handle);
            return success;
        }

        public UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object tags = null, CancellationToken cancellationToken = default)
        {
            if (clearMode == ClearCacheMode.ByTags)
            {
                Debug.LogWarning("[AddressablesAssetPackage] ClearCacheFilesAsync by tags is not supported by Addressables. All cache will be cleared.");
            }
            return UniTask.FromResult(Caching.ClearCache());
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotSupportedException("Addressables does not support creating a downloader for 'all' assets. Use tags or locations.");
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            if (tags == null || tags.Length == 0) return new AddressableDownloader(default);
            var handle = Addressables.DownloadDependenciesAsync(tags);
            return new AddressableDownloader(handle);
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            if (locations == null || locations.Length == 0) return new AddressableDownloader(default);
            var handle = Addressables.DownloadDependenciesAsync(locations);
            return new AddressableDownloader(handle);
        }

        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Addressables does not support pre-downloading for a specific, non-active catalog version."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Addressables does not support pre-downloading for a specific, non-active catalog version."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Addressables does not support pre-downloading for a specific, non-active catalog version."));
        }

        [Obsolete("Synchronous asset loading is deprecated and can cause performance issues. Use LoadAssetAsync instead.", true)]
        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            throw new NotSupportedException("Synchronous asset loading is not supported by the Addressables provider.");
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = AddressableAssetHandle<TAsset>.Create(id, handle, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetsAsync<TAsset>(location, null);
            var id = RegisterHandle();
            var wrapped = AddressableAllAssetsHandle<TAsset>.Create(id, handle, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        [Obsolete("Synchronous instantiation is deprecated and can cause performance issues. Use InstantiateAsync instead.", true)]
        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            throw new NotSupportedException("Synchronous instantiation is not supported by the Addressables provider.");
        }

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            if (handle?.AssetObject == null)
            {
                return new FailedInstantiateHandle("Cannot instantiate from a null or invalid asset handle.");
            }
            
            var op = Addressables.InstantiateAsync(handle.AssetObject, parent, worldPositionStays, setActive);
            var id = RegisterHandle();
            // Pass CancellationToken.None as instantiation cancellation is typically handled by releasing the handle, which Addressables does automatically.
            var wrapped = AddressableInstantiateHandle.Create(id, op, CancellationToken.None);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"InstantiateAsync : {handle.AssetObject.name}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            var op = Addressables.LoadSceneAsync(sceneLocation, loadMode, activateOnLoad, priority);
            var id = RegisterHandle();
            var h = AddressableSceneHandle.Create(id, op, CancellationToken.None); // Scene loading cancellation is handled by unloading.
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"SceneAsync : {sceneLocation}");
            return h;
        }

        [Obsolete("Synchronous scene loading is deprecated and can cause performance issues. Use LoadSceneAsync instead.", true)]
        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            throw new NotSupportedException("Synchronous scene loading is not supported by the Addressables provider.");
        }

        public async UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is AddressableSceneHandle sh)
            {
                if (sh.Raw.IsValid())
                {
                    await Addressables.UnloadSceneAsync(sh.Raw);
                }
                // Return to pool manually since ISceneHandle is not IDisposable in this architecture
                sh.ReturnToPool();
            }
        }

        public UniTask UnloadUnusedAssetsAsync()
        {
            Debug.LogWarning("[AddressablesAssetPackage] UnloadUnusedAssetsAsync is not recommended. Please release individual asset handles via Dispose() for precise memory management.");
            return UniTask.CompletedTask;
        }

        private int RegisterHandle()
        {
            return Interlocked.Increment(ref nextId);
        }
    }
}
#endif
