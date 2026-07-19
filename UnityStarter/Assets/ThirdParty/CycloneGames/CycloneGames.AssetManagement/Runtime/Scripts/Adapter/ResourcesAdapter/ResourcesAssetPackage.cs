using System;
using System.Collections.Generic;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class ResourcesAssetPackage : IAssetPackage, IAssetSyncOperations, IAssetRuntimeDiagnostics
    {
        private readonly string _packageName;

        public string Name => _packageName;

        private readonly Cache.AssetCacheService _cacheService;
        private readonly Dictionary<long, ResourcesInstantiateHandle> _instantiateHandles =
            new Dictionary<long, ResourcesInstantiateHandle>();
        private readonly Action<long> _onInstantiateDisposed;
        private bool _initialized;
        private bool _destroyed;
        private bool _destroying;

        public ResourcesAssetPackage(string name)
        {
            _packageName = name;
            _cacheService = new Cache.AssetCacheService(this);
            _onInstantiateDisposed = OnInstantiateDisposed;
        }

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfShutdownRequested();
            if (_initialized)
            {
                return UniTask.FromResult(true);
            }

            if (options.ProviderOptions != null)
            {
                throw new ArgumentException(
                    "Resources package initialization does not accept provider options.",
                    nameof(options));
            }

            if (options.CacheTuningOverride.HasValue)
            {
                _cacheService.Configure(options.CacheTuningOverride.Value);
            }

            _initialized = true;
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_destroyed || _destroying)
            {
                return UniTask.CompletedTask;
            }

            _destroying = true;
            try
            {
                List<Exception> failures = null;
                var instances = new List<ResourcesInstantiateHandle>(_instantiateHandles.Values);
                for (int i = 0; i < instances.Count; i++)
                {
                    try
                    {
                        instances[i].DisposeInternal();
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                    }
                }

                _instantiateHandles.Clear();
                try
                {
                    _cacheService.Dispose();
                }
                catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                {
                    failures ??= new List<Exception>();
                    failures.Add(ex);
                }

                _initialized = false;
                _destroyed = true;
                if (failures != null)
                {
                    throw new AggregateException(
                        $"Resources package '{_packageName}' failed to release one or more owned resources.",
                        failures);
                }

                return UniTask.CompletedTask;
            }
            finally
            {
                _destroying = false;
            }
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location, string bucket = null, string tag = null, string owner = null) where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateLocation(location);
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), AssetCacheEntryKind.Asset);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return AssetHandleLeases.Create((IAssetHandle<TAsset>)cached);

            var asset = Resources.Load<TAsset>(location);
            long id = RegisterHandle();
            var handle = ResourcesAssetHandle<TAsset>.Create(
                id,
                cacheKey,
                asset,
                _cacheService.OnHandleReleased,
                this);
            if (HandleTracker.Enabled) HandleTracker.Register(id, _packageName, $"AssetSync {typeof(TAsset).Name} : {location}");
            handle = (ResourcesAssetHandle<TAsset>)_cacheService.RegisterNew(cacheKey, bucket, tag, owner, handle);
            return AssetHandleLeases.Create(handle);
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, string bucket = null, string tag = null, string owner = null, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            ValidateLocation(location);
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), AssetCacheEntryKind.Asset);
            var cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null) return AssetHandleLeases.Create((IAssetHandle<TAsset>)cached, cancellationToken);

            var request = Resources.LoadAsync<TAsset>(location);
            long id = RegisterHandle();
            var handle = ResourcesAssetHandle<TAsset>.Create(
                id,
                cacheKey,
                request,
                _cacheService.OnHandleReleased,
                this);
            if (HandleTracker.Enabled) HandleTracker.Register(id, _packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            handle = (ResourcesAssetHandle<TAsset>)_cacheService.RegisterNew(cacheKey, bucket, tag, owner, handle);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(handle, location);
#endif
            return AssetHandleLeases.Create(handle, cancellationToken);
        }

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            if (handle is not IAssetHandleLease lease ||
                !lease.TryGetBackend<ResourcesAssetHandle<GameObject>>(out ResourcesAssetHandle<GameObject> backend) ||
                !ReferenceEquals(backend.Owner, this))
            {
                throw new ArgumentException(
                    "The handle is not an active Resources GameObject lease owned by this package.",
                    nameof(handle));
            }

            if (backend.Task.Status != UniTaskStatus.Succeeded || backend.Asset == null)
            {
                throw new InvalidOperationException(
                    "The Resources GameObject lease must complete successfully before instantiation.");
            }

            GameObject instance = GameObject.Instantiate(backend.Asset, parent, worldPositionStays);
            if (instance != null) instance.SetActive(setActive);
            long id = RegisterHandle();
            var wrapped = ResourcesInstantiateHandle.Create(id, instance, _onInstantiateDisposed);
            _instantiateHandles.Add(id, wrapped);
            if (HandleTracker.Enabled) HandleTracker.Register(id, _packageName, $"InstantiateAsync : {handle?.AssetObject?.name ?? "null"}");
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(wrapped, handle?.AssetObject?.name ?? "unknown");
#endif
            return wrapped;
        }

        public async UniTask UnloadUnusedAssetsAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            _cacheService.ClearAll();
            CLogger.LogWarning("[ResourcesAssetPackage] UnloadUnusedAssetsAsync triggers Resources.UnloadUnusedAssets(). This can cause hitches on the main thread, so prefer explicit handle release and bucket clears whenever possible.");
            await Resources.UnloadUnusedAssets().ToUniTask();
        }

        public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            var cacheKey = Cache.AssetCacheService.BuildCacheKey(location, typeof(TAsset), AssetCacheEntryKind.Asset);
            return _cacheService.Contains(cacheKey);
        }

        public AssetRuntimeCacheSnapshot GetRuntimeCacheSnapshot()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            return _cacheService.CreateRuntimeSnapshot(_packageName, "Resources");
        }

        public void SetCacheIdleMemoryBudget(long maxIdleBytes)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            _cacheService.SetIdleMemoryBudget(maxIdleBytes);
        }

        public int TrimIdleCache(AssetCacheRetentionPolicy policy)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            return _cacheService.TrimIdle(policy);
        }

        public void ClearBucket(string bucket)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            _cacheService.ClearBucket(bucket);
        }

        public void ClearBucketsByPrefix(string bucketPrefix)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            _cacheService.ClearBucketsByPrefix(bucketPrefix);
        }

        private long RegisterHandle()
        {
            return AssetRuntimeGuard.NextHandleId();
        }

        private void OnInstantiateDisposed(long id)
        {
            _instantiateHandles.Remove(id);
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Asset location cannot be null or empty.", nameof(location));
            }
        }

        internal void ConfigureCache(AssetCacheTuning tuning)
        {
            ThrowIfShutdownRequested();
            _cacheService.Configure(tuning);
        }

        private void ThrowIfDestroyed()
        {
            ThrowIfShutdownRequested();
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    $"Resources package '{Name}' has not completed initialization.");
            }
        }

        private void ThrowIfShutdownRequested()
        {
            if (_destroyed || _destroying)
            {
                throw new ObjectDisposedException(nameof(ResourcesAssetPackage));
            }
        }
    }
}
