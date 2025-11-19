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
        private readonly ResourcePackage _rawPackage;
        public string Name => _rawPackage.PackageName;
        private int nextId = 1;

        public YooAssetPackage(ResourcePackage rawPackage)
        {
            _rawPackage = rawPackage;
        }

        public async UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            if (options.ProviderOptions is not InitializeParameters yooOptions)
            {
                Debug.LogError("[YooAssetPackage] Invalid provider options provided for initialization.");
                return false;
            }
            var op = _rawPackage.InitializeAsync(yooOptions);
            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public UniTask DestroyAsync()
        {
            YooAssets.RemovePackage(Name);
            return UniTask.CompletedTask;
        }

        public async UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = _rawPackage.RequestPackageVersionAsync(appendTimeTicks, timeoutSeconds);
            await op.WithCancellation(cancellationToken);
            return op.PackageVersion;
        }

        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            var op = _rawPackage.UpdatePackageManifestAsync(packageVersion, timeoutSeconds);
            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public async UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object tags = null, CancellationToken cancellationToken = default)
        {
            ClearCacheFilesOperation op;
            switch (clearMode)
            {
                case ClearCacheMode.All:
                    op = _rawPackage.ClearCacheFilesAsync(EFileClearMode.ClearAllBundleFiles);
                    break;
                case ClearCacheMode.Unused:
                    op = _rawPackage.ClearCacheFilesAsync(EFileClearMode.ClearUnusedBundleFiles);
                    break;
                case ClearCacheMode.ByTags:
                    if (tags is string[] or System.Collections.Generic.List<string>)
                    {
                        op = _rawPackage.ClearCacheFilesAsync(EFileClearMode.ClearBundleFilesByTags, tags);
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
            
            await op.WithCancellation(cancellationToken);
            return op.Status == EOperationStatus.Succeed;
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            var handle = _rawPackage.LoadAssetSync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAssetHandle<TAsset>.Create(id, handle, CancellationToken.None);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AssetSync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = _rawPackage.LoadAssetAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAssetHandle<TAsset>.Create(id, handle, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }
        
        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = _rawPackage.LoadAllAssetsAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = YooAllAssetsHandle<TAsset>.Create(id, handle, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"AllAssets {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            var handle = _rawPackage.LoadSceneSync(sceneLocation, loadMode);
            var id = RegisterHandle();
            var wrapped = YooSceneHandle.Create(id, handle);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"SceneSync : {sceneLocation}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            var handle = _rawPackage.LoadSceneAsync(sceneLocation, loadMode, suspendLoad: !activateOnLoad, priority: (uint)priority);
            var id = RegisterHandle();
            var wrapped = YooSceneHandle.Create(id, handle);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"SceneAsync : {sceneLocation}");
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
            var op = _rawPackage.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain);
            return YooDownloader.Create(op);
        }
        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            var op = _rawPackage.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain);
            return YooDownloader.Create(op);
        }
        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            var op = _rawPackage.CreateBundleDownloader(locations, recursiveDownload, downloadingMaxNumber, failedTryAgain);
            return YooDownloader.Create(op);
        }
        
        private async UniTask<IDownloader> CreatePreDownloaderInternal(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken, string[] tags = null)
        {
            var updateOp = _rawPackage.UpdatePackageManifestAsync(packageVersion, 30);
            await updateOp.WithCancellation(cancellationToken);

            if (updateOp.Status != EOperationStatus.Succeed)
            {
                Debug.LogError($"[YooAssetPackage] Failed to update manifest for pre-downloading version {packageVersion}. Error: {updateOp.Error}");
                return null;
            }

            var downloaderOp = tags == null
                ? _rawPackage.CreateResourceDownloader(downloadingMaxNumber, failedTryAgain)
                : _rawPackage.CreateResourceDownloader(tags, downloadingMaxNumber, failedTryAgain);
            
            return YooDownloader.Create(downloaderOp);
        }

        public async UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return await CreatePreDownloaderInternal(packageVersion, downloadingMaxNumber, failedTryAgain, cancellationToken);
        }

        public async UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return await CreatePreDownloaderInternal(packageVersion, downloadingMaxNumber, failedTryAgain, cancellationToken, tags);
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotImplementedException("Pre-downloading by locations is not supported by the YooAsset provider."));
        }
        
        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            if (handle is YooAssetHandle<GameObject> yooHandle && yooHandle.Raw.IsDone)
            {
                return yooHandle.Raw.InstantiateSync(parent, worldPositionStays);
            }
            Debug.LogError("[YooAssetPackage] InstantiateSync failed: Handle is not valid or not complete.");
            return null;
        }
        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            if (handle is not YooAssetHandle<GameObject> yooHandle)
            {
                Debug.LogError("[YooAssetPackage] Invalid handle type passed to InstantiateAsync.");
                return null;
            }
            
            var op = yooHandle.Raw.InstantiateAsync(parent, worldPositionStays);
            var id = RegisterHandle();
            var wrapped = YooInstantiateHandle.Create(id, op);
            if (HandleTracker.Enabled) HandleTracker.Register(id, Name, $"InstantiateAsync : {yooHandle.Raw.GetAssetInfo().AssetPath}");
            return wrapped;
        }
        public async UniTask UnloadUnusedAssetsAsync()
        {
            await _rawPackage.UnloadUnusedAssetsAsync();
        }
        
        private int RegisterHandle()
        {
            return Interlocked.Increment(ref nextId);
        }
    }
}
#endif
