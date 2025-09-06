#if ADDRESSABLES_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace CycloneGames.AssetManagement.Runtime
{
    // --- Handle Implementations ---

    internal abstract class AddressablesOperationHandle : IOperation
    {
        protected readonly Action<int> Unregister;
        protected readonly int Id;
        public abstract bool IsDone { get; }
        public abstract float Progress { get; }
        public abstract string Error { get; }
        public abstract System.Threading.Tasks.Task Task { get; }
        public abstract void WaitForAsyncComplete();

        protected AddressablesOperationHandle(Action<int> unregister, int id)
        {
            Unregister = unregister;
            Id = id;
        }
    }

    internal sealed class AddressableAssetHandle<TAsset> : AddressablesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        internal readonly AsyncOperationHandle<TAsset> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;
        public override System.Threading.Tasks.Task Task => Raw.Task;
        public TAsset Asset => Raw.Result;
        public UnityEngine.Object AssetObject => Raw.Result;

        public AddressableAssetHandle(Action<int> unregister, int id, AsyncOperationHandle<TAsset> raw) : base(unregister, id)
        {
            Raw = raw;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();
        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
            Addressables.Release(Raw);
        }
    }

    internal sealed class AddressableAllAssetsHandle<TAsset> : AddressablesOperationHandle, IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private readonly AsyncOperationHandle<IList<TAsset>> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;
        public override System.Threading.Tasks.Task Task => raw.Task;
        public IReadOnlyList<TAsset> Assets => (IReadOnlyList<TAsset>)raw.Result;

        public AddressableAllAssetsHandle(Action<int> unregister, int id, AsyncOperationHandle<IList<TAsset>> raw) : base(unregister, id)
        {
            this.raw = raw;
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();
        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
            Addressables.Release(raw);
        }
    }

    internal sealed class AddressableInstantiateHandle : AddressablesOperationHandle, IInstantiateHandle
    {
        private readonly AsyncOperationHandle<GameObject> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;
        public override System.Threading.Tasks.Task Task => raw.Task;
        public GameObject Instance => raw.Result;

        public AddressableInstantiateHandle(Action<int> unregister, int id, AsyncOperationHandle<GameObject> raw) : base(unregister, id)
        {
            this.raw = raw;
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();
        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            Unregister(Id);
            Addressables.Release(raw);
        }
    }

    internal sealed class AddressableSceneHandle : AddressablesOperationHandle, ISceneHandle
    {
        internal readonly AsyncOperationHandle<SceneInstance> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;
        public override System.Threading.Tasks.Task Task => Raw.Task;
        public string ScenePath { get; } // Addressables doesn't easily expose the path.
        public Scene Scene => Raw.Result.Scene;

        public AddressableSceneHandle(Action<int> unregister, int id, AsyncOperationHandle<SceneInstance> raw) : base(unregister, id)
        {
            Raw = raw;
            ScenePath = raw.DebugName;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();
    }

    internal sealed class AddressableDownloader : IDownloader
    {
        private readonly AsyncOperationHandle raw;
        public bool IsDone => raw.IsDone;
        public bool Succeed => raw.Status == AsyncOperationStatus.Succeeded;
        public float Progress => raw.PercentComplete;
        public int TotalDownloadCount => 0; // Not easily available
        public int CurrentDownloadCount => 0; // Not easily available
        public long TotalDownloadBytes => raw.GetDownloadStatus().TotalBytes;
        public long CurrentDownloadBytes => raw.GetDownloadStatus().DownloadedBytes;
        public string Error => raw.OperationException?.Message;

        public AddressableDownloader(AsyncOperationHandle raw)
        {
            this.raw = raw;
        }

        public void Begin() { }
        public Task StartAsync(CancellationToken cancellationToken = default) => raw.Task;
        public void Pause() { } // Not supported
        public void Resume() { } // Not supported
        public void Cancel() { } // Not supported
        public void Combine(IDownloader other) => throw new NotImplementedException();
    }
}
#endif
