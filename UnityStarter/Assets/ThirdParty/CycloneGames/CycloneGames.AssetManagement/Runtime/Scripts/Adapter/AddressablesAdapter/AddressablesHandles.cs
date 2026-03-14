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
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Thread-safe object pool with soft/hard sizing limits and idle-based auto-shrink.
    /// Soft limit: preferred steady-state pool size.
    /// Hard limit: absolute cap; objects beyond it are discarded to prevent bloat.
    /// Shrink: triggers only when the pool has been idle (no Get or Release) for SHRINK_THRESHOLD_MS.
    /// </summary>
    internal static class AdaptiveAddressablesPool<T> where T : class, new()
    {
        private const int SOFT_LIMIT = 64;
        private const int HARD_LIMIT = 512;
        private const int SHRINK_THRESHOLD_MS = 30000;
        private const int SHRINK_BATCH_SIZE = 16;

        private static readonly Stack<T> _pool = new Stack<T>(SOFT_LIMIT);
        private static readonly object _poolLock = new object();
        // Tracks the last idle start: updated only when pool is not being actively accessed.
        private static long _idleStartTicks = long.MaxValue;
        private static int _highWaterMark;

        public static T Get()
        {
            lock (_poolLock)
            {
                _idleStartTicks = long.MaxValue; // pool is active; reset idle timer
                if (_pool.Count > 0) return _pool.Pop();
            }
            return new T();
        }

        public static void Release(T item)
        {
            if (item == null) return;
            lock (_poolLock)
            {
                int count = _pool.Count;
                if (count < HARD_LIMIT)
                {
                    _pool.Push(item);
                    if (count + 1 > _highWaterMark) _highWaterMark = count + 1;
                }

                // Begin idle timer only after this release if pool is above soft limit.
                if (_pool.Count > SOFT_LIMIT && _idleStartTicks == long.MaxValue)
                    _idleStartTicks = DateTime.UtcNow.Ticks;

                TryShrinkIfIdle();
            }
        }

        private static void TryShrinkIfIdle()
        {
            if (_pool.Count <= SOFT_LIMIT || _idleStartTicks == long.MaxValue) return;

            long idleMs = (DateTime.UtcNow.Ticks - _idleStartTicks) / TimeSpan.TicksPerMillisecond;
            if (idleMs < SHRINK_THRESHOLD_MS) return;

            int toRemove = Math.Min(SHRINK_BATCH_SIZE, _pool.Count - SOFT_LIMIT);
            for (int i = 0; i < toRemove; i++) _pool.Pop();

            if (_pool.Count <= SOFT_LIMIT) _idleStartTicks = long.MaxValue;
        }

        public static (int current, int highWaterMark) GetStats()
        {
            lock (_poolLock) return (_pool.Count, _highWaterMark);
        }

        public static void Clear()
        {
            lock (_poolLock)
            {
                _pool.Clear();
                _highWaterMark = 0;
                _idleStartTicks = long.MaxValue;
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

    internal sealed class AddressableAssetHandle<TAsset> : AddressablesOperationHandle, IAssetHandle<TAsset>, IInternalCacheable where TAsset : UnityEngine.Object
    {
        internal AsyncOperationHandle<TAsset> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;

        private UniTask _task;
        public override UniTask Task => IsDone ? UniTask.CompletedTask : _task;

        public TAsset Asset => Raw.Result;
        public UnityEngine.Object AssetObject => Raw.Result;

        private int _refCount;
        private volatile bool _disposed;
        // Stores the cache key so OnHandleReleased can be O(1) without scanning the active map.
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public AddressableAssetHandle() { }

        public void Initialize(int id, string cacheKey, AsyncOperationHandle<TAsset> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            _cacheKey = cacheKey;
            Raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static AddressableAssetHandle<TAsset> Create(int id, string cacheKey, AsyncOperationHandle<TAsset> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveAddressablesPool<AddressableAssetHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[AddressableAssetHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount); // restore
                CLogger.LogError("[AddressableAssetHandle] Release called more times than Retain. Ignoring extra release.");
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
            if (Raw.IsValid()) Addressables.Release(Raw);
            Raw = default;
            _task = default;
            _cacheKey = null;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableAssetHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class AddressableAllAssetsHandle<TAsset> : AddressablesOperationHandle, IAllAssetsHandle<TAsset>, IInternalCacheable where TAsset : UnityEngine.Object
    {
        private AsyncOperationHandle<IList<TAsset>> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;

        private UniTask _task;
        public override UniTask Task => IsDone ? UniTask.CompletedTask : _task;

        public IReadOnlyList<TAsset> Assets => (IReadOnlyList<TAsset>)raw.Result;

        private int _refCount;
        private volatile bool _disposed;
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public AddressableAllAssetsHandle() { }

        public void Initialize(int id, string cacheKey, AsyncOperationHandle<IList<TAsset>> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            _cacheKey = cacheKey;
            this.raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static AddressableAllAssetsHandle<TAsset> Create(int id, string cacheKey, AsyncOperationHandle<IList<TAsset>> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveAddressablesPool<AddressableAllAssetsHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[AddressableAllAssetsHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableAllAssetsHandle] Release called more times than Retain. Ignoring extra release.");
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
            if (raw.IsValid()) Addressables.Release(raw);
            this.raw = default;
            _task = default;
            _cacheKey = null;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableAllAssetsHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class AddressableInstantiateHandle : AddressablesOperationHandle, IInstantiateHandle, IInternalCacheable
    {
        private AsyncOperationHandle<GameObject> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;

        private UniTask _task;
        public override UniTask Task => IsDone ? UniTask.CompletedTask : _task;

        public GameObject Instance => raw.Result;

        private int _refCount;
        private volatile bool _disposed;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public AddressableInstantiateHandle() { }

        public void Initialize(int id, AsyncOperationHandle<GameObject> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            this.raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static AddressableInstantiateHandle Create(int id, AsyncOperationHandle<GameObject> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveAddressablesPool<AddressableInstantiateHandle>.Get();
            h.Initialize(id, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => raw.WaitForCompletion();

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[AddressableInstantiateHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableInstantiateHandle] Release called more times than Retain. Ignoring extra release.");
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
            if (raw.IsValid()) Addressables.Release(raw);
            this.raw = default;
            _task = default;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableInstantiateHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class FailedInstantiateHandle : IInstantiateHandle
    {
        public bool IsDone => true;
        public float Progress => 1f;
        public string Error { get; private set; }
        public UniTask Task => UniTask.CompletedTask;
        public GameObject Instance => null;
        public int RefCount => 0;

        public FailedInstantiateHandle(string error) { Error = error; }
        public void WaitForAsyncComplete() { }
        public void Retain() { }
        public void Release() { }
        public void Dispose() { }
    }

    internal sealed class AddressableSceneHandle : AddressablesOperationHandle, ISceneHandle, IInternalCacheable
    {
        internal AsyncOperationHandle<SceneInstance> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;

        private UniTask _task;
        public override UniTask Task => IsDone ? UniTask.CompletedTask : _task;

        public string ScenePath { get; private set; }
        public Scene Scene => Raw.Result.Scene;

        private int _refCount;
        private volatile bool _disposed;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public AddressableSceneHandle() { }

        public void Initialize(int id, AsyncOperationHandle<SceneInstance> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            Raw = raw;
            ScenePath = raw.DebugName;
            // AsyncOperationHandle<SceneInstance>.ToUniTask() triggers a warning ("yield SceneInstance
            // is not supported on await IEnumerator"). Poll IsDone instead.
            _task = UniTask.WaitUntil(() => Raw.IsDone, cancellationToken: cancellationToken);
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static AddressableSceneHandle Create(int id, AsyncOperationHandle<SceneInstance> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveAddressablesPool<AddressableSceneHandle>.Get();
            h.Initialize(id, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain()
        {
            if (_disposed) { CLogger.LogError("[AddressableSceneHandle] Retain called on a disposed handle."); return; }
            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableSceneHandle] Release called more times than Retain. Ignoring extra release.");
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
            if (Raw.IsValid()) Addressables.Release(Raw);
            Raw = default;
            ScenePath = null;
            _task = default;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableSceneHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class AddressableDownloader : IDownloader
    {
        private AsyncOperationHandle raw;
        private bool _cancelled;
        public bool IsDone => _cancelled || raw.IsDone;
        public bool Succeed => !_cancelled && raw.Status == AsyncOperationStatus.Succeeded;
        public float Progress => _cancelled ? 0f : raw.PercentComplete;
        public int TotalDownloadCount => 0;
        public int CurrentDownloadCount => 0;
        public long TotalDownloadBytes => _cancelled ? 0 : raw.GetDownloadStatus().TotalBytes;
        public long CurrentDownloadBytes => _cancelled ? 0 : raw.GetDownloadStatus().DownloadedBytes;
        public string Error => _cancelled ? "Cancelled" : raw.OperationException?.Message;

        public AddressableDownloader(AsyncOperationHandle raw) { this.raw = raw; }

        public void Begin() { }
        public UniTask StartAsync(CancellationToken cancellationToken = default) => raw.ToUniTask(cancellationToken: cancellationToken);
        public void Pause() => CLogger.LogWarning("[AddressableDownloader] Pause is not supported by Addressables.");
        public void Resume() => CLogger.LogWarning("[AddressableDownloader] Resume is not supported by Addressables.");
        public void Cancel()
        {
            if (!_cancelled && raw.IsValid()) Addressables.Release(raw);
            raw = default;
            _cancelled = true;
        }
        public void Combine(IDownloader other) => CLogger.LogWarning("[AddressableDownloader] Combine is not supported by Addressables.");
    }
}
#endif
