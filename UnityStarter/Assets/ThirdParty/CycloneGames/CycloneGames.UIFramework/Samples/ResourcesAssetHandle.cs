using System;
using System.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    public sealed class ResourcesAssetHandle<T> : IAssetHandle<T> where T : UnityEngine.Object
    {
        private readonly ResourceRequest _request;

        public ResourcesAssetHandle(ResourceRequest request)
        {
            _request = request;
        }

        public bool IsDone => _request == null || _request.isDone;
        public float Progress => _request?.progress ?? 0f;
        public string Error => _request == null ? "Invalid Handle" : (_request.asset == null && _request.isDone ? "Asset not found" : string.Empty);

        public T Asset => _request?.asset as T;
        public UnityEngine.Object AssetObject => _request?.asset;

        public void WaitForAsyncComplete()
        {
            // This is tricky with ResourceRequest. We can't truly block here without a coroutine.
            // For the purpose of this framework, async is preferred, so this will be a no-op.
            // The caller should check IsDone in a loop if they need to block.
        }

        public void Dispose()
        {
            // Resources.UnloadAsset can be used, but it's often better to manage memory with UnloadUnusedAssets.
            // For this handle, we do nothing, mirroring the lightweight nature of Resources.
        }
    }

    public sealed class ResourcesPackage : IAssetPackage
    {
        public string Name => "Resources";

        public Task<bool> InitializeAsync(AssetPackageInitOptions options, System.Threading.CancellationToken ct = default)
        {
            // Resources doesn't need initialization.
            return Task.FromResult(true);
        }

        public Task DestroyAsync() => Task.CompletedTask;

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var asset = Resources.Load<TAsset>(location);
            throw new NotSupportedException("Sync load is not fully supported in this adapter. Please use LoadAssetAsync.");
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var request = Resources.LoadAsync<TAsset>(location);
            return new ResourcesAssetHandle<TAsset>(request);
        }

        // The rest of the interface is not used by the UI sample.
        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location) where TAsset : UnityEngine.Object => throw new NotSupportedException();
        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false) => throw new NotSupportedException();
        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true) => throw new NotSupportedException();
        public ISceneHandle LoadSceneSync(string sceneLocation, UnityEngine.SceneManagement.LoadSceneMode loadMode = UnityEngine.SceneManagement.LoadSceneMode.Single) => throw new NotSupportedException();
        public ISceneHandle LoadSceneAsync(string sceneLocation, UnityEngine.SceneManagement.LoadSceneMode loadMode = UnityEngine.SceneManagement.LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100) => throw new NotSupportedException();
        public Task UnloadSceneAsync(ISceneHandle sceneHandle) => throw new NotSupportedException();
        public Task UnloadUnusedAssetsAsync() => Task.CompletedTask;
        public Task<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<string>(null);
        public Task<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ClearCacheFilesAsync(string clearMode, object clearParam = null, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60) => null;
        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60) => null;
        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60) => null;
        public Task<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<IDownloader>(null);
        public Task<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<IDownloader>(null);
        public Task<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<IDownloader>(null);
    }
}
