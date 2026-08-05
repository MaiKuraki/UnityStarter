#if CYCLONEGAMES_HAS_YOOASSET
using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;
using System.Threading;

using UnityEngine;
using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;
using YooAsset;

using CycloneGames.Logging;

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

    internal static class YooSynchronousWait
    {
        public static void EnsureTerminal(UniTaskStatus status)
        {
            if (status == UniTaskStatus.Pending)
            {
                throw new NotSupportedException(
                    "Pending YooAsset operations cannot be completed synchronously through this adapter. " +
                    "Await IOperation.Task instead.");
            }
        }
    }

    internal sealed class YooAssetHandle<TAsset> : IAssetHandle<TAsset>, IReferenceCounted,
        IInternalCacheable, IAssetMemoryFootprint, IAssetBackendLifetime, ITrackedAssetHandle
        where TAsset : UnityEngine.Object
    {
        private static readonly LogChannel Log = AssetManagementYooAssetLog.Channel;

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly Cache.AssetCacheKey _cacheKey;
        private readonly AssetOperationCompletion _completion;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private int _refCount;
        private int _releaseState;

        internal AssetHandle Raw { get; private set; }
        internal object Owner { get; private set; }

        private YooAssetHandle(
            long id,
            object owner,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            AssetOperationTailTracker operationTails)
        {
            _id = id;
            Owner = owner;
            _cacheKey = cacheKey;
            Raw = raw;
            _completion = AssetOperationCompletion.Start(YooOperationTask.CompleteAsync(
                raw,
                $"YooAsset failed to load an asset of type '{typeof(TAsset).Name}'."),
                operationTails);
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooAssetHandle<TAsset> Create(
            long id,
            object owner,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            AssetOperationTailTracker operationTails) =>
            new YooAssetHandle<TAsset>(id, owner, cacheKey, raw, onReleaseToCache, operationTails);

        public bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;
        public float Progress => Raw?.Progress ?? 0f;
        public string Error => Raw?.Error ?? string.Empty;
        public UniTask Task => _completion.Task;
        public TAsset Asset => Raw?.GetAssetObject<TAsset>();
        public UnityEngine.Object AssetObject => Raw?.AssetObject;
        public int RefCount => Volatile.Read(ref _refCount);

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            YooSynchronousWait.EnsureTerminal(_completion.Task.Status);
        }

        public void Retain()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                Log.Error("[YooAssetHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[YooAssetHandle] Release called more times than Retain.");
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
            if (!ProviderReleaseStateMachine.TryBeginRelease(ref _releaseState))
            {
                return;
            }

            _completion.TryCancelByOwner();
            try
            {
                Raw?.Dispose();
            }
            catch
            {
                ProviderReleaseStateMachine.MarkReleaseFailed(ref _releaseState);
                throw;
            }

            Raw = null;
            Owner = null;
            _onReleaseToCache = null;
            ProviderReleaseStateMachine.MarkReleased(ref _releaseState);
            HandleTracker.Unregister(_id);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState);
        long IAssetMemoryFootprint.EstimateRuntimeBytes() => Cache.AssetMemoryEstimator.Estimate(AssetObject);
    }

    internal sealed class YooAllAssetsHandle<TAsset> : IAllAssetsHandle<TAsset>, IReferenceCounted,
        IInternalCacheable, IAssetMemoryFootprint, IAssetBackendLifetime, ITrackedAssetHandle
        where TAsset : UnityEngine.Object
    {
        private static readonly LogChannel Log = AssetManagementYooAssetLog.Channel;

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
        private readonly AssetOperationCompletion _completion;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private AllAssetsHandle _raw;
        private int _refCount;
        private int _releaseState;

        private YooAllAssetsHandle(
            long id,
            Cache.AssetCacheKey cacheKey,
            AllAssetsHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            AssetOperationTailTracker operationTails)
        {
            _id = id;
            _cacheKey = cacheKey;
            _raw = raw;
            _completion = AssetOperationCompletion.Start(YooOperationTask.CompleteAsync(
                raw,
                "YooAsset failed to load the requested asset collection."),
                operationTails);
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooAllAssetsHandle<TAsset> Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            AllAssetsHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            AssetOperationTailTracker operationTails) =>
            new YooAllAssetsHandle<TAsset>(id, cacheKey, raw, onReleaseToCache, operationTails);

        public bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;
        public float Progress => _raw?.Progress ?? 0f;
        public string Error => _raw?.Error ?? string.Empty;
        public UniTask Task => _completion.Task;
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
            YooSynchronousWait.EnsureTerminal(_completion.Task.Status);
        }

        public void Retain()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                Log.Error("[YooAllAssetsHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[YooAllAssetsHandle] Release called more times than Retain.");
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
            if (!ProviderReleaseStateMachine.TryBeginRelease(ref _releaseState))
            {
                return;
            }

            _completion.TryCancelByOwner();
            try
            {
                _raw?.Dispose();
            }
            catch
            {
                ProviderReleaseStateMachine.MarkReleaseFailed(ref _releaseState);
                throw;
            }

            _raw = null;
            _assets.Clear();
            _onReleaseToCache = null;
            ProviderReleaseStateMachine.MarkReleased(ref _releaseState);
            HandleTracker.Unregister(_id);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState);

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
        private static readonly LogChannel Log = AssetManagementYooAssetLog.Channel;

        private const long SNAPSHOT_OBJECT_OVERHEAD_BYTES = 64L;

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly Cache.AssetCacheKey _cacheKey;
        private readonly AssetOperationCompletion _completion;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private AssetHandle _raw;
        private byte[] _bytesSnapshot;
        private string _textSnapshot = string.Empty;
        private string _error = string.Empty;
        private float _progress;
        private int _refCount;
        private int _releaseState;

        private YooRawFileHandle(
            long id,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            AssetOperationTailTracker operationTails)
        {
            _id = id;
            _cacheKey = cacheKey;
            _raw = raw;
            _completion = AssetOperationCompletion.Start(CompleteAndSnapshotAsync(raw), operationTails);
            _onReleaseToCache = onReleaseToCache;
            _refCount = 1;
        }

        public static YooRawFileHandle Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            AssetHandle raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache,
            AssetOperationTailTracker operationTails) =>
            new YooRawFileHandle(id, cacheKey, raw, onReleaseToCache, operationTails);

        public bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;
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
        public UniTask Task => _completion.Task;
        public string FilePath => string.Empty;
        public int RefCount => Volatile.Read(ref _refCount);

        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            YooSynchronousWait.EnsureTerminal(_completion.Task.Status);
        }

        public string ReadText()
        {
            if (_completion.Task.Status != UniTaskStatus.Succeeded ||
                ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                return string.Empty;
            }

            return Volatile.Read(ref _textSnapshot) ?? string.Empty;
        }

        public byte[] ReadBytes()
        {
            if (_completion.Task.Status != UniTaskStatus.Succeeded ||
                ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
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

                if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
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
                if (_completion.Task.Status != UniTaskStatus.Canceled)
                {
                    Volatile.Write(ref _error, ex.Message ?? "YooAsset raw-file load failed.");
                }
                throw;
            }
            finally
            {
                ReleaseProviderHandle(raw);
            }
        }

        private void ReleaseProviderHandle(AssetHandle raw)
        {
            if (raw == null || !ReferenceEquals(Volatile.Read(ref _raw), raw))
            {
                return;
            }

            if (raw.IsValid)
            {
                raw.Dispose();
            }

            Interlocked.CompareExchange(ref _raw, null, raw);
        }

        public void Retain()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                Log.Error("[YooRawFileHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[YooRawFileHandle] Release called more times than Retain.");
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
            if (!ProviderReleaseStateMachine.TryBeginRelease(ref _releaseState))
            {
                return;
            }

            _completion.TryCancelByOwner();
            AssetHandle raw = Volatile.Read(ref _raw);
            try
            {
                if (raw != null && raw.IsValid)
                {
                    raw.Dispose();
                }
            }
            catch
            {
                ProviderReleaseStateMachine.MarkReleaseFailed(ref _releaseState);
                throw;
            }

            Interlocked.CompareExchange(ref _raw, null, raw);
            Volatile.Write(ref _bytesSnapshot, null);
            Volatile.Write(ref _textSnapshot, string.Empty);
            _onReleaseToCache = null;
            ProviderReleaseStateMachine.MarkReleased(ref _releaseState);
            HandleTracker.Unregister(_id);
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState);

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
        private static readonly LogChannel Log = AssetManagementYooAssetLog.Channel;

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly AssetOperationCompletion _completion;
        private InstantiateOperation _raw;
        private YooAssetHandle<GameObject> _source;
        private Action<long> _onDisposed;
        private int _refCount;
        private int _callerDisposed;
        private int _releaseState;

        private YooInstantiateHandle(
            long id,
            InstantiateOperation raw,
            YooAssetHandle<GameObject> source,
            Action<long> onDisposed,
            AssetOperationTailTracker operationTails)
        {
            _id = id;
            _raw = raw;
            _completion = AssetOperationCompletion.Start(CompleteAsync(raw), operationTails);
            _source = source;
            _onDisposed = onDisposed;
            _source.Retain();
            _refCount = 1;
        }

        private async UniTask CompleteAsync(InstantiateOperation raw)
        {
            try
            {
                await YooOperationTask.CompleteAsync(
                    raw,
                    "YooAsset failed to instantiate the requested asset.");
            }
            finally
            {
                // The currently supported provider cancellation is synchronous, but retain authoritative cleanup
                // if another compatible provider revision publishes an instance after the wrapper is retired.
                if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState) && raw?.Result != null)
                {
                    UnityEngine.Object.Destroy(raw.Result);
                }
            }
        }

        public static YooInstantiateHandle Create(
            long id,
            InstantiateOperation raw,
            YooAssetHandle<GameObject> source,
            Action<long> onDisposed,
            AssetOperationTailTracker operationTails) =>
            new YooInstantiateHandle(id, raw, source, onDisposed, operationTails);

        public bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;
        public float Progress => _raw?.Progress ?? 0f;
        public string Error => _raw?.Error ?? string.Empty;
        public UniTask Task => _completion.Task;
        public GameObject Instance => !ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState)
            ? _raw?.Result
            : null;
        public int RefCount => Volatile.Read(ref _refCount);
        public void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            YooSynchronousWait.EnsureTerminal(_completion.Task.Status);
        }
        public void Retain()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                Log.Error("[YooInstantiateHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[YooInstantiateHandle] Release called more times than Retain.");
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
                return;
            }

            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState) &&
                !ProviderReleaseStateMachine.IsReleased(ref _releaseState))
            {
                DisposeInternal();
            }
        }

        internal void DisposeInternal()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!ProviderReleaseStateMachine.TryBeginRelease(ref _releaseState))
            {
                return;
            }

            _completion.TryCancelByOwner();
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
            catch
            {
                ProviderReleaseStateMachine.MarkReleaseFailed(ref _releaseState);
                throw;
            }

            YooAssetHandle<GameObject> source = _source;
            _source = null;
            ExceptionDispatchInfo sourceReleaseFailure = null;
            try
            {
                source?.Release();
            }
            catch (Exception exception)
            {
                // The cache retains a failed source release in its retry registry. This instantiate handle no
                // longer owns the source reference and must not decrement its refcount a second time on retry.
                sourceReleaseFailure = ExceptionDispatchInfo.Capture(exception);
            }

            Action<long> onDisposed = _onDisposed;
            _onDisposed = null;
            _raw = null;
            ProviderReleaseStateMachine.MarkReleased(ref _releaseState);
            HandleTracker.Unregister(_id);
            onDisposed?.Invoke(_id);
            sourceReleaseFailure?.Throw();
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class YooSceneHandle : ISceneHandle, IReferenceCounted,
        ISceneTrackerHandleState, ITrackedAssetHandle
    {
        private static readonly LogChannel Log = AssetManagementYooAssetLog.Channel;

        private const float MANUAL_ACTIVATION_READY_PROGRESS = 0.9f;

        private readonly long _id;
        long ITrackedAssetHandle.DiagnosticHandleId => _id;
        private readonly AssetOperationCompletion _completion;
        private int _refCount;
        private int _releaseState;
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
        internal bool IsTerminallyReleased => ProviderReleaseStateMachine.IsReleased(ref _releaseState);
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
            bool activateOnLoad,
            AssetOperationTailTracker operationTails)
        {
            _id = id;
            OwnerToken = ownerToken ?? throw new ArgumentNullException(nameof(ownerToken));
            Raw = raw;
            _completion = AssetOperationCompletion.Start(CompleteLoadAsync(raw), operationTails);
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
            bool activateOnLoad,
            AssetOperationTailTracker operationTails) =>
            new YooSceneHandle(id, ownerToken, scenePath, raw, activateOnLoad, operationTails);

        private bool CanReadRaw => !ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState) &&
                                   Raw != null &&
                                   Raw.IsValid;

        public bool IsDone => _completion.Task.Status != UniTaskStatus.Pending;
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
        public UniTask Task => _completion.Task;
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
                if (ProviderReleaseStateMachine.IsReleased(ref _releaseState))
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
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
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
            if (ProviderReleaseStateMachine.IsReleased(ref _releaseState))
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
                // Provider invalidation is not proof that the Unity scene is absent. Another owner can
                // invalidate YooAsset state before this package observes SceneManager.sceneUnloaded.
                return false;
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

            UniTaskStatus taskStatus = _completion.Task.Status;
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

        public void Retain()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                Log.Error("[YooSceneHandle] Retain called on a disposed handle.");
                return;
            }

            Interlocked.Increment(ref _refCount);
        }

        public void Release()
        {
            if (ProviderReleaseStateMachine.IsOwnerRetired(ref _releaseState))
            {
                return;
            }

            int count = Interlocked.Decrement(ref _refCount);
            if (count < 0)
            {
                Interlocked.Increment(ref _refCount);
                Log.Error("[YooSceneHandle] Release called more times than Retain.");
            }
            else if (count == 0)
            {
                Log.Warning("[YooSceneHandle] Dispose releases caller ownership only. Use IAssetSceneLoader.UnloadSceneAsync to unload the scene.");
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
            if (!ProviderReleaseStateMachine.TryBeginRelease(ref _releaseState))
            {
                return;
            }

            _completion.TryCancelByOwner();
            try
            {
                if (!providerHandleAlreadyReleased && Raw != null && Raw.IsValid)
                {
                    _progress = Raw.Progress;
                    _error = Raw.Error ?? string.Empty;
                    _scene = Raw.SceneObject;
                    Raw.Dispose();
                }
            }
            catch
            {
                ProviderReleaseStateMachine.MarkReleaseFailed(ref _releaseState);
                throw;
            }

            Raw = null;

            _scenePath = string.Empty;
            _activationTask = default;
            _unloadTask = default;
            ProviderReleaseStateMachine.MarkReleased(ref _releaseState);
            Interlocked.Exchange(ref _refCount, 0);
            SceneTracker.Unregister(_id);
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
