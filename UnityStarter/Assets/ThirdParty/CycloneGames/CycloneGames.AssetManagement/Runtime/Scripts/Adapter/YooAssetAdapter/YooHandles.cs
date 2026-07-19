#if CYCLONEGAMES_HAS_YOOASSET
using System;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;
using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;
using YooAsset;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class YooOperationTask
    {
        public static async UniTask CompleteAsync(HandleBase operation, string fallbackError)
        {
            if (operation == null || !operation.IsValid)
            {
                throw new InvalidOperationException($"{fallbackError} The provider handle is invalid.");
            }

            await operation;
            if (!operation.IsValid)
            {
                throw new InvalidOperationException($"{fallbackError} The provider handle became invalid before completion.");
            }

            if (operation.Status != EOperationStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(operation.Error) ? fallbackError : operation.Error);
            }
        }

        public static async UniTask CompleteAsync(AsyncOperationBase operation, string fallbackError)
        {
            if (operation == null)
            {
                throw new InvalidOperationException($"{fallbackError} The provider operation is unavailable.");
            }

            await operation;
            if (operation.Status != EOperationStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(operation.Error) ? fallbackError : operation.Error);
            }
        }
    }

    internal sealed class YooAssetHandle<TAsset> : IAssetHandle<TAsset>, IReferenceCounted,
        IInternalCacheable, IAssetMemoryFootprint, IAssetBackendLifetime, ITrackedAssetHandle
        where TAsset : UnityEngine.Object
    {
        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly Cache.AssetCacheKey _cacheKey;
        private readonly UniTask _task;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private int _refCount;
        private int _disposed;

        internal AssetHandle Raw { get; private set; }
        internal object Owner { get; private set; }

        private YooAssetHandle(
            long id,
            object owner,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache)
        {
            _id = id;
            Owner = owner;
            _cacheKey = cacheKey;
            Raw = raw;
            _task = AssetOperationBroadcast.Create(YooOperationTask.CompleteAsync(
                raw,
                $"YooAsset failed to load an asset of type '{typeof(TAsset).Name}'."));
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooAssetHandle<TAsset> Create(
            long id,
            object owner,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache) =>
            new YooAssetHandle<TAsset>(id, owner, cacheKey, raw, onReleaseToCache);

        public bool IsDone => _task.Status != UniTaskStatus.Pending;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.Error ?? string.Empty;
        public UniTask Task => _task;
        public TAsset Asset => Raw?.GetAssetObject<TAsset>();
        public UnityEngine.Object AssetObject => Raw?.AssetObject;
        public int RefCount => Volatile.Read(ref _refCount);

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Raw?.WaitForAsyncComplete();
        }

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                CLogger.LogError("[YooAssetHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[YooAssetHandle] Release called more times than Retain.");
                return;
            }

            if (count == 0)
            {
                if (_onReleaseToCache != null)
                {
                    _onReleaseToCache(_cacheKey, this);
                }
                else
                {
                    DisposeInternal();
                }
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Release();
        }

        internal void DisposeInternal()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            Raw?.Dispose();
            Raw = null;
            Owner = null;
            _onReleaseToCache = null;
            HandleTracker.Unregister(_id);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => Cache.AssetMemoryEstimator.Estimate(AssetObject);
    }

    internal sealed class YooAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset>, IReferenceCounted,
        IInternalCacheable, IAssetMemoryFootprint, IAssetBackendLifetime, ITrackedAssetHandle
        where TAsset : UnityEngine.Object
    {
        private sealed class ReadOnlyListAdapter : IReadOnlyList<TAsset>
        {
            private IReadOnlyList<UnityEngine.Object> _source;

            public TAsset this[int index] => _source[index] as TAsset;
            public int Count => _source?.Count ?? 0;
            public void SetSource(IReadOnlyList<UnityEngine.Object> source) => _source = source;
            public void Clear() => _source = null;

            public IEnumerator<TAsset> GetEnumerator()
            {
                if (_source == null)
                {
                    yield break;
                }

                for (int i = 0; i < _source.Count; i++)
                {
                    yield return _source[i] as TAsset;
                }
            }

            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly Cache.AssetCacheKey _cacheKey;
        private readonly ReadOnlyListAdapter _assets = new ReadOnlyListAdapter();
        private readonly UniTask _task;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private AllAssetsHandle _raw;
        private int _refCount;
        private int _disposed;

        private YooAllAssetsHandle(
            long id,
            Cache.AssetCacheKey cacheKey,
            AllAssetsHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache)
        {
            _id = id;
            _cacheKey = cacheKey;
            _raw = raw;
            _task = AssetOperationBroadcast.Create(YooOperationTask.CompleteAsync(
                raw,
                "YooAsset failed to load the requested asset collection."));
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooAllAssetsHandle<TAsset> Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            AllAssetsHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache) =>
            new YooAllAssetsHandle<TAsset>(id, cacheKey, raw, onReleaseToCache);

        public bool IsDone => _task.Status != UniTaskStatus.Pending;
        public float Progress => _raw?.Progress ?? 0f;
        public string Error => _raw?.Error ?? string.Empty;
        public UniTask Task => _task;
        public int RefCount => Volatile.Read(ref _refCount);

        public IReadOnlyList<TAsset> Assets
        {
            get
            {
                _assets.SetSource(_raw != null && _raw.IsDone ? _raw.AllAssetObjects : null);
                return _assets;
            }
        }

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            _raw?.WaitForAsyncComplete();
        }

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                CLogger.LogError("[YooAllAssetsHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[YooAllAssetsHandle] Release called more times than Retain.");
                return;
            }

            if (count == 0)
            {
                if (_onReleaseToCache != null)
                {
                    _onReleaseToCache(_cacheKey, this);
                }
                else
                {
                    DisposeInternal();
                }
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Release();
        }

        internal void DisposeInternal()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            _raw?.Dispose();
            _raw = null;
            _assets.Clear();
            _onReleaseToCache = null;
            HandleTracker.Unregister(_id);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;

        long IAssetMemoryFootprint.EstimateRuntimeBytes()
        {
            if (_raw?.AllAssetObjects == null)
            {
                return 0L;
            }

            long total = 0L;
            IReadOnlyList<UnityEngine.Object> objects = _raw.AllAssetObjects;
            for (int i = 0; i < objects.Count; i++)
            {
                if (!Cache.AssetMemoryEstimator.TryAddToAggregate(objects[i], ref total))
                {
                    return 0L;
                }
            }

            return total;
        }
    }

    internal sealed class YooRawFileHandle : IRawFileHandle, IReferenceCounted, IInternalCacheable,
        IAssetMemoryFootprint, IAssetBackendLifetime, ITrackedAssetHandle
    {
        private const long SNAPSHOT_OBJECT_OVERHEAD_BYTES = 64L;

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly Cache.AssetCacheKey _cacheKey;
        private readonly UniTask _task;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private AssetHandle _raw;
        private byte[] _bytesSnapshot;
        private string _textSnapshot = string.Empty;
        private string _error = string.Empty;
        private float _progress;
        private int _refCount;
        private int _disposed;

        private YooRawFileHandle(
            long id,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache)
        {
            _id = id;
            _cacheKey = cacheKey;
            _raw = raw;
            _task = AssetOperationBroadcast.Create(CompleteAndSnapshotAsync(raw));
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooRawFileHandle Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache) =>
            new YooRawFileHandle(id, cacheKey, raw, onReleaseToCache);

        public bool IsDone => _task.Status != UniTaskStatus.Pending;
        public float Progress
        {
            get
            {
                AssetHandle raw = Volatile.Read(ref _raw);
                if (raw != null && PlayerLoopHelper.IsMainThread && raw.IsValid)
                {
                    Volatile.Write(ref _progress, raw.Progress);
                }

                return Volatile.Read(ref _progress);
            }
        }

        public string Error => Volatile.Read(ref _error) ?? string.Empty;
        public UniTask Task => _task;
        public string FilePath => string.Empty;
        public int RefCount => Volatile.Read(ref _refCount);

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_task.Status != UniTaskStatus.Pending)
            {
                return;
            }

            AssetHandle raw = Volatile.Read(ref _raw);
            if (raw != null && raw.IsValid)
            {
                raw.WaitForAsyncComplete();
            }

            // YooAsset 3 invokes its native completion continuation inline during synchronous waiting. The
            // snapshot and its broadcast task must therefore also be terminal before this method may return.
            // Keep a defensive fallback so a provider scheduling change cannot expose a false completion.
            if (_task.Status == UniTaskStatus.Pending)
            {
                throw new NotSupportedException(
                    "YooAsset could not materialize the raw-file snapshot synchronously. Await Task instead.");
            }
        }

        public string ReadText()
        {
            if (_task.Status != UniTaskStatus.Succeeded || Volatile.Read(ref _disposed) != 0)
            {
                return string.Empty;
            }

            return Volatile.Read(ref _textSnapshot) ?? string.Empty;
        }

        public byte[] ReadBytes()
        {
            if (_task.Status != UniTaskStatus.Succeeded || Volatile.Read(ref _disposed) != 0)
            {
                return null;
            }

            byte[] snapshot = Volatile.Read(ref _bytesSnapshot);
            if (snapshot == null)
            {
                return null;
            }

            if (snapshot.Length == 0)
            {
                return Array.Empty<byte>();
            }

            var copy = new byte[snapshot.Length];
            Buffer.BlockCopy(snapshot, 0, copy, 0, snapshot.Length);
            return copy;
        }

        private async UniTask CompleteAndSnapshotAsync(AssetHandle raw)
        {
            try
            {
                await YooOperationTask.CompleteAsync(
                    raw,
                    "YooAsset failed to load the requested raw file.");

                if (!PlayerLoopHelper.IsMainThread)
                {
                    await UniTask.SwitchToMainThread();
                }

                if (Volatile.Read(ref _disposed) != 0)
                {
                    throw new ObjectDisposedException(nameof(YooRawFileHandle));
                }

                RawFileObject rawFile = raw.GetAssetObject<RawFileObject>();
                if (rawFile == null)
                {
                    throw new InvalidOperationException(
                        "YooAsset completed the raw-file load without a RawFileObject result.");
                }

                byte[] bytes = rawFile.GetBytes();
                if (bytes == null)
                {
                    throw new InvalidOperationException(
                        "YooAsset completed the raw-file load without readable byte content.");
                }

                string text = rawFile.GetText() ?? string.Empty;
                Volatile.Write(ref _bytesSnapshot, bytes);
                Volatile.Write(ref _textSnapshot, text);
                Volatile.Write(ref _progress, 1f);
            }
            catch (Exception ex)
            {
                Volatile.Write(ref _error, ex.Message ?? "YooAsset raw-file load failed.");
                throw;
            }
            finally
            {
                ReleaseProviderHandle(raw);
            }
        }

        private void ReleaseProviderHandle(AssetHandle raw)
        {
            if (raw == null || !ReferenceEquals(Interlocked.CompareExchange(ref _raw, null, raw), raw))
            {
                return;
            }

            if (raw.IsValid)
            {
                raw.Dispose();
            }
        }

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                CLogger.LogError("[YooRawFileHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[YooRawFileHandle] Release called more times than Retain.");
                return;
            }

            if (count == 0)
            {
                if (_onReleaseToCache != null)
                {
                    _onReleaseToCache(_cacheKey, this);
                }
                else
                {
                    DisposeInternal();
                }
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Release();
        }

        internal void DisposeInternal()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            AssetHandle raw = Interlocked.Exchange(ref _raw, null);
            if (raw != null && raw.IsValid)
            {
                raw.Dispose();
            }

            Volatile.Write(ref _bytesSnapshot, null);
            Volatile.Write(ref _textSnapshot, string.Empty);
            _onReleaseToCache = null;
            HandleTracker.Unregister(_id);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;

        long IAssetMemoryFootprint.EstimateRuntimeBytes()
        {
            byte[] bytes = Volatile.Read(ref _bytesSnapshot);
            string text = Volatile.Read(ref _textSnapshot);
            long byteCount = bytes?.LongLength ?? 0L;
            long textBytes = text == null ? 0L : (long)text.Length * sizeof(char);
            return SNAPSHOT_OBJECT_OVERHEAD_BYTES + byteCount + textBytes;
        }
    }

    internal sealed class YooInstantiateHandle : IInstantiateHandle, IReferenceCounted, IInternalCacheable,
        ITrackedAssetHandle
    {
        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly UniTask _task;
        private InstantiateOperation _raw;
        private YooAssetHandle<GameObject> _source;
        private Action<long> _onDisposed;
        private int _refCount;
        private int _callerDisposed;
        private int _disposed;

        private YooInstantiateHandle(
            long id,
            InstantiateOperation raw,
            YooAssetHandle<GameObject> source,
            Action<long> onDisposed)
        {
            _id = id;
            _raw = raw;
            _task = AssetOperationBroadcast.Create(YooOperationTask.CompleteAsync(
                raw,
                "YooAsset failed to instantiate the requested asset."));
            _source = source;
            _onDisposed = onDisposed;
            _source.Retain();
            _refCount = 1;
        }

        public static YooInstantiateHandle Create(
            long id,
            InstantiateOperation raw,
            YooAssetHandle<GameObject> source,
            Action<long> onDisposed) => new YooInstantiateHandle(id, raw, source, onDisposed);

        public bool IsDone => _task.Status != UniTaskStatus.Pending;
        public float Progress => _raw?.Progress ?? 0f;
        public string Error => _raw?.Error ?? string.Empty;
        public UniTask Task => _task;
        public GameObject Instance => _raw?.Result;
        public int RefCount => Volatile.Read(ref _refCount);
        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            _raw?.WaitForCompletion();
        }
        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[YooInstantiateHandle] Release called more times than Retain.");
            }
            else if (count == 0)
            {
                DisposeInternal();
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _callerDisposed, 1) == 0)
            {
                Release();
            }
        }

        internal void DisposeInternal()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            try
            {
                if (_raw != null)
                {
                    if (!_raw.IsDone)
                    {
                        _raw.Cancel();
                    }

                    if (_raw.Result != null)
                    {
                        UnityEngine.Object.Destroy(_raw.Result);
                    }
                }
            }
            finally
            {
                Action<long> onDisposed = _onDisposed;
                _onDisposed = null;
                _raw = null;
                _source?.Release();
                _source = null;
                HandleTracker.Unregister(_id);
                onDisposed?.Invoke(_id);
            }
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class YooSceneHandle : ISceneHandle, IReferenceCounted,
        ISceneTrackerHandleState, ITrackedAssetHandle
    {
        private const float MANUAL_ACTIVATION_READY_PROGRESS = 0.9f;

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly UniTask _task;
        private int _refCount;
        private int _disposed;
        private SceneActivationState _activationState;
        private bool _activationStarted;
        private UniTask _activationTask;
        private bool _manualLoadResumed;
        private bool _unloadStarted;
        private UniTask _unloadTask;
        private string _scenePath;
        private Scene _scene;
        private float _progress;
        private string _error = string.Empty;
        private int _callerDisposed;
        private string _lifecycleError = string.Empty;
        private bool _providerSceneUnloaded;

        internal YooAsset.SceneHandle Raw { get; private set; }
        internal long DebugId => _id;
        internal object OwnerToken { get; private set; }
        internal bool UnloadStarted => _unloadStarted;
        internal bool IsProviderHandleReleased => Raw == null || !Raw.IsValid;
        internal bool IsTerminallyReleased => Volatile.Read(ref _disposed) != 0;
        internal bool RequiresShutdownActivation
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                if (IsTerminallyReleased ||
                    _providerSceneUnloaded ||
                    ActivationMode != SceneActivationMode.Manual ||
                    _manualLoadResumed)
                {
                    return false;
                }

                RefreshActivationState();
                if (_activationState == SceneActivationState.Activated)
                {
                    return false;
                }

                YooAsset.SceneHandle raw = Raw;
                return raw == null ||
                       !raw.IsValid ||
                       !raw.IsDone ||
                       raw.Status != EOperationStatus.Failed;
            }
        }

        private YooSceneHandle(
            long id,
            object ownerToken,
            string scenePath,
            YooAsset.SceneHandle raw,
            bool activateOnLoad)
        {
            _id = id;
            OwnerToken = ownerToken ?? throw new ArgumentNullException(nameof(ownerToken));
            Raw = raw;
            _task = AssetOperationBroadcast.Create(CompleteLoadAsync(raw));
            ActivationMode = activateOnLoad ? SceneActivationMode.ActivateOnLoad : SceneActivationMode.Manual;
            _activationState = SceneActivationState.Loading;
            _scenePath = scenePath ?? string.Empty;
            _refCount = 1;
        }

        private async UniTask CompleteLoadAsync(YooAsset.SceneHandle raw)
        {
            await YooOperationTask.CompleteAsync(
                raw,
                "YooAsset failed to load the requested scene.");
            CaptureSceneIfAvailable(raw);
        }

        public static YooSceneHandle Create(
            long id,
            object ownerToken,
            string scenePath,
            YooAsset.SceneHandle raw,
            bool activateOnLoad) => new YooSceneHandle(id, ownerToken, scenePath, raw, activateOnLoad);

        private bool CanReadRaw => Volatile.Read(ref _disposed) == 0 && Raw != null && Raw.IsValid;

        public bool IsDone => _task.Status != UniTaskStatus.Pending;
        public float Progress
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return CanReadRaw ? _progress = Raw.Progress : _progress;
            }
        }
        public string Error
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                if (!string.IsNullOrEmpty(_lifecycleError))
                {
                    return _lifecycleError;
                }

                return CanReadRaw ? _error = Raw.Error ?? string.Empty : _error;
            }
        }
        public UniTask Task => _task;
        public string ScenePath => _scenePath;
        public Scene Scene
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return CanReadRaw ? _scene = Raw.SceneObject : _scene;
            }
        }
        public SceneActivationMode ActivationMode { get; private set; }
        public bool SupportsManualActivation => true;
        public int RefCount => Volatile.Read(ref _refCount);

        public SceneActivationState ActivationState
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                RefreshActivationState();
                return _activationState;
            }
        }

        public bool ShouldRemoveFromSceneTracker
        {
            get
            {
                if (Volatile.Read(ref _disposed) != 0)
                {
                    return true;
                }

                if (Raw == null || !Raw.IsValid)
                {
                    return !_scene.IsValid() || !_scene.isLoaded;
                }

                return false;
            }
        }

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!IsDone)
            {
                throw new NotSupportedException(
                    "YooAsset does not expose public synchronous scene completion for this provider version.");
            }
        }

        public UniTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(YooSceneHandle));
            }

            RefreshActivationState();
            if (_activationState == SceneActivationState.Activated)
            {
                return UniTask.CompletedTask;
            }

            if (_activationStarted)
            {
                return _activationTask;
            }

            if (_unloadStarted)
            {
                throw new InvalidOperationException(
                    "Scene activation cannot start after scene unload has been committed.");
            }

            if (!_activationStarted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _activationStarted = true;
                _activationTask = AssetOperationBroadcast.Create(ActivateCoreAsync());
            }

            return _activationTask;
        }

        internal async UniTask ResolveShutdownActivationAsync()
        {
            if (!RequiresShutdownActivation)
            {
                return;
            }

            try
            {
                await ActivateAsync(CancellationToken.None);
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                // Terminal load failure and provider-observed scene retirement no longer hold Unity's queue.
                // Any still-unresolved manual activation must stop shutdown before unload operations are queued.
                if (RequiresShutdownActivation)
                {
                    throw;
                }
            }
        }

        private async UniTask ActivateCoreAsync()
        {
            try
            {
                if (ActivationMode == SceneActivationMode.Manual && CanReadRaw && !_manualLoadResumed)
                {
                    if (!Raw.AllowSceneActivation())
                    {
                        throw new InvalidOperationException(
                            "YooAsset rejected the scene activation request.");
                    }
                    _manualLoadResumed = true;
                    _activationState = SceneActivationState.Loading;
                }

                // The broadcast task is repeatable. Always await it so a load that already faulted cannot be
                // mistaken for a completed, activatable scene.
                await Task;

                if (!CanReadRaw)
                {
                    throw new InvalidOperationException("YooAsset scene handle became invalid before activation.");
                }

                _activationState = SceneActivationState.Activated;
            }
            catch
            {
                _activationStarted = false;
                throw;
            }
        }

        internal UniTask UnloadAsync(CancellationToken cancellationToken)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Volatile.Read(ref _disposed) != 0)
            {
                return UniTask.CompletedTask;
            }

            if (!_unloadStarted)
            {
                cancellationToken.ThrowIfCancellationRequested();
                _unloadStarted = true;
                _lifecycleError = string.Empty;
                SceneTracker.MarkUnloadRequested(_id);
                _unloadTask = AssetOperationBroadcast.Create(UnloadCoreAsync());
            }

            return _unloadTask;
        }

        private async UniTask UnloadCoreAsync()
        {
            try
            {
                if (_activationStarted)
                {
                    try
                    {
                        // Preserve operation ordering: an activation committed before unload reaches its terminal
                        // wrapper state before YooAsset is allowed to invalidate the native scene handle.
                        await _activationTask;
                    }
                    catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
                    {
                        // The activation caller observes its own failure. Cleanup must still run so a failed load
                        // cannot retain provider ownership indefinitely.
                    }
                }

                YooAsset.SceneHandle raw = Raw;
                if (raw == null || !raw.IsValid)
                {
                    // YooAsset invalidates and releases SceneHandle from its scene-unloaded callback. A valid,
                    // still-loaded cached Scene is the only evidence that cleanup is not already complete.
                    if (IsKnownSceneAbsent(raw))
                    {
                        DisposeInternal(providerHandleAlreadyReleased: true);
                        return;
                    }

                    throw new InvalidOperationException(
                        "YooAsset scene unload cannot start because the provider handle is invalid and scene absence cannot be proven.");
                }

                if (raw.IsDone && raw.Status == EOperationStatus.Failed)
                {
                    // YooAsset 3 represents an immediately rejected scene load with ErrorProvider.
                    // ErrorProvider cannot construct UnloadSceneOperation; its terminal handle must be
                    // released directly because no Unity Scene was created.
                    DisposeInternal(providerHandleAlreadyReleased: false);
                    return;
                }

                // YooAsset's operation unsuspends a manual load and waits for an in-flight scene load before
                // unloading it. Do not reject an invalid SceneObject before starting this operation.
                CaptureSceneIfAvailable(raw);
                UnloadSceneOperation operation = raw.UnloadSceneAsync();
                if (ActivationMode == SceneActivationMode.Manual && !_manualLoadResumed)
                {
                    // The provider accepted authoritative unload, which is YooAsset's operation for releasing
                    // the manual activation barrier before scene teardown. Do not mark the barrier resolved if
                    // operation construction throws.
                    _manualLoadResumed = true;
                    _activationState = SceneActivationState.Loading;
                }
                await operation;
                if (operation.Status == EOperationStatus.Succeeded)
                {
                    // YooAsset releases SceneHandle automatically through its scene-unloaded callback.
                    // Do not query the provider handle after successful completion.
                    DisposeInternal(providerHandleAlreadyReleased: true);
                    return;
                }

                if (IsKnownSceneAbsent(raw))
                {
                    DisposeInternal(providerHandleAlreadyReleased: !raw.IsValid);
                    return;
                }

                throw new InvalidOperationException(
                    string.IsNullOrEmpty(operation.Error) ? "YooAsset scene unload failed." : operation.Error);
            }
            catch (Exception exception)
            {
                _unloadStarted = false;
                if (AssetRuntimeGuard.IsRecoverableException(exception))
                {
                    _lifecycleError = exception.Message;
                    SceneTracker.MarkUnloadFailed(_id, _lifecycleError);
                }
                throw;
            }
        }

        private void CaptureSceneIfAvailable(YooAsset.SceneHandle raw)
        {
            if (raw != null && raw.IsValid)
            {
                Scene providerScene = raw.SceneObject;
                if (providerScene.IsValid())
                {
                    _scene = providerScene;
                }
            }
        }

        internal bool MatchesScene(Scene scene)
        {
            if (!scene.IsValid())
            {
                return false;
            }

            if (!_scene.IsValid())
            {
                CaptureSceneIfAvailable(Raw);
            }

            return _scene.IsValid() && _scene == scene;
        }

        internal void OnProviderSceneUnloadObserved(Scene scene)
        {
            _providerSceneUnloaded = true;
            if (scene.IsValid() && (!_scene.IsValid() || _scene == scene))
            {
                _scene = scene;
            }
        }

        internal void OnProviderSceneUnloaded(Scene scene)
        {
            // YooAsset releases SceneHandle from its scene-unloaded callback. The wrapper must not
            // release that invalidated provider handle a second time.
            OnProviderSceneUnloadObserved(scene);
            DisposeInternal(providerHandleAlreadyReleased: true);
        }

        private bool IsKnownSceneAbsent(YooAsset.SceneHandle raw)
        {
            if (_scene.IsValid())
            {
                return !_scene.isLoaded;
            }

            if (_providerSceneUnloaded)
            {
                return true;
            }

            if (raw == null || !raw.IsValid)
            {
                return true;
            }

            Scene providerScene = raw.SceneObject;
            if (providerScene.IsValid())
            {
                _scene = providerScene;
                return !providerScene.isLoaded;
            }

            return raw.IsDone && raw.Status == EOperationStatus.Failed;
        }

        private void RefreshActivationState()
        {
            if (_activationState != SceneActivationState.Loading)
            {
                return;
            }

            UniTaskStatus taskStatus = _task.Status;
            if (ActivationMode == SceneActivationMode.Manual && !_manualLoadResumed)
            {
                if (PlayerLoopHelper.IsMainThread && CanReadRaw)
                {
                    _progress = Raw.Progress;
                }

                // Unity scene loading holds at 0.9 while activation is disabled. YooAsset keeps its
                // provider task pending at that barrier, so task completion cannot identify this state.
                if ((taskStatus == UniTaskStatus.Pending || taskStatus == UniTaskStatus.Succeeded) &&
                    _progress >= MANUAL_ACTIVATION_READY_PROGRESS)
                {
                    _activationState = SceneActivationState.WaitingForActivation;
                }

                return;
            }

            if (taskStatus == UniTaskStatus.Succeeded)
            {
                _activationState = SceneActivationState.Activated;
            }
        }

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[YooSceneHandle] Release called more times than Retain.");
            }
            else if (count == 0)
            {
                CLogger.LogWarning("[YooSceneHandle] Dispose releases caller ownership only. Use IAssetSceneLoader.UnloadSceneAsync to unload the scene.");
            }
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _callerDisposed, 1) == 0)
            {
                Release();
            }
        }

        private void DisposeInternal(bool providerHandleAlreadyReleased)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            SceneTracker.Unregister(_id);
            Interlocked.Exchange(ref _refCount, 0);
            if (!providerHandleAlreadyReleased && Raw != null)
            {
                if (Raw.IsValid)
                {
                    _progress = Raw.Progress;
                    _error = Raw.Error ?? string.Empty;
                    _scene = Raw.SceneObject;
                    Raw.Dispose();
                }
            }

            Raw = null;

            _scenePath = string.Empty;
            _activationTask = default;
            _unloadTask = default;
            HandleTracker.Unregister(_id);
        }
    }

    internal sealed class YooDownloader : IDownloader
    {
        private readonly CancellationTokenSource _sharedCancellation = new CancellationTokenSource();
        private readonly UniTaskCompletionSource _disposeCompletion = new UniTaskCompletionSource();
        private ResourceDownloaderOperation _operation;
        private Action<YooDownloader> _onDisposed;
        private bool _startStarted;
        private UniTask _startTask;
        private UniTask _startCallerTask;
        private int _disposed;
        private bool _cancelled;
        private bool _succeeded;
        private float _progress;
        private int _totalCount;
        private int _currentCount;
        private long _totalBytes;
        private long _currentBytes;
        private string _error = string.Empty;

        public YooDownloader(
            ResourceDownloaderOperation operation,
            Action<YooDownloader> onDisposed,
            int scopeValueCount)
        {
            _operation = operation ?? throw new ArgumentNullException(nameof(operation));
            _onDisposed = onDisposed;
            ScopeValueCount = scopeValueCount;
        }

        internal int ScopeValueCount { get; }

        public bool IsDone
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return IsDoneOnMainThread();
            }
        }

        public bool Succeed
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return !_cancelled &&
                       (_startStarted
                           ? _startCallerTask.Status == UniTaskStatus.Succeeded && _succeeded
                           : _succeeded);
            }
        }

        public float Progress
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _operation?.Progress ?? _progress;
            }
        }

        public int TotalDownloadCount
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _operation?.TotalDownloadCount ?? _totalCount;
            }
        }

        public int CurrentDownloadCount
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _operation?.CurrentDownloadCount ?? _currentCount;
            }
        }

        public long TotalDownloadBytes
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _operation?.TotalDownloadBytes ?? _totalBytes;
            }
        }

        public long CurrentDownloadBytes
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _operation?.CurrentDownloadBytes ?? _currentBytes;
            }
        }

        public string Error
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _cancelled ? "Cancelled" : _operation?.Error ?? _error;
            }
        }

        public UniTask PrepareAsync(CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(YooDownloader));
            }

            if (_cancelled)
            {
                throw new OperationCanceledException("The YooAsset download was cancelled.");
            }

            return UniTask.CompletedTask;
        }

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (_cancelled)
            {
                throw new OperationCanceledException("The YooAsset download was cancelled.");
            }

            if (!_startStarted)
            {
                _startStarted = true;
                _startTask = AssetOperationBroadcast.Create(StartCoreAsync());
                _startCallerTask = AssetOperationBroadcast.CreateCallerView(
                    _startTask,
                    _sharedCancellation.Token);
            }

            await WaitWithCallerCancellationOnMainThreadAsync(_startCallerTask, cancellationToken);
        }

        private async UniTask StartCoreAsync()
        {
            ResourceDownloaderOperation operation = _operation;
            operation.StartDownload();
            await operation;

            CaptureSnapshot();
            if (_cancelled)
            {
                throw new OperationCanceledException("The YooAsset download was cancelled.");
            }

            if (operation.Status != EOperationStatus.Succeeded)
            {
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(operation.Error)
                        ? "YooAsset dependency download failed."
                        : operation.Error);
            }
        }

        public void Cancel()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_cancelled || Volatile.Read(ref _disposed) != 0 || HasTerminalCallerResult())
            {
                return;
            }

            _cancelled = true;
            _operation?.CancelDownload();
            CaptureSnapshot();
            _sharedCancellation.Cancel();
        }

        public void Dispose()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Interlocked.Exchange(ref _disposed, 1) != 0)
            {
                return;
            }

            if (!_cancelled && !HasTerminalCallerResult())
            {
                _cancelled = true;
            }

            try
            {
                if (_cancelled)
                {
                    _operation?.CancelDownload();
                }
                CaptureSnapshot();
                if (_cancelled)
                {
                    _sharedCancellation.Cancel();
                }
            }
            finally
            {
                CompleteDisposeAfterProviderTerminalAsync().Forget();
            }
        }

        internal UniTask WaitForDisposeCompletionAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            return Volatile.Read(ref _disposed) != 0
                ? _disposeCompletion.Task
                : UniTask.CompletedTask;
        }

        private async UniTask CompleteDisposeAfterProviderTerminalAsync()
        {
            try
            {
                if (_startStarted)
                {
                    try
                    {
                        await _startTask;
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        // Cancellation/failure is already published by the caller-visible task. Disposal waits
                        // only until the provider wrapper has observed terminal abort and captured its snapshot.
                    }
                }
            }
            finally
            {
                try
                {
                    CaptureSnapshot();
                    _operation = null;
                    _sharedCancellation.Dispose();
                }
                finally
                {
                    try
                    {
                        Action<YooDownloader> onDisposed = _onDisposed;
                        _onDisposed = null;
                        onDisposed?.Invoke(this);
                    }
                    finally
                    {
                        _disposeCompletion.TrySetResult();
                    }
                }
            }
        }

        private void CaptureSnapshot()
        {
            if (_operation == null)
            {
                return;
            }

            _succeeded = _operation.Status == EOperationStatus.Succeeded;
            _progress = _operation.Progress;
            _totalCount = _operation.TotalDownloadCount;
            _currentCount = _operation.CurrentDownloadCount;
            _totalBytes = _operation.TotalDownloadBytes;
            _currentBytes = _operation.CurrentDownloadBytes;
            _error = _operation.Error ?? string.Empty;
        }

        private bool IsDoneOnMainThread()
        {
            return _cancelled ||
                   Volatile.Read(ref _disposed) != 0 ||
                   HasTerminalCallerResult();
        }

        private bool HasTerminalCallerResult()
        {
            return _startStarted && _startCallerTask.Status != UniTaskStatus.Pending;
        }

        private static async UniTask WaitWithCallerCancellationOnMainThreadAsync(
            UniTask sharedTask,
            CancellationToken cancellationToken)
        {
            try
            {
                await AssetOperationBroadcast.CreateCallerView(sharedTask, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                if (!PlayerLoopHelper.IsMainThread)
                {
                    await UniTask.SwitchToMainThread();
                }

                throw;
            }
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(YooDownloader));
            }
        }
    }
}
#endif
