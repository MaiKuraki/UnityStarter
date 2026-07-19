#if CYCLONEGAMES_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceProviders;
using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    internal static class AddressablesOperationTask
    {
        public static async UniTask CompleteAsync<T>(
            AsyncOperationHandle<T> operation,
            string fallbackError)
        {
            if (!operation.IsValid())
            {
                throw new InvalidOperationException($"{fallbackError} The provider handle is invalid.");
            }

            if (!operation.IsDone)
            {
                await operation.ToUniTask();
            }

            if (!operation.IsValid())
            {
                throw new InvalidOperationException($"{fallbackError} The provider handle became invalid before completion.");
            }

            if (operation.Status != AsyncOperationStatus.Succeeded)
            {
                Exception providerException = operation.OperationException;
                string message = providerException?.Message;
                throw new InvalidOperationException(
                    string.IsNullOrEmpty(message) ? fallbackError : message,
                    providerException);
            }
        }
    }

    internal static class AddressablesSynchronousWait
    {
        public static void Complete<T>(AsyncOperationHandle<T> operation)
        {
            if (!operation.IsValid() || operation.IsDone)
            {
                return;
            }

#if UNITY_WEBGL
            throw new PlatformNotSupportedException(
                "Pending Addressables operations cannot be completed synchronously on WebGL. Await IOperation.Task instead.");
#else
            throw new NotSupportedException(
                "Pending Addressables operations cannot be completed synchronously through this adapter. " +
                "WaitForCompletion can stall every active Addressables load and is unsafe for remote bundles; await IOperation.Task instead.");
#endif
        }
    }

    internal abstract class AddressablesOperationHandle : IOperation, ITrackedAssetHandle
    {
        protected long Id;
        long ITrackedAssetHandle.DiagnosticHandleId => Id;

        public abstract bool IsDone { get; }
        public abstract float Progress { get; }
        public abstract string Error { get; }
        public abstract UniTask Task { get; }
        public abstract void WaitForAsyncComplete();

        protected void SetId(long id)
        {
            Id = id;
        }
    }

    internal sealed class AddressableAssetHandle<TAsset> : AddressablesOperationHandle,
        IAssetHandle<TAsset>, IReferenceCounted, IInternalCacheable, IAssetMemoryFootprint,
        IAssetBackendLifetime
        where TAsset : UnityEngine.Object
    {
        internal AsyncOperationHandle<TAsset> Raw;
        internal string Location { get; private set; }
        internal object Owner { get; private set; }

        private Cache.AssetCacheKey _cacheKey;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private UniTask _task;
        private int _refCount;
        private int _disposed;

        public override bool IsDone => _task.Status != UniTaskStatus.Pending;
        public override float Progress => Raw.IsValid() ? Raw.PercentComplete : 0f;
        public override string Error => Raw.IsValid() ? Raw.OperationException?.Message ?? string.Empty : string.Empty;
        public override UniTask Task => _task;
        public TAsset Asset => Raw.IsValid() ? Raw.Result : null;
        public UnityEngine.Object AssetObject => Asset;
        public int RefCount => Volatile.Read(ref _refCount);

        public static AddressableAssetHandle<TAsset> Create(
            long id,
            object owner,
            Cache.AssetCacheKey cacheKey,
            string location,
            AsyncOperationHandle<TAsset> raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache)
        {
            var handle = new AddressableAssetHandle<TAsset>
            {
                Id = id,
                Owner = owner,
                _cacheKey = cacheKey,
                Location = location,
                Raw = raw,
                _onReleaseToCache = onReleaseToCache,
                _refCount = 1,
            };
            handle._task = AssetOperationBroadcast.Create(AddressablesOperationTask.CompleteAsync(
                raw,
                $"Addressables failed to load asset '{location}'."));
            return handle;
        }

        public override void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            AddressablesSynchronousWait.Complete(Raw);
        }

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                CLogger.LogError("[AddressableAssetHandle] Retain called on a disposed handle.");
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

            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableAssetHandle] Release called more times than Retain.");
                return;
            }

            if (newCount == 0)
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

            HandleTracker.Unregister(Id);
            if (Raw.IsValid())
            {
                Addressables.Release(Raw);
            }

            Raw = default;
            Owner = null;
            Location = null;
            _cacheKey = default;
            _onReleaseToCache = null;
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;
        long IAssetMemoryFootprint.EstimateRuntimeBytes() =>
            Raw.IsValid() ? Cache.AssetMemoryEstimator.Estimate(Raw.Result) : 0L;
    }

    internal sealed class AddressableAllAssetsHandle<TAsset> : AddressablesOperationHandle,
        IAllAssetsHandle<TAsset>, IReferenceCounted, IInternalCacheable, IAssetMemoryFootprint,
        IAssetBackendLifetime
        where TAsset : UnityEngine.Object
    {
        private sealed class ReadOnlyListAdapter : IReadOnlyList<TAsset>
        {
            private IList<TAsset> _source;

            public TAsset this[int index] => _source[index];
            public int Count => _source?.Count ?? 0;
            public void SetSource(IList<TAsset> source) => _source = source;
            public void Clear() => _source = null;
            public IEnumerator<TAsset> GetEnumerator() => (_source ?? Array.Empty<TAsset>()).GetEnumerator();
            System.Collections.IEnumerator System.Collections.IEnumerable.GetEnumerator() => GetEnumerator();
        }

        private AsyncOperationHandle<IList<TAsset>> _raw;
        private readonly ReadOnlyListAdapter _assets = new ReadOnlyListAdapter();
        private Cache.AssetCacheKey _cacheKey;
        private Action<Cache.AssetCacheKey, IReferenceCounted> _onReleaseToCache;
        private UniTask _task;
        private int _refCount;
        private int _disposed;

        public override bool IsDone => _task.Status != UniTaskStatus.Pending;
        public override float Progress => _raw.IsValid() ? _raw.PercentComplete : 0f;
        public override string Error => _raw.IsValid() ? _raw.OperationException?.Message ?? string.Empty : string.Empty;
        public override UniTask Task => _task;
        public IReadOnlyList<TAsset> Assets
        {
            get
            {
                _assets.SetSource(_raw.IsValid() && _raw.IsDone ? _raw.Result : null);
                return _assets;
            }
        }
        public int RefCount => Volatile.Read(ref _refCount);

        public static AddressableAllAssetsHandle<TAsset> Create(
            long id,
            Cache.AssetCacheKey cacheKey,
            AsyncOperationHandle<IList<TAsset>> raw,
            Action<Cache.AssetCacheKey, IReferenceCounted> onReleaseToCache)
        {
            var handle = new AddressableAllAssetsHandle<TAsset>
            {
                Id = id,
                _cacheKey = cacheKey,
                _raw = raw,
                _onReleaseToCache = onReleaseToCache,
                _refCount = 1,
            };
            handle._task = AssetOperationBroadcast.Create(AddressablesOperationTask.CompleteAsync(
                raw,
                "Addressables failed to load the requested asset collection."));
            return handle;
        }

        public override void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            AddressablesSynchronousWait.Complete(_raw);
        }

        public void Retain()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                CLogger.LogError("[AddressableAllAssetsHandle] Retain called on a disposed handle.");
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

            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableAllAssetsHandle] Release called more times than Retain.");
                return;
            }

            if (newCount == 0)
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

            HandleTracker.Unregister(Id);
            if (_raw.IsValid())
            {
                Addressables.Release(_raw);
            }

            _raw = default;
            _assets.Clear();
            _cacheKey = default;
            _onReleaseToCache = null;
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
        bool IAssetBackendLifetime.IsDisposed => Volatile.Read(ref _disposed) != 0;

        long IAssetMemoryFootprint.EstimateRuntimeBytes()
        {
            if (!_raw.IsValid() || _raw.Result == null)
            {
                return 0L;
            }

            long total = 0L;
            IList<TAsset> all = _raw.Result;
            for (int i = 0; i < all.Count; i++)
            {
                if (!Cache.AssetMemoryEstimator.TryAddToAggregate(all[i], ref total))
                {
                    return 0L;
                }
            }

            return total;
        }
    }

    internal sealed class AddressableInstantiateHandle : AddressablesOperationHandle,
        IInstantiateHandle, IReferenceCounted, IInternalCacheable
    {
        private AsyncOperationHandle<GameObject> _raw;
        private UniTask _task;
        private Action<long> _onDisposed;
        private bool _setActive;
        private int _refCount;
        private int _callerDisposed;
        private int _disposed;

        public override bool IsDone => _task.Status != UniTaskStatus.Pending;
        public override float Progress => _raw.IsValid() ? _raw.PercentComplete : 0f;
        public override string Error => _raw.IsValid() ? _raw.OperationException?.Message ?? string.Empty : string.Empty;
        public override UniTask Task => _task;
        public GameObject Instance => _raw.IsValid() ? _raw.Result : null;
        public int RefCount => Volatile.Read(ref _refCount);

        public static AddressableInstantiateHandle Create(
            long id,
            AsyncOperationHandle<GameObject> raw,
            bool setActive,
            Action<long> onDisposed)
        {
            var handle = new AddressableInstantiateHandle
            {
                Id = id,
                _raw = raw,
                _onDisposed = onDisposed,
                _setActive = setActive,
                _refCount = 1,
            };
            handle._task = AssetOperationBroadcast.Create(handle.CompleteAsync());
            return handle;
        }

        public override void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (!_raw.IsValid())
            {
                return;
            }

            AddressablesSynchronousWait.Complete(_raw);
            if (_raw.IsDone && _raw.Status == AsyncOperationStatus.Succeeded)
            {
                ApplyActiveState();
            }
        }

        private async UniTask CompleteAsync()
        {
            await AddressablesOperationTask.CompleteAsync(
                _raw,
                "Addressables failed to instantiate the requested asset.");
            ApplyActiveState();
        }

        private void ApplyActiveState()
        {
            if (!_setActive && _raw.IsValid() && _raw.Result != null)
            {
                _raw.Result.SetActive(false);
            }
        }

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableInstantiateHandle] Release called more times than Retain.");
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

            HandleTracker.Unregister(Id);
            try
            {
                if (_raw.IsValid())
                {
                    Addressables.Release(_raw);
                }
            }
            finally
            {
                Action<long> onDisposed = _onDisposed;
                _onDisposed = null;
                _raw = default;
                onDisposed?.Invoke(Id);
            }
        }

        void IInternalCacheable.ForceDispose() => DisposeInternal();
    }

    internal sealed class AddressableSceneHandle : AddressablesOperationHandle,
        ISceneHandle, IReferenceCounted
    {
        internal AsyncOperationHandle<SceneInstance> Raw;
        internal long DebugId => Id;
        internal object OwnerToken { get; private set; }
        internal bool UnloadStarted => _unloadStarted;
        internal bool IsProviderHandleReleased => !Raw.IsValid();
        internal bool IsTerminallyReleased => Volatile.Read(ref _disposed) != 0;
        internal bool RequiresShutdownActivation
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                if (IsTerminallyReleased ||
                    _providerSceneUnloaded ||
                    ActivationMode != SceneActivationMode.Manual)
                {
                    return false;
                }

                RefreshActivationState();
                if (_activationState == SceneActivationState.Activated)
                {
                    return false;
                }

                return !Raw.IsValid() ||
                       !Raw.IsDone ||
                       Raw.Status != AsyncOperationStatus.Failed;
            }
        }

        private int _refCount;
        private int _disposed;
        private SceneActivationState _activationState;
        private bool _activationStarted;
        private UniTask _activationTask;
        private bool _unloadStarted;
        private UniTask _unloadTask;
        private UniTask _task;
        private Scene _scene;
        private int _callerDisposed;
        private string _lifecycleError = string.Empty;
        private bool _providerSceneUnloaded;

        public override bool IsDone => _task.Status != UniTaskStatus.Pending;
        public override float Progress
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return Raw.IsValid() ? Raw.PercentComplete : 0f;
            }
        }
        public override string Error
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                if (!string.IsNullOrEmpty(_lifecycleError))
                {
                    return _lifecycleError;
                }

                return Raw.IsValid() ? Raw.OperationException?.Message ?? string.Empty : string.Empty;
            }
        }
        public override UniTask Task => _task;
        public string ScenePath { get; private set; }
        public Scene Scene
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                TryCaptureScene();
                return _scene;
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

        public static AddressableSceneHandle Create(
            long id,
            object ownerToken,
            string scenePath,
            AsyncOperationHandle<SceneInstance> raw,
            bool activateOnLoad)
        {
            var handle = new AddressableSceneHandle
            {
                Id = id,
                OwnerToken = ownerToken ?? throw new ArgumentNullException(nameof(ownerToken)),
                Raw = raw,
                ScenePath = scenePath,
                ActivationMode = activateOnLoad ? SceneActivationMode.ActivateOnLoad : SceneActivationMode.Manual,
                _activationState = SceneActivationState.Loading,
                _refCount = 1,
            };
            handle._task = AssetOperationBroadcast.Create(handle.CompleteLoadAsync(
                raw,
                scenePath));
            return handle;
        }

        private async UniTask CompleteLoadAsync(
            AsyncOperationHandle<SceneInstance> raw,
            string scenePath)
        {
            await AddressablesOperationTask.CompleteAsync(
                raw,
                $"Addressables failed to load scene '{scenePath}'.");
            TryCaptureScene();
        }

        private bool TryCaptureScene()
        {
            if (_scene.IsValid())
            {
                return true;
            }

            if (!Raw.IsValid() || !Raw.IsDone || Raw.Status != AsyncOperationStatus.Succeeded)
            {
                return false;
            }

            Scene providerScene = Raw.Result.Scene;
            if (!providerScene.IsValid())
            {
                return false;
            }

            _scene = providerScene;
            return true;
        }

        internal bool MatchesScene(Scene scene)
        {
            return scene.IsValid() && TryCaptureScene() && _scene == scene;
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
            // Addressables owns automatic provider-handle release for Single-mode and external unloads.
            // Only invalidate this wrapper; releasing Raw here can double-release the provider operation.
            OnProviderSceneUnloadObserved(scene);
            DisposeInternal();
        }

        public override void WaitForAsyncComplete()
        {
            AssetRuntimeGuard.EnsureMainThread();
            AddressablesSynchronousWait.Complete(Raw);
        }

        public UniTask ActivateAsync(CancellationToken cancellationToken = default)
        {
            return StartOrJoinActivation(cancellationToken, allowStartDuringUnload: false);
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
                // A load that became terminally failed or a scene retired by Single/external unload no longer
                // owns Unity's manual activation barrier. Any still-unresolved state must stop shutdown before
                // a new unload operation is queued behind that barrier.
                if (RequiresShutdownActivation)
                {
                    throw;
                }
            }
        }

        private UniTask StartOrJoinActivation(
            CancellationToken cancellationToken,
            bool allowStartDuringUnload)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AddressableSceneHandle));
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

            if (_unloadStarted && !allowStartDuringUnload)
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

        private async UniTask ActivateCoreAsync()
        {
            try
            {
                // The broadcast task is repeatable. Always await it so a load that already faulted cannot be
                // mistaken for a completed, activatable scene.
                await Task;

                if (!Raw.IsValid() || Raw.Status != AsyncOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException("Addressables scene handle became invalid before activation.");
                }

                if (ActivationMode == SceneActivationMode.Manual)
                {
                    AsyncOperation operation = Raw.Result.ActivateAsync();
                    while (operation != null && !operation.isDone)
                    {
                        await UniTask.Yield();
                    }
                }

                EnsureActivationCompletionStillOwned();

                _activationState = SceneActivationState.Activated;
            }
            catch
            {
                _activationStarted = false;
                throw;
            }
        }

        private void EnsureActivationCompletionStillOwned()
        {
            if (Volatile.Read(ref _disposed) != 0 ||
                _providerSceneUnloaded ||
                !Raw.IsValid() ||
                Raw.Status != AsyncOperationStatus.Succeeded ||
                !TryCaptureScene() ||
                !_scene.isLoaded)
            {
                throw new InvalidOperationException(
                    "Addressables scene activation completed after scene ownership had already been retired.");
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
                SceneTracker.MarkUnloadRequested(Id);
                _unloadTask = AssetOperationBroadcast.Create(UnloadCoreAsync());
            }

            return _unloadTask;
        }

        private async UniTask UnloadCoreAsync()
        {
            AsyncOperationHandle<SceneInstance> unloadOperation = default;
            try
            {
                if (Raw.IsValid() && Raw.IsDone && Raw.Status == AsyncOperationStatus.Failed)
                {
                    Addressables.Release(Raw);
                    Raw = default;
                    DisposeInternal();
                    return;
                }

                if (ActivationMode == SceneActivationMode.Manual && ActivationState != SceneActivationState.Activated)
                {
                    try
                    {
                        await StartOrJoinActivation(
                            CancellationToken.None,
                            allowStartDuringUnload: true);
                    }
                    catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
                    {
                        if (IsKnownSceneAbsent())
                        {
                            DisposeInternal();
                            return;
                        }

                        if (Raw.IsValid() && Raw.IsDone && Raw.Status == AsyncOperationStatus.Failed)
                        {
                            Addressables.Release(Raw);
                            Raw = default;
                            DisposeInternal();
                            return;
                        }

                        throw;
                    }
                }

                if (Raw.IsValid())
                {
                    unloadOperation = Addressables.UnloadSceneAsync(Raw, autoReleaseHandle: false);
                    while (!unloadOperation.IsDone)
                    {
                        await UniTask.Yield();
                    }
                    if (unloadOperation.Status != AsyncOperationStatus.Succeeded)
                    {
                        throw new InvalidOperationException(
                            unloadOperation.OperationException?.Message ?? "Addressables scene unload failed.",
                            unloadOperation.OperationException);
                    }
                }

                DisposeInternal();
            }
            catch (Exception exception)
            {
                if (AssetRuntimeGuard.IsRecoverableException(exception) && IsKnownSceneAbsent())
                {
                    DisposeInternal();
                    return;
                }

                _unloadStarted = false;
                if (AssetRuntimeGuard.IsRecoverableException(exception))
                {
                    _lifecycleError = exception.Message;
                    SceneTracker.MarkUnloadFailed(Id, _lifecycleError);
                }
                throw;
            }
            finally
            {
                if (unloadOperation.IsValid())
                {
                    Addressables.Release(unloadOperation);
                }
            }
        }

        private bool IsKnownSceneAbsent()
        {
            if (_scene.IsValid() && _scene.isLoaded)
            {
                return false;
            }

            return _providerSceneUnloaded || !Raw.IsValid();
        }

        private void RefreshActivationState()
        {
            if (_activationState != SceneActivationState.Loading ||
                _task.Status != UniTaskStatus.Succeeded ||
                !Raw.IsValid() ||
                Raw.Status != AsyncOperationStatus.Succeeded)
            {
                return;
            }

            _activationState = ActivationMode == SceneActivationMode.Manual && !_activationStarted
                ? SceneActivationState.WaitingForActivation
                : ActivationMode == SceneActivationMode.ActivateOnLoad
                    ? SceneActivationState.Activated
                    : SceneActivationState.Loading;
        }

        public void Retain() => Interlocked.Increment(ref _refCount);

        public void Release()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                return;
            }

            int newCount = Interlocked.Decrement(ref _refCount);
            if (newCount < 0)
            {
                Interlocked.Increment(ref _refCount);
                CLogger.LogError("[AddressableSceneHandle] Release called more times than Retain.");
            }
            else if (newCount == 0)
            {
                CLogger.LogWarning("[AddressableSceneHandle] Dispose releases caller ownership only. Use IAssetSceneLoader.UnloadSceneAsync to unload the scene.");
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

            SceneTracker.Unregister(Id);
            HandleTracker.Unregister(Id);
            Interlocked.Exchange(ref _refCount, 0);
            Raw = default;
            ScenePath = null;
            _activationTask = default;
            _unloadTask = default;
        }
    }

    internal sealed class AddressableDownloader : IDownloader
    {
        private static readonly long DOWNLOAD_STATUS_SAMPLE_INTERVAL_TICKS =
            Math.Max(1L, Stopwatch.Frequency / 4L);

        private readonly string[] _keys;
        private readonly CancellationTokenSource _sharedCancellation = new CancellationTokenSource();
        private readonly UniTaskCompletionSource _disposeCompletion = new UniTaskCompletionSource();
        private Action<AddressableDownloader> _onDisposed;
        private AsyncOperationHandle<long> _sizeOperation;
        private AsyncOperationHandle _downloadOperation;
        private bool _prepareStarted;
        private bool _prepared;
        private UniTask _prepareTask;
        private UniTask _prepareCallerTask;
        private bool _startStarted;
        private UniTask _startTask;
        private UniTask _startCallerTask;
        private int _disposed;
        private bool _cancelled;
        private bool _succeeded;
        private float _progress;
        private long _totalBytes;
        private long _downloadedBytes;
        private long _nextDownloadStatusSampleTimestamp;
        private bool _hasTerminalDownloadSnapshot;
        private string _error = string.Empty;

        public AddressableDownloader(
            string[] keys,
            Action<AddressableDownloader> onDisposed)
        {
            _keys = keys ?? throw new ArgumentNullException(nameof(keys));
            _onDisposed = onDisposed;
            if (_keys.Length == 0)
            {
                _prepared = true;
                _succeeded = true;
                _progress = 1f;
            }
        }

        internal int ScopeValueCount => _keys.Length;

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
                return SucceedOnMainThread();
            }
        }

        public float Progress
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return CanReadDownloadOperation() && _downloadOperation.IsValid()
                    ? _downloadOperation.PercentComplete
                    : _progress;
            }
        }

        // Addressables does not expose dependency bundle count on the operation handle.
        // Report one aggregate work item so provider-neutral workflows do not mistake it for an empty download.
        public int TotalDownloadCount
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return TotalDownloadCountOnMainThread();
            }
        }

        public int CurrentDownloadCount
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return TotalDownloadCountOnMainThread() != 0 &&
                       IsDoneOnMainThread() &&
                       SucceedOnMainThread()
                    ? 1
                    : 0;
            }
        }

        public long TotalDownloadBytes
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _totalBytes;
            }
        }

        public long CurrentDownloadBytes
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                RefreshDownloadSnapshotIfDue();
                return _downloadedBytes;
            }
        }

        public string Error
        {
            get
            {
                AssetRuntimeGuard.EnsureMainThread();
                return _cancelled
                    ? "Cancelled"
                    : _downloadOperation.IsValid()
                        ? _downloadOperation.OperationException?.Message ?? string.Empty
                        : _error;
            }
        }

        public UniTask PrepareAsync(CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            if (_cancelled)
            {
                throw new OperationCanceledException("The Addressables download was cancelled.");
            }

            if (_prepared)
            {
                return UniTask.CompletedTask;
            }

            if (!_prepareStarted)
            {
                _prepareStarted = true;
                _prepareTask = AssetOperationBroadcast.Create(PrepareCoreAsync());
                _prepareCallerTask = AssetOperationBroadcast.CreateCallerView(
                    _prepareTask,
                    _sharedCancellation.Token);
            }

            return CreateWaitView(_prepareCallerTask, cancellationToken);
        }

        private async UniTask PrepareCoreAsync()
        {
            try
            {
                _sizeOperation = Addressables.GetDownloadSizeAsync(_keys);
                await _sizeOperation.ToUniTask();
                if (_cancelled)
                {
                    throw new OperationCanceledException("The Addressables download was cancelled.");
                }

                if (!_sizeOperation.IsValid() || _sizeOperation.Status != AsyncOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException(
                        _sizeOperation.IsValid()
                            ? _sizeOperation.OperationException?.Message ?? "Addressables download-size query failed."
                            : "Addressables download-size handle became invalid.");
                }

                _totalBytes = Math.Max(0L, _sizeOperation.Result);
                _prepared = true;
                if (_totalBytes == 0L)
                {
                    _progress = 1f;
                    _succeeded = true;
                }
            }
            catch (Exception exception) when (
                _cancelled && AssetRuntimeGuard.IsRecoverableException(exception))
            {
                throw new OperationCanceledException("The Addressables download was cancelled.");
            }
            catch (Exception exception) when (
                Volatile.Read(ref _disposed) != 0 && AssetRuntimeGuard.IsRecoverableException(exception))
            {
                throw new ObjectDisposedException(nameof(AddressableDownloader));
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                if (string.IsNullOrEmpty(_error))
                {
                    _error = $"Addressables download-size query failed ({exception.GetType().Name}).";
                }

                _prepareStarted = false;
                throw;
            }
            finally
            {
                ReleaseSizeOperation();
            }
        }

        public async UniTask StartAsync(CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDisposed();
            await PrepareAsync(cancellationToken);
            cancellationToken.ThrowIfCancellationRequested();
            if (_cancelled)
            {
                throw new OperationCanceledException("The Addressables download was cancelled.");
            }

            if (_totalBytes == 0L)
            {
                return;
            }

            if (!_startStarted)
            {
                _startStarted = true;
                _startTask = AssetOperationBroadcast.Create(StartCoreAsync());
                _startCallerTask = AssetOperationBroadcast.CreateCallerView(
                    _startTask,
                    _sharedCancellation.Token);
            }

            await CreateWaitView(_startCallerTask, cancellationToken);
        }

        private async UniTask StartCoreAsync()
        {
            try
            {
                _downloadOperation = Addressables.DownloadDependenciesAsync(
                    _keys,
                    Addressables.MergeMode.Union,
                    autoReleaseHandle: false);
                await _downloadOperation.ToUniTask();
                if (_cancelled)
                {
                    throw new OperationCanceledException("The Addressables download was cancelled.");
                }

                if (!_downloadOperation.IsValid() ||
                    _downloadOperation.Status != AsyncOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException(
                        _downloadOperation.IsValid()
                            ? _downloadOperation.OperationException?.Message ?? "Addressables dependency download failed."
                            : "Addressables dependency download handle became invalid.");
                }

                _succeeded = true;
                _progress = 1f;
                _downloadedBytes = _totalBytes;
            }
            catch (Exception exception) when (
                _cancelled && AssetRuntimeGuard.IsRecoverableException(exception))
            {
                throw new OperationCanceledException("The Addressables download was cancelled.");
            }
            catch (Exception exception) when (
                Volatile.Read(ref _disposed) != 0 && AssetRuntimeGuard.IsRecoverableException(exception))
            {
                throw new ObjectDisposedException(nameof(AddressableDownloader));
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                if (string.IsNullOrEmpty(_error))
                {
                    _error = $"Addressables dependency download failed ({ex.GetType().Name}).";
                }
                throw;
            }
            finally
            {
                ReleaseDownloadOperation();
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
                UniTask providerTask = _startStarted
                    ? _startTask
                    : _prepareStarted
                        ? _prepareTask
                        : UniTask.CompletedTask;
                try
                {
                    await providerTask;
                }
                catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
                {
                    // The caller-visible result was already published. Disposal only waits for the provider-owned
                    // operation to reach its terminal finally block so its handle can be released safely.
                }
            }
            finally
            {
                try
                {
                    _sharedCancellation.Dispose();
                }
                finally
                {
                    try
                    {
                        Action<AddressableDownloader> onDisposed = _onDisposed;
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

        private UniTask CreateWaitView(UniTask sharedTask, CancellationToken callerCancellation)
        {
            if (!callerCancellation.CanBeCanceled)
            {
                return sharedTask;
            }

            return WaitWithCallerCancellationOnMainThreadAsync(sharedTask, callerCancellation);
        }

        private static async UniTask WaitWithCallerCancellationOnMainThreadAsync(
            UniTask sharedTask,
            CancellationToken callerCancellation)
        {
            try
            {
                await AssetOperationBroadcast.CreateCallerView(sharedTask, callerCancellation);
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

        private void ReleaseDownloadOperation()
        {
            AsyncOperationHandle operation = _downloadOperation;
            try
            {
                CaptureDownloadSnapshot();
            }
            finally
            {
                _downloadOperation = default;
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }
            }
        }

        private void ReleaseSizeOperation()
        {
            AsyncOperationHandle<long> operation = _sizeOperation;
            _sizeOperation = default;
            if (operation.IsValid())
            {
                Addressables.Release(operation);
            }
        }

        private void CaptureDownloadSnapshot()
        {
            if (!_downloadOperation.IsValid())
            {
                return;
            }

            try
            {
                DownloadStatus status = _downloadOperation.GetDownloadStatus();
                _totalBytes = Math.Max(_totalBytes, status.TotalBytes);
                _downloadedBytes = status.DownloadedBytes;
                _progress = _downloadOperation.PercentComplete;
                _succeeded = _downloadOperation.IsDone &&
                             _downloadOperation.Status == AsyncOperationStatus.Succeeded;
                string providerError = _downloadOperation.OperationException?.Message;
                if (!string.IsNullOrEmpty(providerError))
                {
                    _error = providerError;
                }
            }
            catch (Exception exception) when (AssetRuntimeGuard.IsRecoverableException(exception))
            {
                if (string.IsNullOrEmpty(_error))
                {
                    _error = $"Addressables download status sampling failed ({exception.GetType().Name}).";
                }
            }
            finally
            {
                _hasTerminalDownloadSnapshot = _downloadOperation.IsDone;
                _nextDownloadStatusSampleTimestamp = Stopwatch.GetTimestamp() +
                                                     DOWNLOAD_STATUS_SAMPLE_INTERVAL_TICKS;
            }
        }

        private void RefreshDownloadSnapshotIfDue()
        {
            if (!CanReadDownloadOperation() ||
                _hasTerminalDownloadSnapshot ||
                !_downloadOperation.IsValid())
            {
                return;
            }

            long timestamp = Stopwatch.GetTimestamp();
            if (!_downloadOperation.IsDone && timestamp < _nextDownloadStatusSampleTimestamp)
            {
                return;
            }

            CaptureDownloadSnapshot();
        }

        private bool IsDoneOnMainThread()
        {
            return _cancelled ||
                   Volatile.Read(ref _disposed) != 0 ||
                   HasTerminalCallerResult();
        }

        private bool SucceedOnMainThread()
        {
            if (_cancelled)
            {
                return false;
            }

            if (_startStarted)
            {
                return _startCallerTask.Status == UniTaskStatus.Succeeded && _succeeded;
            }

            return _prepared &&
                   _totalBytes == 0L &&
                   (!_prepareStarted || _prepareCallerTask.Status == UniTaskStatus.Succeeded);
        }

        private bool HasTerminalCallerResult()
        {
            if (_startStarted)
            {
                return _startCallerTask.Status != UniTaskStatus.Pending;
            }

            return _prepared &&
                   _totalBytes == 0L &&
                   (!_prepareStarted || _prepareCallerTask.Status != UniTaskStatus.Pending);
        }

        private int TotalDownloadCountOnMainThread()
        {
            return _prepared && _totalBytes > 0L ? 1 : 0;
        }

        private bool CanReadDownloadOperation()
        {
            return !_cancelled && Volatile.Read(ref _disposed) == 0;
        }

        private void ThrowIfDisposed()
        {
            if (Volatile.Read(ref _disposed) != 0)
            {
                throw new ObjectDisposedException(nameof(AddressableDownloader));
            }
        }
    }
}
#endif
