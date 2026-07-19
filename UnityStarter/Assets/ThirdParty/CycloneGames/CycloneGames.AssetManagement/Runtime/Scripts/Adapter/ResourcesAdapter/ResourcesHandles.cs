using System;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    internal abstract class ResourcesOperationHandle : IOperation, ITrackedAssetHandle
    {
        protected long Id;
        long ITrackedAssetHandle.DiagnosticHandleId => Id;
        public virtual bool IsDone => true;
        public virtual float Progress => 1f;
        public virtual string Error => string.Empty;
        public virtual UniTask Task => UniTask.CompletedTask;
        public virtual void WaitForAsyncComplete() { }

        protected ResourcesOperationHandle() { }
        protected void SetId(long id) => Id = id;
    }

    internal sealed class ResourcesAssetHandle<TAsset> : ResourcesOperationHandle, IAssetHandle<TAsset>, IReferenceCounted, IInternalCacheable, IAssetMemoryFootprint, IAssetBackendLifetime where TAsset : UnityEngine.Object
    {
        private const string LOAD_FAILURE_MESSAGE = "Resource asset was not found or failed to load.";

        private ResourceRequest _request;
        private TAsset _syncAsset;
        private UniTask _task;

        public ResourcesAssetPackage Owner { get; private set; }

        public override bool IsDone => _task.Status != UniTaskStatus.Pending;
        public override float Progress => _request?.progress ?? 1f;
        public override string Error => IsDone && Asset == null
            ? LOAD_FAILURE_MESSAGE
            : string.Empty;
        public override UniTask Task => _task;

        public override void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!IsDone)
            {
                throw new NotSupportedException(
                    "Unity Resources does not provide a portable synchronous wait for a pending ResourceRequest.");
            }
        }

        public TAsset Asset => _syncAsset != null ? _syncAsset : _request?.asset as TAsset;
        public UnityEngine.Object AssetObject => Asset;

        private int _refCount;
        private Cache.AssetCacheKey _cacheKey;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private int _disposed;

        public ResourcesAssetHandle() { }

        public void Initialize(
            long id,
            Cache.AssetCacheKey cacheKey,
            ResourceRequest request,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            ResourcesAssetPackage owner)
        {
            SetId(id);
            _cacheKey = cacheKey;
            _request = request;
            _syncAsset = null;
            _task = AssetOperationBroadcast.Create(CompleteAsync(request));
            _onReleaseToCache = onReleaseToCache;
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _disposed = 0;
            _refCount = 1;
        }

        public void Initialize(
            long id,
            Cache.AssetCacheKey cacheKey,
            TAsset asset,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            ResourcesAssetPackage owner)
        {
            SetId(id);
            _cacheKey = cacheKey;
            _request = null;
            _syncAsset = asset;
            _task = AssetOperationBroadcast.Create(asset != null
                ? UniTask.CompletedTask
                : UniTask.FromException(new InvalidOperationException(LOAD_FAILURE_MESSAGE)));
            _onReleaseToCache = onReleaseToCache;
            Owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _disposed = 0;
            _refCount = 1;
        }

        private static async UniTask CompleteAsync(ResourceRequest request)
        {
            await request.ToUniTask();
            if (request.asset is not TAsset)
            {
                throw new InvalidOperationException(LOAD_FAILURE_MESSAGE);
            }
        }

        public static ResourcesAssetHandle<TAsset> Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            ResourceRequest request,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            ResourcesAssetPackage owner)
        {
            var h = new ResourcesAssetHandle<TAsset>();
            h.Initialize(id, cacheKey, request, onReleaseToCache, owner);
            return h;
        }

        public static ResourcesAssetHandle<TAsset> Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            TAsset asset,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            ResourcesAssetPackage owner)
        {
            var h = new ResourcesAssetHandle<TAsset>();
            h.Initialize(id, cacheKey, asset, onReleaseToCache, owner);
            return h;
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0) { CLogger.LogError("[ResourcesAssetHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
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

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Release();
        }

        internal void DisposeInternal()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            HandleTracker.Unregister(Id);
            // Resources assets cannot be unloaded individually; only Resources.UnloadUnusedAssets() can reclaim them.
            _request = null;
            _syncAsset = null;
            _task = default;
            _cacheKey = default;
            _onReleaseToCache = null;
            Owner = null;
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => Cache.AssetMemoryEstimator.Estimate(AssetObject);
    }

    internal sealed class ResourcesInstantiateHandle : ResourcesOperationHandle, IInstantiateHandle, IReferenceCounted, IInternalCacheable
    {
        private const string INSTANTIATE_FAILURE_MESSAGE = "The resource instance could not be created.";

        public GameObject Instance { get; private set; }
        public override string Error => _error;
        public override UniTask Task => _task;

        private UniTask _task;
        private string _error;
        private int _refCount;
        private Action<long> _onDisposed;
        private int _disposed;

        public ResourcesInstantiateHandle() { }

        public void Initialize(long id, GameObject instance, Action<long> onDisposed)
        {
            SetId(id);
            Instance = instance;
            _error = instance == null ? INSTANTIATE_FAILURE_MESSAGE : string.Empty;
            _task = AssetOperationBroadcast.Create(instance != null
                ? UniTask.CompletedTask
                : UniTask.FromException(new InvalidOperationException(INSTANTIATE_FAILURE_MESSAGE)));
            _onDisposed = onDisposed;
            _disposed = 0;
            _refCount = 1;
        }

        public static ResourcesInstantiateHandle Create(long id, GameObject instance, Action<long> onDisposed)
        {
            var h = new ResourcesInstantiateHandle();
            h.Initialize(id, instance, onDisposed);
            return h;
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0) { CLogger.LogError("[ResourcesInstantiateHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[ResourcesInstantiateHandle] Release called more times than Retain. Refcount underflow prevented.");
                return;
            }
            if (newCount == 0)
            {
                DisposeInternal();
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Release();
        }

        internal void DisposeInternal()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;
            try
            {
                HandleTracker.Unregister(Id);
                if (Instance != null)
                {
#if UNITY_EDITOR
                    if (!Application.isPlaying)
                    {
                        UnityEngine.Object.DestroyImmediate(Instance);
                    }
                    else
                    {
                        UnityEngine.Object.Destroy(Instance);
                    }
#else
                    UnityEngine.Object.Destroy(Instance);
#endif
                }
            }
            finally
            {
                Instance = null;
                Action<long> onDisposed = _onDisposed;
                _onDisposed = null;
                onDisposed?.Invoke(Id);
            }
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }
}
