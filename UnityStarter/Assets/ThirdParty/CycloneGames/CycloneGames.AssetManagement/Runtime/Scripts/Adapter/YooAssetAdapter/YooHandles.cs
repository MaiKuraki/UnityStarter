#if YOOASSET_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
    /// <summary>
    /// Thread-safe object pool with soft/hard sizing limits and idle-based auto-shrink.
    /// Soft limit: preferred steady-state pool size.
    /// Hard limit: absolute cap; objects beyond it are discarded to prevent memory bloat.
    /// Shrink: triggers only when the pool has been genuinely idle for SHRINK_THRESHOLD_MS
    ///         (idle timer starts after Release when pool exceeds soft limit, and resets on any Get).
    /// </summary>
    internal static class AdaptiveHandlePool<T> where T : class, new()
    {
        private const int SOFT_LIMIT = 64;
        private const int HARD_LIMIT = 512;
        private const int SHRINK_THRESHOLD_MS = 30000;
        private const int SHRINK_BATCH_SIZE = 16;

        private static readonly Stack<T> _pool = new Stack<T>(SOFT_LIMIT);
        private static readonly object _poolLock = new object();
        // long.MaxValue signals "not idle" (pool is actively in use).
        private static long _idleStartTicks = long.MaxValue;
        private static int _highWaterMark;

        public static T Get()
        {
            lock (_poolLock)
            {
                _idleStartTicks = long.MaxValue; // reset idle timer on every access
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

                // Start idle timer only when pool is above soft limit and no timer is running.
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

    public sealed class YooAssetHandle<TAsset> : IAssetHandle<TAsset>, IInternalCacheable where TAsset : UnityEngine.Object
    {
        private int _id;
        internal AssetHandle Raw;
        private UniTask _task;
        private int _refCount;
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public YooAssetHandle() { }

        internal void Initialize(int id, string cacheKey, AssetHandle raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            _id = id;
            _cacheKey = cacheKey;
            Raw = raw;
            _onReleaseToCache = onReleaseToCache;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _refCount = 1;
        }

        public static YooAssetHandle<TAsset> Create(int id, string cacheKey, AssetHandle raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveHandlePool<YooAssetHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.LastError ?? string.Empty;
        public UniTask Task => IsDone ? UniTask.CompletedTask : _task;
        public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

        public TAsset Asset => Raw != null ? Raw.GetAssetObject<TAsset>() : null;
        public UnityEngine.Object AssetObject => Raw?.AssetObject;

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(_cacheKey, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            Raw?.Dispose();
            Raw = null;
            _cacheKey = null;
            _onReleaseToCache = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            _task = default;
            AdaptiveHandlePool<YooAssetHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    public sealed class YooAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset>, IInternalCacheable where TAsset : UnityEngine.Object
    {
        // Wraps a List<UnityEngine.Object> as IReadOnlyList<TAsset> without allocating a new list.
        private sealed class ReadOnlyListAdapter : IReadOnlyList<TAsset>
        {
            private IReadOnlyList<UnityEngine.Object> _source;

            public void Initialize(IReadOnlyList<UnityEngine.Object> source) => _source = source;
            public void Clear() => _source = null;

            public TAsset this[int index] => _source[index] as TAsset;
            public int Count => _source?.Count ?? 0;

            public IEnumerator<TAsset> GetEnumerator()
            {
                if (_source == null) yield break;
                foreach (var item in _source) yield return item as TAsset;
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private int _id;
        internal AllAssetsHandle Raw;
        private readonly ReadOnlyListAdapter _listAdapter = new ReadOnlyListAdapter();
        private UniTask _task;
        private int _refCount;
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public YooAllAssetsHandle() { }

        internal void Initialize(int id, string cacheKey, AllAssetsHandle raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            _id = id;
            _cacheKey = cacheKey;
            Raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _listAdapter.Clear();
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooAllAssetsHandle<TAsset> Create(int id, string cacheKey, AllAssetsHandle raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveHandlePool<YooAllAssetsHandle<TAsset>>.Get();
            h.Initialize(id, cacheKey, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.LastError ?? string.Empty;
        public UniTask Task => IsDone ? UniTask.CompletedTask : _task;
        public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

        public IReadOnlyList<TAsset> Assets
        {
            get
            {
                if (Raw == null || !Raw.IsDone) return Array.Empty<TAsset>();
                if (_listAdapter.Count == 0 && Raw.AllAssetObjects != null)
                    _listAdapter.Initialize(Raw.AllAssetObjects);
                return _listAdapter;
            }
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(_cacheKey, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            Raw?.Dispose();
            Raw = null;
            _listAdapter.Clear();
            _cacheKey = null;
            _onReleaseToCache = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            _task = default;
            AdaptiveHandlePool<YooAllAssetsHandle<TAsset>>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    public sealed class YooInstantiateHandle : IInstantiateHandle, IInternalCacheable
    {
        private int _id;
        internal InstantiateOperation Raw;
        private int _refCount;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public YooInstantiateHandle() { }

        internal void Initialize(int id, InstantiateOperation raw, Action<string, IReferenceCounted> onReleaseToCache)
        {
            _id = id;
            Raw = raw;
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooInstantiateHandle Create(int id, InstantiateOperation raw, Action<string, IReferenceCounted> onReleaseToCache)
        {
            var h = AdaptiveHandlePool<YooInstantiateHandle>.Get();
            h.Initialize(id, raw, onReleaseToCache);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.Error ?? string.Empty;
        public UniTask Task => IsDone ? UniTask.CompletedTask : (Raw?.Task.AsUniTask() ?? UniTask.CompletedTask);
        public void WaitForAsyncComplete() { }

        public GameObject Instance => Raw?.Result;

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(null, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            Raw = null;
            _onReleaseToCache = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            AdaptiveHandlePool<YooInstantiateHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    public sealed class YooSceneHandle : ISceneHandle, IInternalCacheable
    {
        private int _id;
        public YooAsset.SceneHandle Raw;
        private int _refCount;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public YooSceneHandle() { }

        internal void Initialize(int id, YooAsset.SceneHandle raw, Action<string, IReferenceCounted> onReleaseToCache)
        {
            _id = id;
            Raw = raw;
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooSceneHandle Create(int id, YooAsset.SceneHandle raw, Action<string, IReferenceCounted> onReleaseToCache)
        {
            var h = AdaptiveHandlePool<YooSceneHandle>.Get();
            h.Initialize(id, raw, onReleaseToCache);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.LastError ?? string.Empty;
        public UniTask Task => IsDone ? UniTask.CompletedTask : (Raw?.Task.AsUniTask() ?? UniTask.CompletedTask);
        public void WaitForAsyncComplete() { }

        public string ScenePath => Raw?.SceneName;
        public Scene Scene => Raw?.SceneObject ?? default;

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(null, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            // UnloadAsync is fire-and-forget here since DisposeInternal is called from release paths.
            // Callers that need to await unload (e.g. UnloadSceneAsync) must do so before calling this.
            Raw?.UnloadAsync();
            Raw = null;
            _onReleaseToCache = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            AdaptiveHandlePool<YooSceneHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    public sealed class YooRawFileHandle : IRawFileHandle, IInternalCacheable
    {
        private int _id;
        private RawFileHandle _raw;
        private UniTask _task;
        private int _refCount;
        private string _cacheKey;
        private Action<string, IReferenceCounted> _onReleaseToCache;

        public YooRawFileHandle() { }

        internal void Initialize(int id, string cacheKey, RawFileHandle raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            _id = id;
            _cacheKey = cacheKey;
            _raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooRawFileHandle Create(int id, string cacheKey, RawFileHandle raw, Action<string, IReferenceCounted> onReleaseToCache, CancellationToken cancellationToken)
        {
            var h = AdaptiveHandlePool<YooRawFileHandle>.Get();
            h.Initialize(id, cacheKey, raw, onReleaseToCache, cancellationToken);
            return h;
        }

        public bool IsDone => _raw == null || _raw.IsDone;
        public float Progress => _raw?.Progress ?? 0f;
        public string Error => _raw?.LastError ?? string.Empty;
        public UniTask Task => IsDone ? UniTask.CompletedTask : _task;
        public void WaitForAsyncComplete() => _raw?.WaitForAsyncComplete();

        public string FilePath => _raw?.GetRawFilePath() ?? string.Empty;

        public string ReadText()
        {
            if (_raw == null || !_raw.IsDone) return string.Empty;
            return _raw.GetRawFileText();
        }

        public byte[] ReadBytes()
        {
            if (_raw == null || !_raw.IsDone) return null;
            return _raw.GetRawFileData();
        }

        public int RefCount => Interlocked.CompareExchange(ref _refCount, 0, 0);

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Interlocked.Decrement(ref _refCount) == 0)
            {
                if (_onReleaseToCache != null) _onReleaseToCache(_cacheKey, this);
                else DisposeInternal();
            }
        }

        public void Dispose() => Release();

        internal void DisposeInternal()
        {
            _raw?.Dispose();
            _raw = null;
            _cacheKey = null;
            _onReleaseToCache = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            _task = default;
            AdaptiveHandlePool<YooRawFileHandle>.Release(this);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    public sealed class YooDownloader : IDownloader
    {
        private ResourceDownloaderOperation _op;

        public YooDownloader() { }

        internal void Initialize(ResourceDownloaderOperation op) => _op = op;

        public static YooDownloader Create(ResourceDownloaderOperation op)
        {
            var d = AdaptiveHandlePool<YooDownloader>.Get();
            d.Initialize(op);
            return d;
        }

        public bool IsDone => _op == null || _op.IsDone;
        public bool Succeed => _op != null && _op.Status == EOperationStatus.Succeed;
        public float Progress => _op?.Progress ?? 1f;
        public int TotalDownloadCount => _op?.TotalDownloadCount ?? 0;
        public int CurrentDownloadCount => _op?.CurrentDownloadCount ?? 0;
        public long TotalDownloadBytes => _op?.TotalDownloadBytes ?? 0;
        public long CurrentDownloadBytes => _op?.CurrentDownloadBytes ?? 0;
        public string Error => _op?.Error ?? string.Empty;

        public void Begin() => _op?.BeginDownload();

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            Begin();
            while (!IsDone)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    _op?.CancelDownload();
                    throw new OperationCanceledException(cancellationToken);
                }
                await UniTask.Yield(cancellationToken);
            }
        }

        public void Pause() => _op?.PauseDownload();
        public void Resume() => _op?.ResumeDownload();
        public void Cancel() => _op?.CancelDownload();

        public void Combine(IDownloader other)
        {
            if (_op == null) return;
            if (other is YooDownloader yd && yd._op != null) _op.Combine(yd._op);
        }
    }
}
#endif