using CycloneGames.Logger;
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class ResourcesHandlePool<T> where T : class, new()
    {
        private const int HARD_LIMIT = 256;
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
                if (_pool.Count < HARD_LIMIT) _pool.Push(item);
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

    internal sealed class ResourcesAssetHandle<TAsset> : ResourcesOperationHandle, IAssetHandle<TAsset>, IInternalCacheable where TAsset : UnityEngine.Object
    {
        private ResourceRequest _request;
        private TAsset _syncAsset;
        private UniTask _task;

        public override bool IsDone => _request?.isDone ?? true;
        public override float Progress => _request?.progress ?? 1f;
        public override UniTask Task => IsDone ? UniTask.CompletedTask : _task;

        public TAsset Asset => _syncAsset != null ? _syncAsset : _request?.asset as TAsset;
        public UnityEngine.Object AssetObject => Asset;

        private int _refCount;
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;
        private volatile bool _disposed;

        public ResourcesAssetHandle() { }

        public void Initialize(int id, string cacheKey, ResourceRequest request, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            _cacheKey = cacheKey;
            _request = request;
            _syncAsset = null;
            _task = request.ToUniTask(cancellationToken: cancellationToken);
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public void Initialize(int id, string cacheKey, TAsset asset, Action<string, IReferenceCounted> onReleaseToCache)
        {
            SetId(id);
            _cacheKey = cacheKey;
            _request = null;
            _syncAsset = asset;
            _task = UniTask.CompletedTask;
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static ResourcesAssetHandle<TAsset> Create(int id, string cacheKey, ResourceRequest request, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = ResourcesHandlePool<ResourcesAssetHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, request, onReleaseToCache, cancellationToken);
            return h;
        }

        public static ResourcesAssetHandle<TAsset> Create(int id, string cacheKey, TAsset asset, Action<string, IReferenceCounted> onReleaseToCache)
        {
            var h = ResourcesHandlePool<ResourcesAssetHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, asset, onReleaseToCache);
            return h;
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[ResourcesAssetHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[ResourcesAssetHandle] Release called more times than Retain. Refcount underflow prevented.");
                return;
            }
            if (newCount == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(_cacheKey, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            _disposed = true;
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            // Resources assets cannot be unloaded individually; only Resources.UnloadUnusedAssets() can reclaim them.
            _request = null;
            _syncAsset = null;
            _task = default;
            _cacheKey = null;
            _onReleaseToCache = null;
            ResourcesHandlePool<ResourcesAssetHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class ResourcesAllAssetsHandle<TAsset> : ResourcesOperationHandle, IAllAssetsHandle<TAsset>, IInternalCacheable where TAsset : UnityEngine.Object
    {
        private UniTask _task;

        public override bool IsDone => _task.Status.IsCompleted();
        public override float Progress => _task.Status.IsCompleted() ? 1f : 0f;
        public override UniTask Task => IsDone ? UniTask.CompletedTask : _task;

        public IReadOnlyList<TAsset> Assets { get; private set; }

        private int _refCount;
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;
        private volatile bool _disposed;

        public ResourcesAllAssetsHandle() { }

        public void Initialize(int id, string cacheKey, TAsset[] assets, Action<string, IReferenceCounted> onReleaseToCache)
        {
            SetId(id);
            _cacheKey = cacheKey;
            Assets = assets;
            _task = SimulateAsync();
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static ResourcesAllAssetsHandle<TAsset> Create(int id, string cacheKey, TAsset[] assets, Action<string, IReferenceCounted> onReleaseToCache)
        {
            var h = ResourcesHandlePool<ResourcesAllAssetsHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, assets, onReleaseToCache);
            return h;
        }

        private async UniTask SimulateAsync() => await UniTask.Yield();

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[ResourcesAllAssetsHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[ResourcesAllAssetsHandle] Release called more times than Retain. Refcount underflow prevented.");
                return;
            }
            if (newCount == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(_cacheKey, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            _disposed = true;
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            Assets = null;
            _task = default;
            _cacheKey = null;
            _onReleaseToCache = null;
            ResourcesHandlePool<ResourcesAllAssetsHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class ResourcesInstantiateHandle : ResourcesOperationHandle, IInstantiateHandle, IInternalCacheable
    {
        public GameObject Instance { get; private set; }

        private int _refCount;
        private Action<string, IReferenceCounted> _onReleaseToCache;
        private volatile bool _disposed;

        public ResourcesInstantiateHandle() { }

        public void Initialize(int id, GameObject instance, Action<string, IReferenceCounted> onReleaseToCache)
        {
            SetId(id);
            Instance = instance;
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static ResourcesInstantiateHandle Create(int id, GameObject instance, Action<string, IReferenceCounted> onReleaseToCache)
        {
            var h = ResourcesHandlePool<ResourcesInstantiateHandle>.Get();
            h.Initialize(id, instance, onReleaseToCache);
            return h;
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[ResourcesInstantiateHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[ResourcesInstantiateHandle] Release called more times than Retain. Refcount underflow prevented.");
                return;
            }
            if (newCount == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(null, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            _disposed = true;
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            Instance = null;
            _onReleaseToCache = null;
            ResourcesHandlePool<ResourcesInstantiateHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }
}
