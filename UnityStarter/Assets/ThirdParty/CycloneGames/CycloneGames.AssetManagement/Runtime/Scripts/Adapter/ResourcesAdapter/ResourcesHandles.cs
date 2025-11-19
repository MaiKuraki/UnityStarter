using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class ResourcesHandlePool<T> where T : class, new()
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

    internal abstract class ResourcesOperationHandle : IOperation
    {
        protected int Id;
        public virtual bool IsDone => true;
        public virtual float Progress => 1f;
        public virtual string Error => string.Empty;
        public virtual UniTask Task => UniTask.CompletedTask;
        public virtual void WaitForAsyncComplete() { }

        protected ResourcesOperationHandle() { }
        protected void SetId(int id) => Id = id;
    }

    internal sealed class ResourcesAssetHandle<TAsset> : ResourcesOperationHandle, IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private ResourceRequest request;
        private TAsset syncAsset;
        private UniTask _task;

        public override bool IsDone => request?.isDone ?? true;
        public override float Progress => request?.progress ?? 1f;
        public override UniTask Task => _task;
        
        public TAsset Asset => syncAsset != null ? syncAsset : request?.asset as TAsset;
        public UnityEngine.Object AssetObject => Asset;

        public ResourcesAssetHandle() { }

        // Async handle init
        public void Initialize(int id, ResourceRequest request, System.Threading.CancellationToken cancellationToken)
        {
            SetId(id);
            this.request = request;
            this.syncAsset = null;
            this._task = request.ToUniTask(cancellationToken: cancellationToken);
        }
        
        // Sync handle init
        public void Initialize(int id, TAsset asset)
        {
            SetId(id);
            this.request = null;
            this.syncAsset = asset;
            this._task = UniTask.CompletedTask;
        }

        public static ResourcesAssetHandle<TAsset> Create(int id, ResourceRequest request, System.Threading.CancellationToken cancellationToken)
        {
            var h = ResourcesHandlePool<ResourcesAssetHandle<TAsset>>.Get();
            h.Initialize(id, request, cancellationToken);
            return h;
        }

        public static ResourcesAssetHandle<TAsset> Create(int id, TAsset asset)
        {
            var h = ResourcesHandlePool<ResourcesAssetHandle<TAsset>>.Get();
            h.Initialize(id, asset);
            return h;
        }

        public void Dispose()
        {
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            // Individual assets loaded from Resources cannot be unloaded.
            request = null;
            syncAsset = null;
            _task = default;
            ResourcesHandlePool<ResourcesAssetHandle<TAsset>>.Release(this);
        }
    }

    internal sealed class ResourcesAllAssetsHandle<TAsset> : ResourcesOperationHandle, IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private UniTask _task;
        
        public override bool IsDone => _task.Status.IsCompleted();
        public override float Progress => _task.Status.IsCompleted() ? 1f : 0f;
        public override UniTask Task => _task;
        
        public IReadOnlyList<TAsset> Assets { get; private set; }

        public ResourcesAllAssetsHandle() { }

        public void Initialize(int id, TAsset[] assets)
        {
            SetId(id);
            Assets = assets;
            _task = SimulateAsync(); // Assuming this simulation is still desired
        }

        public static ResourcesAllAssetsHandle<TAsset> Create(int id, TAsset[] assets)
        {
            var h = ResourcesHandlePool<ResourcesAllAssetsHandle<TAsset>>.Get();
            h.Initialize(id, assets);
            return h;
        }

        private async UniTask SimulateAsync()
        {
            await UniTask.Yield();
        }

        public void Dispose()
        {
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            Assets = null;
            _task = default;
            ResourcesHandlePool<ResourcesAllAssetsHandle<TAsset>>.Release(this);
        }
    }

    internal sealed class ResourcesInstantiateHandle : ResourcesOperationHandle, IInstantiateHandle
    {
        public GameObject Instance { get; private set; }

        public ResourcesInstantiateHandle() { }

        public void Initialize(int id, GameObject instance)
        {
            SetId(id);
            Instance = instance;
        }

        public static ResourcesInstantiateHandle Create(int id, GameObject instance)
        {
            var h = ResourcesHandlePool<ResourcesInstantiateHandle>.Get();
            h.Initialize(id, instance);
            return h;
        }

        public void Dispose()
        {
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            Instance = null;
            ResourcesHandlePool<ResourcesInstantiateHandle>.Release(this);
        }
    }
}
