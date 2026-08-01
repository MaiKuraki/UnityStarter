using System;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;

using CycloneGames.Logging;

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
        private static readonly LogChannel Log = AssetManagementLog.Channel;

        private const string LOAD_FAILURE_MESSAGE = "Resource asset was not found or failed to load.";

        private ResourceRequest _request;
        private TAsset _syncAsset;
        private AssetOperationCompletion _completion;

        public ResourcesAssetPackage Owner { get; private set; }

        public override bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;
        public override float Progress => _request?.progress ?? 1f;
        public override string Error => _completion.Task.Status == UniTaskStatus.Faulted
            ? LOAD_FAILURE_MESSAGE
            : string.Empty;
        public override UniTask Task => _completion.Task;

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
            ResourcesAssetPackage owner,
            AssetOperationTailTracker operationTails)
        {
            SetId(id);
            _cacheKey = cacheKey;
            _request = request;
            _syncAsset = null;
            _completion = AssetOperationCompletion.Start(CompleteAsync(request), operationTails);
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
            ResourcesAssetPackage owner,
            AssetOperationTailTracker operationTails)
        {
            SetId(id);
            _cacheKey = cacheKey;
            _request = null;
            _syncAsset = asset;
            _completion = AssetOperationCompletion.Start(asset != null
                ? UniTask.CompletedTask
                : UniTask.FromException(new InvalidOperationException(LOAD_FAILURE_MESSAGE)),
                operationTails);
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
            ResourcesAssetPackage owner,
            AssetOperationTailTracker operationTails)
        {
            var h = new ResourcesAssetHandle<TAsset>();
            h.Initialize(id, cacheKey, request, onReleaseToCache, owner, operationTails);
            return h;
        }

        public static ResourcesAssetHandle<TAsset> Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            TAsset asset,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            ResourcesAssetPackage owner,
            AssetOperationTailTracker operationTails)
        {
            var h = new ResourcesAssetHandle<TAsset>();
            h.Initialize(id, cacheKey, asset, onReleaseToCache, owner, operationTails);
            return h;
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0) { Log.Error("[ResourcesAssetHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0) return;
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[ResourcesAssetHandle] Release called more times than Retain. Refcount underflow prevented.");
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
            _completion.TryCancelByOwner();
            HandleTracker.Unregister(Id);
            // Wrapper retirement deliberately drops only this package's strong reference. Resources.UnloadAsset can
            // invalidate shared external references, so native reclamation remains an explicit global maintenance pass.
            _request = null;
            _syncAsset = null;
            _cacheKey = default;
            _onReleaseToCache = null;
            Owner = null;
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => Cache.AssetMemoryEstimator.Estimate(AssetObject);
    }

    internal sealed class ResourcesInstantiateHandle : ResourcesOperationHandle, IInstantiateHandle, IReferenceCounted, IInternalCacheable
    {
        private static readonly LogChannel Log = AssetManagementLog.Channel;

        private const string INSTANTIATE_FAILURE_MESSAGE = "The resource instance could not be created.";
        private const int MAX_DESTROY_BARRIER_PLAYER_LOOP_TURNS = 4;
        private const int RELEASE_ACTIVE = 0;
        private const int RELEASE_IN_PROGRESS = 1;
        private const int RELEASE_FAILED = 2;
        private const int RELEASED = 3;

        public GameObject Instance { get; private set; }
        public override string Error => _error;
        public override UniTask Task => _completion.Task;

        private AssetOperationCompletion _completion;
        private AssetOperationCompletion _destroyCompletion;
        private AssetOperationTailTracker _operationTails;
        private Action<GameObject> _requestDestroy;
        private Func<GameObject, UniTask> _waitForDestroyed;
        private string _error;
        private int _refCount;
        private Action<long> _onDisposed;
        private int _releaseState;

        public ResourcesInstantiateHandle() { }

        public void Initialize(
            long id,
            GameObject instance,
            Action<long> onDisposed,
            AssetOperationTailTracker operationTails,
            Action<GameObject> requestDestroy,
            Func<GameObject, UniTask> waitForDestroyed)
        {
            SetId(id);
            Instance = instance;
            _error = instance == null ? INSTANTIATE_FAILURE_MESSAGE : string.Empty;
            _completion = AssetOperationCompletion.Start(instance != null
                ? UniTask.CompletedTask
                : UniTask.FromException(new InvalidOperationException(INSTANTIATE_FAILURE_MESSAGE)),
                operationTails);
            _operationTails = operationTails;
            _requestDestroy = requestDestroy ?? throw new ArgumentNullException(nameof(requestDestroy));
            _waitForDestroyed = waitForDestroyed ?? throw new ArgumentNullException(nameof(waitForDestroyed));
            _onDisposed = onDisposed;
            _releaseState = RELEASE_ACTIVE;
            _refCount = 1;
        }

        public static ResourcesInstantiateHandle Create(
            long id,
            GameObject instance,
            Action<long> onDisposed,
            AssetOperationTailTracker operationTails)
        {
            return Create(
                id,
                instance,
                onDisposed,
                operationTails,
                RequestUnityDestroy,
                WaitForUnityDestroyedAsync);
        }

        internal static ResourcesInstantiateHandle Create(
            long id,
            GameObject instance,
            Action<long> onDisposed,
            AssetOperationTailTracker operationTails,
            Action<GameObject> requestDestroy,
            Func<GameObject, UniTask> waitForDestroyed)
        {
            var h = new ResourcesInstantiateHandle();
            h.Initialize(
                id,
                instance,
                onDisposed,
                operationTails,
                requestDestroy,
                waitForDestroyed);
            return h;
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (Volatile.Read(ref _releaseState) != RELEASE_ACTIVE)
            {
                Log.Error("[ResourcesInstantiateHandle] Retain called after instance release began.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int releaseState = Volatile.Read(ref _releaseState);
            if (releaseState == RELEASE_IN_PROGRESS || releaseState == RELEASED)
            {
                return;
            }

            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[ResourcesInstantiateHandle] Release called more times than Retain. Refcount underflow prevented.");
                return;
            }
            if (newCount == 0)
            {
                try
                {
                    AssetOperationBroadcast.Observe(DisposeInternal());
                }
                catch
                {
                    RestoreRetryOwnership();
                    throw;
                }
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Release();
        }

        internal UniTask DisposeInternal()
        {
            AssetRuntimeGuard.EnsureMainThread();
            int releaseState = Volatile.Read(ref _releaseState);
            if (releaseState == RELEASED)
            {
                return UniTask.CompletedTask;
            }

            if (releaseState == RELEASE_IN_PROGRESS)
            {
                return _destroyCompletion.Task;
            }

            if (!TryBeginRelease())
            {
                return Volatile.Read(ref _releaseState) == RELEASED
                    ? UniTask.CompletedTask
                    : _destroyCompletion.Task;
            }

            GameObject instance = Instance;
            try
            {
                if (instance == null)
                {
                    CompleteRelease();
                    return UniTask.CompletedTask;
                }

                _requestDestroy(instance);
                if (instance == null)
                {
                    CompleteRelease();
                    return UniTask.CompletedTask;
                }

                _destroyCompletion = AssetOperationCompletion.Start(
                    CompleteDestroyBarrierAsync(instance),
                    _operationTails);
                return _destroyCompletion.Task;
            }
            catch
            {
                MarkReleaseFailed();
                throw;
            }
        }

        private async UniTask CompleteDestroyBarrierAsync(GameObject instance)
        {
            try
            {
                await _waitForDestroyed(instance);
                if (instance != null)
                {
                    throw new InvalidOperationException(
                        $"Unity did not destroy Resources instance {Id} within the bounded destroy barrier.");
                }

                CompleteRelease();
            }
            catch
            {
                MarkReleaseFailed();
                throw;
            }
        }

        private bool TryBeginRelease()
        {
            while (true)
            {
                int observed = Volatile.Read(ref _releaseState);
                if (observed == RELEASE_IN_PROGRESS || observed == RELEASED)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _releaseState, RELEASE_IN_PROGRESS, observed) == observed)
                {
                    return true;
                }
            }
        }

        private void MarkReleaseFailed()
        {
            Volatile.Write(ref _releaseState, RELEASE_FAILED);
            RestoreRetryOwnership();
        }

        private void RestoreRetryOwnership()
        {
            Interlocked.CompareExchange(ref _refCount, 1, 0);
        }

        private void CompleteRelease()
        {
            Instance = null;
            Action<long> onDisposed = _onDisposed;
            onDisposed?.Invoke(Id);

            _completion.TryCancelByOwner();
            HandleTracker.Unregister(Id);
            Interlocked.Exchange(ref _refCount, 0);
            Volatile.Write(ref _releaseState, RELEASED);
            _onDisposed = null;
            _requestDestroy = null;
            _waitForDestroyed = null;
            _operationTails = null;
        }

        private static void RequestUnityDestroy(GameObject instance)
        {
#if UNITY_EDITOR
            if (!Application.isPlaying)
            {
                UnityEngine.Object.DestroyImmediate(instance);
                return;
            }
#endif
            UnityEngine.Object.Destroy(instance);
        }

        private static async UniTask WaitForUnityDestroyedAsync(GameObject instance)
        {
            for (int i = 0; i < MAX_DESTROY_BARRIER_PLAYER_LOOP_TURNS && instance != null; i++)
            {
                // Object.Destroy is applied after the current Update loop. LastPostLateUpdate gives Unity a
                // deterministic destruction point while the bounded iteration count prevents an implicit endless wait.
                await UniTask.Yield(PlayerLoopTiming.LastPostLateUpdate);
            }
        }

        void IInternalCacheable.ForceDispose()
        {
            AssetOperationBroadcast.Observe(DisposeInternal());
        }
    }
}
