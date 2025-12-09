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
    /// Thread-safe object pool for handle objects to reduce GC allocations.
    /// Uses a bounded pool strategy: maintains a maximum pool size to prevent unbounded memory growth.
    /// </summary>
    internal static class HandlePool<T> where T : class, new()
    {
        private const int MAX_POOL_SIZE = 128;
        private static readonly Stack<T> _pool = new Stack<T>(32);
        private static readonly object _poolLock = new object();

        public static T Get()
        {
            lock (_poolLock)
            {
                return _pool.Count > 0 ? _pool.Pop() : new T();
            }
        }

        public static void Release(T item)
        {
            if (item == null) return;
            lock (_poolLock)
            {
                if (_pool.Count < MAX_POOL_SIZE)
                {
                    _pool.Push(item);
                }
            }
        }
    }

    public sealed class YooAssetHandle<TAsset> : IAssetHandle<TAsset> where TAsset : UnityEngine.Object
    {
        private int _id;
        internal AssetHandle Raw;
        private UniTask _task;
        private int _disposed;

        public YooAssetHandle() { }

        internal void Initialize(int id, AssetHandle raw, CancellationToken cancellationToken)
        {
            _id = id;
            Raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public static YooAssetHandle<TAsset> Create(int id, AssetHandle raw, CancellationToken cancellationToken)
        {
            var h = HandlePool<YooAssetHandle<TAsset>>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.LastError ?? string.Empty;
        public UniTask Task => _task;
        public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

        public TAsset Asset => Raw != null ? Raw.GetAssetObject<TAsset>() : null;
        public UnityEngine.Object AssetObject => Raw?.AssetObject;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Raw?.Dispose();
            Raw = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            _task = default;
            HandlePool<YooAssetHandle<TAsset>>.Release(this);
        }
    }

    public sealed class YooAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset> where TAsset : UnityEngine.Object
    {
        // Private utility class to wrap a list of Objects as a read-only list of TAsset, avoiding GC allocation of a new list.
        private sealed class ReadOnlyListAdapter : IReadOnlyList<TAsset>
        {
            private IReadOnlyList<UnityEngine.Object> _source;

            public void Initialize(IReadOnlyList<UnityEngine.Object> source)
            {
                _source = source;
            }

            public void Clear()
            {
                _source = null;
            }

            public TAsset this[int index] => _source[index] as TAsset;
            public int Count => _source?.Count ?? 0;
            public IEnumerator<TAsset> GetEnumerator()
            {
                if (_source == null) yield break;
                foreach (var item in _source)
                {
                    yield return item as TAsset;
                }
            }
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private int _id;
        internal AllAssetsHandle Raw;
        private readonly ReadOnlyListAdapter _listAdapter = new ReadOnlyListAdapter();
        private UniTask _task;
        private int _disposed;

        public YooAllAssetsHandle() { }

        internal void Initialize(int id, AllAssetsHandle raw, CancellationToken cancellationToken)
        {
            _id = id;
            Raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
            _listAdapter.Clear(); // Reset adapter
        }

        public static YooAllAssetsHandle<TAsset> Create(int id, AllAssetsHandle raw, CancellationToken cancellationToken)
        {
            var h = HandlePool<YooAllAssetsHandle<TAsset>>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.LastError ?? string.Empty;
        public UniTask Task => _task;
        public void WaitForAsyncComplete() => Raw?.WaitForAsyncComplete();

        public IReadOnlyList<TAsset> Assets
        {
            get
            {
                if (Raw == null || !Raw.IsDone) return Array.Empty<TAsset>();
                // Re-use the adapter, check if it's already set for current Raw
                // Since we clear on Initialize, we just need to set it if empty.
                // Note: This assumes Assets is called after completion.
                if (_listAdapter.Count == 0 && Raw.AllAssetObjects != null)
                {
                    _listAdapter.Initialize(Raw.AllAssetObjects);
                }
                return _listAdapter;
            }
        }

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Raw?.Dispose();
            Raw = null;
            _listAdapter.Clear();
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            _task = default;
            HandlePool<YooAllAssetsHandle<TAsset>>.Release(this);
        }
    }

    public sealed class YooInstantiateHandle : IInstantiateHandle
    {
        private int _id;
        internal InstantiateOperation Raw;
        private int _disposed;

        public YooInstantiateHandle() { }

        internal void Initialize(int id, InstantiateOperation raw)
        {
            _id = id;
            Raw = raw;
        }

        public static YooInstantiateHandle Create(int id, InstantiateOperation raw)
        {
            var h = HandlePool<YooInstantiateHandle>.Get();
            h.Initialize(id, raw);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.Error ?? string.Empty;
        public UniTask Task => Raw?.Task.AsUniTask() ?? UniTask.CompletedTask;
        public void WaitForAsyncComplete() { /* not supported */ }

        public GameObject Instance => Raw?.Result;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Raw = null; // InstantiateOperation doesn't need Dispose, but we clear ref.
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            HandlePool<YooInstantiateHandle>.Release(this);
        }
    }

    public sealed class YooSceneHandle : ISceneHandle
    {
        private int _id;
        public YooAsset.SceneHandle Raw;
        private int _disposed;

        public YooSceneHandle() { }

        internal void Initialize(int id, YooAsset.SceneHandle raw)
        {
            _id = id;
            Raw = raw;
        }

        public static YooSceneHandle Create(int id, YooAsset.SceneHandle raw)
        {
            var h = HandlePool<YooSceneHandle>.Get();
            h.Initialize(id, raw);
            return h;
        }

        public bool IsDone => Raw == null || Raw.IsDone;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.LastError ?? string.Empty;
        public UniTask Task => Raw?.Task.AsUniTask() ?? UniTask.CompletedTask;
        public void WaitForAsyncComplete() { /* not supported */ }

        public string ScenePath => Raw?.SceneName;
        public Scene Scene => Raw?.SceneObject ?? default;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            Raw?.UnloadAsync();
            Raw = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            HandlePool<YooSceneHandle>.Release(this);
        }
    }

    public sealed class YooRawFileHandle : IRawFileHandle
    {
        private int _id;
        private RawFileHandle _raw;
        private UniTask _task;
        private int _disposed;

        public YooRawFileHandle() { }

        internal void Initialize(int id, RawFileHandle raw, CancellationToken cancellationToken)
        {
            _id = id;
            _raw = raw;
            _task = raw.ToUniTask(cancellationToken: cancellationToken);
        }

        public static YooRawFileHandle Create(int id, RawFileHandle raw, CancellationToken cancellationToken)
        {
            var h = HandlePool<YooRawFileHandle>.Get();
            h.Initialize(id, raw, cancellationToken);
            return h;
        }

        public bool IsDone => _raw == null || _raw.IsDone;
        public float Progress => _raw?.Progress ?? 0f;
        public string Error => _raw?.LastError ?? string.Empty;
        public UniTask Task => _task;
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

        public void Dispose()
        {
            if (Interlocked.Exchange(ref _disposed, 1) != 0) return;

            _raw?.Dispose();
            _raw = null;
            if (HandleTracker.Enabled) HandleTracker.Unregister(_id);
            _task = default;
            HandlePool<YooRawFileHandle>.Release(this);
        }
    }

    public sealed class YooDownloader : IDownloader
    {
        private ResourceDownloaderOperation _op;

        public YooDownloader() { }

        internal void Initialize(ResourceDownloaderOperation op)
        {
            _op = op;
        }

        public static YooDownloader Create(ResourceDownloaderOperation op)
        {
            var d = HandlePool<YooDownloader>.Get();
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

        public async UniTask StartAsync(System.Threading.CancellationToken cancellationToken = default)
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
            if (other is YooDownloader yd && yd._op != null)
            {
                _op.Combine(yd._op);
            }
        }
    }
}
#endif