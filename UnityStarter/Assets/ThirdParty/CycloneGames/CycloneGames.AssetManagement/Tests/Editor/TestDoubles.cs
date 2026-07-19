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
        private readonly UniTask _task;
        private readonly bool? _isDoneOverride;

        public TestOperation(
            float progress,
            string error = null,
            UniTask? task = null,
            bool? isDoneOverride = null)
        {
            Progress = progress;
            Error = error;
            _task = AssetOperationBroadcast.Create(task ?? (string.IsNullOrEmpty(error)
                ? UniTask.CompletedTask
                : UniTask.FromException(new System.InvalidOperationException(error))));
            _isDoneOverride = isDoneOverride;
        }

        public bool IsDone => _isDoneOverride ?? (_task.Status != UniTaskStatus.Pending);
        public float Progress { get; }
        public string Error { get; }
        public UniTask Task => _task;
        public void WaitForAsyncComplete() { }
    }

    internal sealed class TestAssetHandle<TAsset> : IAssetHandle<TAsset>, IReferenceCounted,
        IAssetMemoryFootprint where TAsset : Object
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
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => 1L;
    }

    internal sealed class TestAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset>, IReferenceCounted,
        IAssetMemoryFootprint where TAsset : Object
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
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => 1L;
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

    internal sealed class TestSceneHandle : ISceneHandle, IReferenceCounted, ISceneTrackerHandleState
    {
        public string ScenePathValue;
        public SceneActivationMode ActivationModeValue;
        public SceneActivationState ActivationStateValue;
        public bool SupportsManualActivationValue;
        public bool IsDoneValue;
        public float ProgressValue;
        public int RefCountValue = 1;
        public string ErrorValue = string.Empty;
        public bool ShouldRemoveFromSceneTrackerValue;

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
        public bool ShouldRemoveFromSceneTracker => ShouldRemoveFromSceneTrackerValue;
        public UniTask ActivateAsync(CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public void Retain() => RefCountValue++;
        public void Release() => RefCountValue--;
        public void Dispose() => Release();
        public void WaitForAsyncComplete() { }
    }

    internal sealed class RecordingAssetPackage : IAssetPackage, IAssetSyncOperations, IAssetBulkLoader,
        IAssetRawFileLoader, IAssetSceneLoader,
        IAssetStoragePreflight
    {
        public string NameValue = "Default";
        public string LastCall;
        public string LastLocation;
        public string LastBucket;
        public string LastTag;
        public string LastOwner;
        public LoadSceneMode LastLoadMode;
        public LocalPhysicsMode LastLocalPhysicsMode;
        public SceneActivationMode LastActivationMode;
        public bool LastActivateOnLoad;
        public int LastPriority;
        public int InitializeCallCount;
        public AssetCacheRetentionPolicy LastRetentionPolicy;
        public bool InitializeResult = true;
        public AssetStoragePreflightResult StoragePreflightResult = new AssetStoragePreflightResult(
            AssetStorageCapacityStatus.Available,
            long.MaxValue);

        public string Name => NameValue;
        public string PackageName => NameValue;

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            InitializeCallCount++;
            return UniTask.FromResult(InitializeResult);
        }

        public UniTask DestroyAsync() => UniTask.CompletedTask;
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

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true) => null;

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneParameters loadParameters, SceneActivationMode activationMode, int priority = 100, string bucket = null)
        {
            LastCall = "LoadSceneAsyncParameters";
            LastLocation = sceneLocation;
            LastLoadMode = loadParameters.loadSceneMode;
            LastLocalPhysicsMode = loadParameters.localPhysicsMode;
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
            LastLocalPhysicsMode = LocalPhysicsMode.None;
            LastActivateOnLoad = activateOnLoad;
            LastPriority = priority;
            LastBucket = bucket;
            return new TestSceneHandle();
        }

        public UniTask UnloadSceneAsync(ISceneHandle sceneHandle, CancellationToken cancellationToken = default) => UniTask.CompletedTask;
        public UniTask UnloadUnusedAssetsAsync() => UniTask.CompletedTask;

        public UniTask<AssetStoragePreflightResult> CheckStorageAsync(
            AssetStoragePreflightRequest request,
            CancellationToken cancellationToken = default)
        {
            return UniTask.FromResult(StoragePreflightResult);
        }

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
        public int RemovePackageCallCount;
        public bool CreatedPackageInitializeResult = true;
        public AssetManagementOptions LastOptions;

        public bool Initialized => InitializedValue;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            InitializeCallCount++;
            LastOptions = options;
            InitializedValue = true;
            return UniTask.CompletedTask;
        }

        public UniTask DestroyAsync()
        {
            InitializedValue = false;
            return UniTask.CompletedTask;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            CreatePackageCallCount++;
            CreatedPackage = new RecordingAssetPackage
            {
                NameValue = packageName,
                InitializeResult = CreatedPackageInitializeResult
            };
            return CreatedPackage;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            return CreatedPackage != null && CreatedPackage.Name == packageName ? CreatedPackage : null;
        }

        public UniTask<bool> RemovePackageAsync(string packageName)
        {
            RemovePackageCallCount++;
            if (CreatedPackage == null || CreatedPackage.Name != packageName)
            {
                return UniTask.FromResult(false);
            }

            CreatedPackage = null;
            return UniTask.FromResult(true);
        }
        public IReadOnlyList<string> GetAllPackageNames() => CreatedPackage == null ? System.Array.Empty<string>() : new[] { CreatedPackage.Name };
    }
}
