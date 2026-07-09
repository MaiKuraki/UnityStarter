using System.Collections.Generic;
using System.Threading;

using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    internal sealed class TestOperation : IOperation
    {
        public TestOperation(float progress, string error = null)
        {
            Progress = progress;
            Error = error;
        }

        public bool IsDone => true;
        public float Progress { get; }
        public string Error { get; }
        public UniTask Task => UniTask.CompletedTask;
        public void WaitForAsyncComplete() { }
    }

    internal sealed class TestAssetHandle<TAsset> : IAssetHandle<TAsset>, IReferenceCounted where TAsset : Object
    {
        public TAsset Asset { get; set; }
        public Object AssetObject => Asset;
        public bool IsDone => true;
        public float Progress => 1f;
        public string Error { get; set; }
        public UniTask Task => UniTask.CompletedTask;
        public int RefCount { get; private set; } = 1;
        public void Retain() => RefCount++;
        public void Release() => RefCount--;
        public void Dispose() => Release();
        public void WaitForAsyncComplete() { }
    }

    internal sealed class TestAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset>, IReferenceCounted where TAsset : Object
    {
        public IReadOnlyList<TAsset> Assets { get; set; } = new List<TAsset>(0);
        public bool IsDone => true;
        public float Progress => 1f;
        public string Error { get; set; }
        public UniTask Task => UniTask.CompletedTask;
        public int RefCount { get; private set; } = 1;
        public void Retain() => RefCount++;
        public void Release() => RefCount--;
        public void Dispose() => Release();
        public void WaitForAsyncComplete() { }
    }

    internal sealed class TestRawFileHandle : IRawFileHandle, IReferenceCounted
    {
        public string FilePath { get; set; } = string.Empty;
        public bool IsDone => true;
        public float Progress => 1f;
        public string Error { get; set; }
        public UniTask Task => UniTask.CompletedTask;
        public int RefCount { get; private set; } = 1;
        public string ReadText() => string.Empty;
        public byte[] ReadBytes() => System.Array.Empty<byte>();
        public void Retain() => RefCount++;
        public void Release() => RefCount--;
        public void Dispose() => Release();
        public void WaitForAsyncComplete() { }
    }

    internal sealed class TestSceneHandle : ISceneHandle, IReferenceCounted
    {
        public string ScenePathValue;
        public SceneActivationMode ActivationModeValue;
        public SceneActivationState ActivationStateValue;
        public bool SupportsManualActivationValue;
        public bool IsDoneValue;
        public float ProgressValue;
        public int RefCountValue = 1;
        public string ErrorValue = string.Empty;

        public string ScenePath => ScenePathValue;
        public Scene Scene => default;
        public SceneActivationMode ActivationMode => ActivationModeValue;
        public SceneActivationState ActivationState => ActivationStateValue;
        public bool SupportsManualActivation => SupportsManualActivationValue;
        public bool IsDone => IsDoneValue;
        public float Progress => ProgressValue;
        public string Error => ErrorValue;
        public UniTask Task => UniTask.CompletedTask;
        public int RefCount => RefCountValue;
        public UniTask ActivateAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public void Retain() => RefCountValue++;
        public void Release() => RefCountValue--;
        public void Dispose() => Release();
        public void WaitForAsyncComplete() { }
    }

    internal sealed class RecordingAssetPackage : IAssetPackage
    {
        public string NameValue = "Default";
        public string LastCall;
        public string LastLocation;
        public string LastBucket;
        public string LastTag;
        public string LastOwner;
        public LoadSceneMode LastLoadMode;
        public SceneActivationMode LastActivationMode;
        public bool LastActivateOnLoad;
        public int LastPriority;
        public int InitializeCallCount;
        public AssetCacheRetentionPolicy LastRetentionPolicy;
        public string RequestPackageVersionValue = "1.0.0";
        public System.Exception RequestPackageVersionException;
        public System.Exception UpdatePackageManifestException;
        public System.Exception CreateDownloaderForAllException;
        public bool UpdatePackageManifestResult = true;
        public readonly List<string> UpdatedPackageVersions = new List<string>();
        public IDownloader DownloaderForAll;
        public IDownloader DownloaderForLocations;
        public int CreateDownloaderForAllCallCount;
        public int CreateDownloaderForLocationsCallCount;
        public int LastDownloadingMaxNumber;
        public int LastFailedTryAgain;
        public bool LastRecursiveDownload;
        public string[] LastLocations;
        public int ClearCacheFilesCallCount;
        public ClearCacheMode LastClearCacheMode;

        public string Name => NameValue;

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync() => UniTask.CompletedTask;
        public UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            if (RequestPackageVersionException != null)
            {
                return UniTask.FromException<string>(RequestPackageVersionException);
            }

            return UniTask.FromResult(RequestPackageVersionValue);
        }

        public UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            if (UpdatePackageManifestException != null)
            {
                return UniTask.FromException<bool>(UpdatePackageManifestException);
            }

            UpdatedPackageVersions.Add(packageVersion);
            return UniTask.FromResult(UpdatePackageManifestResult);
        }

        public UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object clearParam = null, CancellationToken cancellationToken = default)
        {
            ClearCacheFilesCallCount++;
            LastClearCacheMode = clearMode;
            return UniTask.FromResult(true);
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            CreateDownloaderForAllCallCount++;
            LastDownloadingMaxNumber = downloadingMaxNumber;
            LastFailedTryAgain = failedTryAgain;
            if (CreateDownloaderForAllException != null)
            {
                throw CreateDownloaderForAllException;
            }

            return DownloaderForAll;
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain) => null;

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            CreateDownloaderForLocationsCallCount++;
            LastLocations = locations;
            LastRecursiveDownload = recursiveDownload;
            LastDownloadingMaxNumber = downloadingMaxNumber;
            LastFailedTryAgain = failedTryAgain;
            return DownloaderForLocations;
        }
        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default) => UniTask.FromResult<IDownloader>(null);
        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default) => UniTask.FromResult<IDownloader>(null);
        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default) => UniTask.FromResult<IDownloader>(null);

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string bucket = null, string tag = null, string owner = null) where TAsset : Object
        {
            Record("LoadAssetSync", location, bucket, tag, owner);
            return new TestAssetHandle<TAsset>();
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : Object
        {
            Record("LoadAssetAsync", location, bucket, tag, owner);
            return new TestAssetHandle<TAsset>();
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : Object
        {
            Record("LoadAllAssetsAsync", location, bucket, tag, owner);
            return new TestAllAssetsHandle<TAsset>();
        }

        public IRawFileHandle LoadRawFileSync(string location, string bucket = null, string tag = null, string owner = null)
        {
            Record("LoadRawFileSync", location, bucket, tag, owner);
            return new TestRawFileHandle();
        }

        public IRawFileHandle LoadRawFileAsync(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default)
        {
            Record("LoadRawFileAsync", location, bucket, tag, owner);
            return new TestRawFileHandle();
        }

        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false) => null;
        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true) => null;

        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, string bucket = null)
        {
            LastCall = "LoadSceneSync";
            LastLocation = sceneLocation;
            LastLoadMode = loadMode;
            LastBucket = bucket;
            return new TestSceneHandle();
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode, SceneActivationMode activationMode, int priority = 100, string bucket = null)
        {
            LastCall = "LoadSceneAsyncManual";
            LastLocation = sceneLocation;
            LastLoadMode = loadMode;
            LastActivationMode = activationMode;
            LastPriority = priority;
            LastBucket = bucket;
            return new TestSceneHandle();
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100, string bucket = null)
        {
            LastCall = "LoadSceneAsyncBool";
            LastLocation = sceneLocation;
            LastLoadMode = loadMode;
            LastActivateOnLoad = activateOnLoad;
            LastPriority = priority;
            LastBucket = bucket;
            return new TestSceneHandle();
        }

        public UniTask UnloadSceneAsync(ISceneHandle sceneHandle) => UniTask.CompletedTask;
        public UniTask UnloadUnusedAssetsAsync() => UniTask.CompletedTask;

        public bool IsAssetCached<TAsset>(string location) where TAsset : Object => false;

        public void SetCacheIdleMemoryBudget(long maxIdleBytes) { }

        public int TrimIdleCache(AssetCacheRetentionPolicy policy)
        {
            LastCall = "TrimIdleCache";
            LastRetentionPolicy = policy;
            return 0;
        }

        public void ClearBucket(string bucket)
        {
            LastCall = "ClearBucket";
            LastBucket = bucket;
        }

        public void ClearBucketsByPrefix(string bucketPrefix)
        {
            LastCall = "ClearBucketsByPrefix";
            LastBucket = bucketPrefix;
        }

        private void Record(string call, string location, string bucket, string tag, string owner)
        {
            LastCall = call;
            LastLocation = location;
            LastBucket = bucket;
            LastTag = tag;
            LastOwner = owner;
        }
    }

    internal sealed class RecordingAssetModule : IAssetModule
    {
        public bool InitializedValue;
        public RecordingAssetPackage CreatedPackage;
        public int InitializeCallCount;
        public int CreatePackageCallCount;
        public AssetManagementOptions LastOptions;

        public bool Initialized => InitializedValue;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            InitializeCallCount++;
            LastOptions = options;
            InitializedValue = true;
            return UniTask.CompletedTask;
        }

        public UniTask DestroyAsync(CancellationToken cancellationToken = default)
        {
            InitializedValue = false;
            return UniTask.CompletedTask;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            CreatePackageCallCount++;
            CreatedPackage = new RecordingAssetPackage { NameValue = packageName };
            return CreatedPackage;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            return CreatedPackage != null && CreatedPackage.Name == packageName ? CreatedPackage : null;
        }

        public UniTask<bool> RemovePackageAsync(string packageName) => UniTask.FromResult(true);
        public IReadOnlyList<string> GetAllPackageNames() => CreatedPackage == null ? System.Array.Empty<string>() : new[] { CreatedPackage.Name };
        public IPatchService CreatePatchService(string packageName) => null;
    }
}
