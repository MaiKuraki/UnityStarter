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
    internal static class AddressablesHandlePool<T> where T : class, new()
    {
        private static readonly Stack<T> _pool = new Stack<T>(32);

        public static T Get()
        {
            lock (_pool)
            {
                return _pool.Count > 0 ? _pool.Pop() : new T();
            }
        }

        public static void Release(T item)
        {
            if (item == null) return;
            lock (_pool)
            {
                _pool.Push(item);
            }
        }
    }

    internal abstract class AddressablesOperationHandle : IOperation
    {
        protected int Id;
        public abstract bool IsDone { get; }
        public abstract float Progress { get; }
        public abstract string Error { get; }
        public abstract UniTask Task { get; }
        public abstract void WaitForAsyncComplete();

        protected AddressablesOperationHandle() { }
        
        protected void SetId(int id) => Id = id;
    }

    internal sealed class AddressableAssetHandle<TAsset> : AddressablesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        internal AsyncOperationHandle<TAsset> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;
        
        private UniTask _task;
        public override UniTask Task => _task; 
        
        public TAsset Asset => Raw.Result;
        public UnityEngine.Object AssetObject => Raw.Result;

        public AddressableAssetHandle() { }

        public void Initialize(int id, AsyncOperationHandle<TAsset> raw, CancellationToken cancellationToken)
        {
            SetId(id);
            Raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public static AddressableAssetHandle<TAsset> Create(int id, AsyncOperationHandle<TAsset> raw, CancellationToken cancellationToken)
        {
            var h = AddressablesHandlePool<AddressableAssetHandle<TAsset>>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();
        public void Dispose()
        {
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            if (Raw.IsValid()) Addressables.Release(Raw);
            Raw = default;
            _task = default;
            AddressablesHandlePool<AddressableAssetHandle<TAsset>>.Release(this);
        }
    }

    internal sealed class AddressableAllAssetsHandle<TAsset> : AddressablesOperationHandle, IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private AsyncOperationHandle<IList<TAsset>> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;
        
        private UniTask _task;
        public override UniTask Task => _task;

        public IReadOnlyList<TAsset> Assets => (IReadOnlyList<TAsset>)raw.Result;

        public AddressableAllAssetsHandle() { }

        public void Initialize(int id, AsyncOperationHandle<IList<TAsset>> raw, CancellationToken cancellationToken)
        {
            SetId(id);
            this.raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public static AddressableAllAssetsHandle<TAsset> Create(int id, AsyncOperationHandle<IList<TAsset>> raw, CancellationToken cancellationToken)
        {
            var h = AddressablesHandlePool<AddressableAllAssetsHandle<TAsset>>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();
        public void Dispose()
        {
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            if (raw.IsValid()) Addressables.Release(raw);
            raw = default;
            _task = default;
            AddressablesHandlePool<AddressableAllAssetsHandle<TAsset>>.Release(this);
        }
    }

    internal sealed class AddressableInstantiateHandle : AddressablesOperationHandle, IInstantiateHandle
    {
        private AsyncOperationHandle<GameObject> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;
        
        private UniTask _task;
        public override UniTask Task => _task;
        
        public GameObject Instance => raw.Result;

        public AddressableInstantiateHandle() { }

        public void Initialize(int id, AsyncOperationHandle<GameObject> raw, CancellationToken cancellationToken)
        {
            SetId(id);
            this.raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public static AddressableInstantiateHandle Create(int id, AsyncOperationHandle<GameObject> raw, CancellationToken cancellationToken)
        {
            var h = AddressablesHandlePool<AddressableInstantiateHandle>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();
        public void Dispose()
        {
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            if (raw.IsValid()) Addressables.Release(raw);
            raw = default;
            _task = default;
            AddressablesHandlePool<AddressableInstantiateHandle>.Release(this);
        }
    }
    
    internal sealed class FailedInstantiateHandle : IInstantiateHandle
    {
        public bool IsDone => true;
        public float Progress => 1f;
        public string Error { get; private set; }
        public UniTask Task => UniTask.CompletedTask;
        public GameObject Instance => null;
        
        public FailedInstantiateHandle(string error) { Error = error; }
        public void WaitForAsyncComplete() { }
        public void Dispose() { }
    }

    internal sealed class AddressableSceneHandle : AddressablesOperationHandle, ISceneHandle
    {
        internal AsyncOperationHandle<SceneInstance> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;
        
        private UniTask _task;
        public override UniTask Task => _task;
        
        public string ScenePath { get; private set; }
        public Scene Scene => Raw.Result.Scene;

        public AddressableSceneHandle() { }

        public void Initialize(int id, AsyncOperationHandle<SceneInstance> raw, CancellationToken cancellationToken)
        {
            SetId(id);
            Raw = raw;
            ScenePath = raw.DebugName;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public static AddressableSceneHandle Create(int id, AsyncOperationHandle<SceneInstance> raw, CancellationToken cancellationToken)
        {
            var h = AddressablesHandlePool<AddressableSceneHandle>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();
        
        public void ReturnToPool()
        {
             if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
             Raw = default;
             ScenePath = null;
             _task = default;
             AddressablesHandlePool<AddressableSceneHandle>.Release(this);
        }
    }

    internal sealed class AddressableDownloader : IDownloader
    {
        private AsyncOperationHandle raw;
        public bool IsDone => raw.IsDone;
        public bool Succeed => raw.Status == AsyncOperationStatus.Succeeded;
        public float Progress => raw.PercentComplete;
        public int TotalDownloadCount => 0; 
        public int CurrentDownloadCount => 0; 
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
