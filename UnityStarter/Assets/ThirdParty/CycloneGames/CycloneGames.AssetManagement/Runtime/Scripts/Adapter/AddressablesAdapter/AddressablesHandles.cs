#if CYCLONEGAMES_HAS_ADDRESSABLES
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
        // Monotonic millisecond clock (Environment.TickCount64 is unavailable on Unity's runtime).
        private static readonly double s_msPerTick = 1000.0 / System.Diagnostics.Stopwatch.Frequency;
        private static long NowMs() => (long)(System.Diagnostics.Stopwatch.GetTimestamp() * s_msPerTick);
        // Tracks the last idle start as monotonic ms; long.MaxValue means actively in use.
        private static long _idleStartMs = long.MaxValue;
        private static int _highWaterMark;

        public static T Get()
        {
            lock (_poolLock)
            {
                _idleStartMs = long.MaxValue; // pool is active; reset idle timer
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
                if (_pool.Count > SOFT_LIMIT && _idleStartMs == long.MaxValue)
                    _idleStartMs = NowMs();

                TryShrinkIfIdle();
            }
        }

        private static void TryShrinkIfIdle()
        {
            if (_pool.Count <= SOFT_LIMIT || _idleStartMs == long.MaxValue) return;

            long idleMs = NowMs() - _idleStartMs;
            if (idleMs < SHRINK_THRESHOLD_MS) return;

            int toRemove = Math.Min(SHRINK_BATCH_SIZE, _pool.Count - SOFT_LIMIT);
            for (int i = 0; i < toRemove; i++) _pool.Pop();

            if (_pool.Count <= SOFT_LIMIT) _idleStartMs = long.MaxValue;
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
                _idleStartMs = long.MaxValue;
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

    internal sealed class AddressableAssetHandle<TAsset> : AddressablesOperationHandle, IAssetHandle<TAsset>, IReferenceCounted, IInternalCacheable, IAssetMemoryFootprint where TAsset : UnityEngine.Object
    {
        internal AsyncOperationHandle<TAsset> Raw;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;

        // Use AsyncOperationHandle.Task (System.Threading.Tasks.Task, supports multiple awaiters)
        // instead of IEnumerator.ToUniTask() which creates a one-shot EnumeratorPromise.
        public override UniTask Task => IsDone ? UniTask.CompletedTask : Raw.Task.AsUniTask();

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
            _cacheKey = null;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableAssetHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => Raw.IsValid() ? Cache.AssetMemoryEstimator.Estimate(Raw.Result) : 0;
    }

    internal sealed class AddressableAllAssetsHandle<TAsset> : AddressablesOperationHandle, IAllAssetsHandle<TAsset>, IReferenceCounted, IInternalCacheable, IAssetMemoryFootprint where TAsset : UnityEngine.Object
    {
        private AsyncOperationHandle<IList<TAsset>> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;

        // Use AsyncOperationHandle.Task (System.Threading.Tasks.Task, supports multiple awaiters)
        // instead of IEnumerator.ToUniTask() which creates a one-shot EnumeratorPromise.
        public override UniTask Task => IsDone ? UniTask.CompletedTask : raw.Task.AsUniTask();

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
            _cacheKey = null;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableAllAssetsHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        long IAssetMemoryFootprint.EstimateRuntimeBytes()
        {
            if (!raw.IsValid() || raw.Result == null) return 0;
            long total = 0;
            var all = raw.Result;
            for (int i = 0; i < all.Count; i++) total += Cache.AssetMemoryEstimator.Estimate(all[i]);
            return total;
        }
    }

    internal sealed class AddressableInstantiateHandle : AddressablesOperationHandle, IInstantiateHandle, IReferenceCounted, IInternalCacheable
    {
        private AsyncOperationHandle<GameObject> raw;
        public override bool IsDone => raw.IsDone;
        public override float Progress => raw.PercentComplete;
        public override string Error => raw.OperationException?.Message;

        // Use AsyncOperationHandle.Task (System.Threading.Tasks.Task, supports multiple awaiters)
        // instead of IEnumerator.ToUniTask() which creates a one-shot EnumeratorPromise.
        public override UniTask Task => IsDone ? UniTask.CompletedTask : raw.Task.AsUniTask();

        public GameObject Instance => raw.Result;

        private int _refCount;
        private volatile bool _disposed;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public AddressableInstantiateHandle() { }

        public void Initialize(int id, AsyncOperationHandle<GameObject> raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            this.raw = raw;
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
            if (raw.IsValid())
            {
                if (!Addressables.ReleaseInstance(raw))
                {
                    Addressables.Release(raw);
                }
            }
            this.raw = default;
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

        public FailedInstantiateHandle(string error) { Error = error; }
        public void WaitForAsyncComplete() { }
        public void Dispose() { }
    }

    internal sealed class AddressableSceneHandle : AddressablesOperationHandle, ISceneHandle, IReferenceCounted, IInternalCacheable
    {
        internal AsyncOperationHandle<SceneInstance> Raw;
        internal int DebugId => Id;
        public override bool IsDone => Raw.IsDone;
        public override float Progress => Raw.PercentComplete;
        public override string Error => Raw.OperationException?.Message;

        // Use AsyncOperationHandle.Task (System.Threading.Tasks.Task, supports multiple awaiters).
        public override UniTask Task => IsDone ? UniTask.CompletedTask : Raw.Task.AsUniTask();

        public string ScenePath { get; private set; }
        public Scene Scene => Raw.Result.Scene;
        public SceneActivationMode ActivationMode { get; private set; }
        public SceneActivationState ActivationState
        {
            get
            {
                RefreshActivationState();
                return _activationState;
            }
        }

        public bool SupportsManualActivation => true;

        private int _refCount;
        private volatile bool _disposed;
        private Action<string, IReferenceCounted> _onReleaseToCache;
        private SceneActivationState _activationState;

        public AddressableSceneHandle() { }

        public void Initialize(int id, AsyncOperationHandle<SceneInstance> raw, bool activateOnLoad, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            SetId(id);
            Raw = raw;
            ScenePath = raw.DebugName;
            ActivationMode = activateOnLoad ? SceneActivationMode.ActivateOnLoad : SceneActivationMode.Manual;
            _activationState = SceneActivationState.Loading;
            _onReleaseToCache = onReleaseToCache;
            _disposed = false;
            _refCount = 1;
        }

        public static AddressableSceneHandle Create(int id, AsyncOperationHandle<SceneInstance> raw, bool activateOnLoad, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveAddressablesPool<AddressableSceneHandle>.Get();
            h.Initialize(id, raw, activateOnLoad, onReleaseToCache, cancellationToken);
            return h;
        }

        public override void WaitForAsyncComplete() => Raw.WaitForCompletion();

        public async UniTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            RefreshActivationState();
            if (_activationState == SceneActivationState.Activated)
            {
                return;
            }

            if (!IsDone)
            {
                await Task.AttachExternalCancellation(cancellationToken);
            }

            RefreshActivationState();
            if (_activationState == SceneActivationState.Activated)
            {
                return;
            }

            if (!Raw.IsValid())
            {
                return;
            }

            if (ActivationMode == SceneActivationMode.ActivateOnLoad)
            {
                _activationState = SceneActivationState.Activated;
                return;
            }

            AsyncOperation activateOperation = Raw.Result.ActivateAsync();
            while (activateOperation != null && !activateOperation.isDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(cancellationToken);
            }

            _activationState = SceneActivationState.Activated;
        }

        private void RefreshActivationState()
        {
            if (_activationState != SceneActivationState.Loading || !IsDone)
            {
                return;
            }

            _activationState = ActivationMode == SceneActivationMode.Manual
                ? SceneActivationState.WaitingForActivation
                : SceneActivationState.Activated;
        }

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
                CLogger.LogWarning("[AddressableSceneHandle] Release only releases caller ownership. Use IAssetPackage.UnloadSceneAsync to unload the scene.");
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            _disposed = true;
            SceneTracker.Unregister(Id);
            if (HandleTracker.Enabled) HandleTracker.Unregister(Id);
            Raw = default;
            ScenePath = null;
            ActivationMode = SceneActivationMode.ActivateOnLoad;
            _activationState = SceneActivationState.Loading;
            _onReleaseToCache = null;
            AdaptiveAddressablesPool<AddressableSceneHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class AddressableDownloader : IDownloader
    {
        private AsyncOperationHandle raw;
        private bool _cancelled;
        private readonly bool _completedWithoutWork;
        public bool IsDone => _completedWithoutWork || _cancelled || (raw.IsValid() && raw.IsDone);
        public bool Succeed => _completedWithoutWork || (!_cancelled && raw.IsValid() && raw.Status == AsyncOperationStatus.Succeeded);
        public float Progress => _completedWithoutWork ? 1f : _cancelled ? 0f : raw.IsValid() ? raw.PercentComplete : 0f;
        public int TotalDownloadCount => 0;
        public int CurrentDownloadCount => 0;
        public long TotalDownloadBytes => _completedWithoutWork || _cancelled || !raw.IsValid() ? 0 : raw.GetDownloadStatus().TotalBytes;
        public long CurrentDownloadBytes => _completedWithoutWork || _cancelled || !raw.IsValid() ? 0 : raw.GetDownloadStatus().DownloadedBytes;
        public string Error => _cancelled ? "Cancelled" : raw.IsValid() ? raw.OperationException?.Message : string.Empty;

        public AddressableDownloader(AsyncOperationHandle raw)
        {
            this.raw = raw;
            _completedWithoutWork = !raw.IsValid();
        }

        public void Begin() { }
        public UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            return _completedWithoutWork || _cancelled || !raw.IsValid()
                ? UniTask.CompletedTask
                : raw.ToUniTask(cancellationToken: cancellationToken);
        }
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
