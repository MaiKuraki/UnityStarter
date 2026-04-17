using CycloneGames.Logger;
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

        private readonly Cache.AssetCacheService _cacheService;

        // Cached delegate to avoid per-call lambda allocation for non-cached handle types.
        private static readonly Action<string, IReferenceCounted> _instantiateReleaseCallback =
            (_, h) => ((ResourcesInstantiateHandle)h).DisposeInternal();

        public ResourcesAssetPackage(string name)
        {
            packageName = name;
            _cacheService = new Cache.AssetCacheService(this);
        }

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync()
        {
            _cacheService.Dispose();
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

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string bucket = null, string tag = null, string owner = null) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset));
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAssetHandle<TAsset>)cached;

            var asset = Resources.Load<TAsset>(location);
            var id = RegisterHandle();
            var handle = ResourcesAssetHandle<TAsset>.Create(id, cacheKey, asset, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AssetSync {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, handle);
            return handle;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset));
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAssetHandle<TAsset>)cached;

            var request = Resources.LoadAsync<TAsset>(location);
            var id = RegisterHandle();
            var handle = ResourcesAssetHandle<TAsset>.Create(id, cacheKey, request, _cacheService.OnHandleReleased, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, handle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(handle, location);
#endif
            return handle;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset));
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return (IAllAssetsHandle<TAsset>)cached;

            var assets = Resources.LoadAll<TAsset>(location);
            var id = RegisterHandle();
            var handle = ResourcesAllAssetsHandle<TAsset>.Create(id, cacheKey, assets, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            _cacheService.RegisterNew(cacheKey, bucket, tag, owner, handle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(handle, location);
#endif
            return handle;
        }

        public IRawFileHandle LoadRawFileSync(string location, string bucket = null, string tag = null, string owner = null)
        {
            throw new NotSupportedException("Resources does not support RawFile loading. Use LoadAssetAsync<TextAsset> for text files.");
        }

        public IRawFileHandle LoadRawFileAsync(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default)
        {
            throw new NotSupportedException("Resources does not support RawFile loading. Use LoadAssetAsync<TextAsset> for text files.");
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
            // InstantiateHandle is not cached; pass null key.
            var wrapped = ResourcesInstantiateHandle.Create(id, instance, _instantiateReleaseCallback);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"InstantiateAsync : {handle?.AssetObject?.name ?? "null"}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, handle?.AssetObject?.name ?? "unknown");
#endif
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100, string bucket = null)
        {
            throw new NotSupportedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode, SceneActivationMode activationMode, int priority = 100, string bucket = null)
        {
            throw new NotSupportedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, string bucket = null)
        {
            throw new NotSupportedException("Loading scenes from Resources is not supported via this API. Use Unity's SceneManager directly.");
        }

        public UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            return UniTask.FromException(new NotSupportedException("Unloading scenes from Resources is not supported via this API."));
        }

        public async UniTask UnloadUnusedAssetsAsync()
        {
            _cacheService.ClearAll();
            CLogger.LogWarning("[ResourcesAssetPackage] UnloadUnusedAssetsAsync triggers Resources.UnloadUnusedAssets(). This can cause hitches on the main thread, so prefer explicit handle release and bucket clears whenever possible.");
            await Resources.UnloadUnusedAssets().ToUniTask();
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
            return Interlocked.Increment(ref nextId);
        }
    }
}
