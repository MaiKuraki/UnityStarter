#if ADDRESSABLES_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;

namespace CycloneGames.AssetManagement.Runtime
{
    internal abstract class AddressablesOperationHandle : IOperation
    {
        protected readonly int Id;
        public abstract bool IsDone { get; }
        public abstract float Progress { get; }
        public abstract string Error { get; }
        public abstract UniTask Task { get; }
        public abstract void WaitForAsyncComplete();

        protected AddressablesOperationHandle(int id)
        {
            Id = id;
        }
    }

    internal sealed class AddressableAssetHandle<TAsset> : AddressablesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        internal readonly AsyncOperationHandle<TAsset> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;
        public override UniTask Task { get; }
        public TAsset Asset => Raw.Result;
        public UnityEngine.Object AssetObject => Raw.Result;

        public AddressableAssetHandle(int id, AsyncOperationHandle<TAsset> raw, CancellationToken cancellationToken) : base(id)
        {
            Raw = raw;
            Task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();
        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            if (Raw.IsValid()) Addressables.Release(Raw);
        }
    }

    internal sealed class AddressableAllAssetsHandle<TAsset> : AddressablesOperationHandle, IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private readonly AsyncOperationHandle<IList<TAsset>> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;
        public override UniTask Task { get; }
        public IReadOnlyList<TAsset> Assets => (IReadOnlyList<TAsset>)raw.Result;

        public AddressableAllAssetsHandle(int id, AsyncOperationHandle<IList<TAsset>> raw, CancellationToken cancellationToken) : base(id)
        {
            this.raw = raw;
            Task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();
        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            if (raw.IsValid()) Addressables.Release(raw);
        }
    }

    internal sealed class AddressableInstantiateHandle : AddressablesOperationHandle, IInstantiateHandle
    {
        private readonly AsyncOperationHandle<GameObject> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;
        public override UniTask Task { get; }
        public GameObject Instance => raw.Result;

        public AddressableInstantiateHandle(int id, AsyncOperationHandle<GameObject> raw, CancellationToken cancellationToken) : base(id)
        {
            this.raw = raw;
            Task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();
        public void Dispose()
        {
            HandleTracker.Unregister(Id);
            if (raw.IsValid()) Addressables.Release(raw);
        }
    }
    
    internal sealed class FailedInstantiateHandle : IInstantiateHandle
    {
        public bool IsDone => true;
        public float Progress => 1f;
        public string Error { get; }
        public UniTask Task => UniTask.CompletedTask;
        public GameObject Instance => null;
        public FailedInstantiateHandle(string error) { Error = error; }
        public void WaitForAsyncComplete() { }
        public void Dispose() { }
    }

    internal sealed class AddressableSceneHandle : AddressablesOperationHandle, ISceneHandle
    {
        internal readonly AsyncOperationHandle<SceneInstance> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;
        public override UniTask Task { get; }
        public string ScenePath { get; }
        public Scene Scene => Raw.Result.Scene;

        public AddressableSceneHandle(int id, AsyncOperationHandle<SceneInstance> raw, CancellationToken cancellationToken) : base(id)
        {
            Raw = raw;
            ScenePath = raw.DebugName;
            Task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();
    }

    internal sealed class AddressableDownloader : IDownloader
    {
        private readonly AsyncOperationHandle raw;
        public bool IsDone => raw.IsDone;
        public bool Succeed => raw.Status == AsyncOperationStatus.Succeeded;
        public float Progress => raw.PercentComplete;
        public int TotalDownloadCount => 0; // Not available
        public int CurrentDownloadCount => 0; // Not available
        public long TotalDownloadBytes => raw.GetDownloadStatus().TotalBytes;
        public long CurrentDownloadBytes => raw.GetDownloadStatus().DownloadedBytes;
        public string Error => raw.OperationException?.Message;

        public AddressableDownloader(AsyncOperationHandle raw)
        {
            this.raw = raw;
        }

        public void Begin() { }
        public UniTask StartAsync(CancellationToken cancellationToken = default) => raw.ToUniTask(cancellationToken: cancellationToken);
        public void Pause() => Debug.LogWarning("[AddressableDownloader] Pause is not supported by Addressables.");
        public void Resume() => Debug.LogWarning("[AddressableDownloader] Resume is not supported by Addressables.");
        public void Cancel()
        {
            if (raw.IsValid())
            {
                Addressables.Release(raw);
            }
        }
        public void Combine(IDownloader other) => Debug.LogWarning("[AddressableDownloader] Combine is not supported by Addressables.");
    }
}
#endif
