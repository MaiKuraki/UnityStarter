using System;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using CycloneGames.AssetManagement;

namespace CycloneGames.UIFramework.Samples
{
    public sealed class AddressablesAssetHandle<T> : IAssetHandle<T> where T : UnityEngine.Object
    {
        private AsyncOperationHandle<T> _handle;

        public AddressablesAssetHandle(AsyncOperationHandle<T> handle) { _handle = handle; }

        public bool IsDone => !_handle.IsValid() || _handle.IsDone;
        public float Progress => _handle.IsValid() ? _handle.PercentComplete : 0f;
        public string Error => !_handle.IsValid() ? "Invalid Handle" :
          (_handle.Status == AsyncOperationStatus.Failed ? (_handle.OperationException?.Message ?? "Failed") : string.Empty);

        public T Asset => _handle.IsValid() && _handle.Status == AsyncOperationStatus.Succeeded ? _handle.Result : null;
        public UnityEngine.Object AssetObject => Asset;

        public void WaitForAsyncComplete() { /* Addressables does not support blocking wait; no-op */ }

        public void Dispose()
        {
            if (_handle.IsValid()) Addressables.Release(_handle);
        }
    }

    public sealed class AddressablesPackage : IAssetPackage
    {
        public string Name => "AddressablesDefault";

        public Task<bool> InitializeAsync(AssetPackageInitOptions options, System.Threading.CancellationToken ct = default)
        {
            // Optionally: await Addressables.InitializeAsync() here; minimal sample returns true.
            return Task.FromResult(true);
        }

        public Task DestroyAsync() => Task.CompletedTask;

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            // UIFramework does not use sync loading; if needed, call Addressables.LoadAssetAsync(...).WaitForCompletion().
            throw new NotSupportedException("Sync load is not supported in this minimal adapter.");
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var h = Addressables.LoadAssetAsync<TAsset>(location);
            return new AddressablesAssetHandle<TAsset>(h);
        }

        // Remaining members are unused by UIFramework in this sample; throw NotSupported or implement as needed.
        public CycloneGames.AssetManagement.IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location) where TAsset : UnityEngine.Object => throw new NotSupportedException();
        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false) => throw new NotSupportedException();
        public CycloneGames.AssetManagement.IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true) => throw new NotSupportedException();
        public CycloneGames.AssetManagement.ISceneHandle LoadSceneSync(string sceneLocation, UnityEngine.SceneManagement.LoadSceneMode loadMode = UnityEngine.SceneManagement.LoadSceneMode.Single) => throw new NotSupportedException();
        public CycloneGames.AssetManagement.ISceneHandle LoadSceneAsync(string sceneLocation, UnityEngine.SceneManagement.LoadSceneMode loadMode = UnityEngine.SceneManagement.LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100) => throw new NotSupportedException();
        public Task UnloadSceneAsync(CycloneGames.AssetManagement.ISceneHandle sceneHandle) => throw new NotSupportedException();
        public Task UnloadUnusedAssetsAsync() => Task.CompletedTask;

        // Download / pre-download related APIs; implement if needed.
        public Task<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<string>(null);
        public Task<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
        public Task<bool> ClearCacheFilesAsync(string clearMode, object clearParam = null, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult(false);
        public CycloneGames.AssetManagement.IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60) => null;
        public CycloneGames.AssetManagement.IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60) => null;
        public CycloneGames.AssetManagement.IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60) => null;
        public Task<CycloneGames.AssetManagement.IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<CycloneGames.AssetManagement.IDownloader>(null);
        public Task<CycloneGames.AssetManagement.IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<CycloneGames.AssetManagement.IDownloader>(null);
        public Task<CycloneGames.AssetManagement.IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, int timeoutSeconds = 60, System.Threading.CancellationToken cancellationToken = default) => Task.FromResult<CycloneGames.AssetManagement.IDownloader>(null);
    }
}