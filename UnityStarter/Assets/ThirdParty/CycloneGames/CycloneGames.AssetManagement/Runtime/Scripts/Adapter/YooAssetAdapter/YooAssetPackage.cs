#if CYCLONEGAMES_HAS_YOOASSET
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;
using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;
using YooAsset;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class YooAssetPackage : IAssetPackage, IAssetSyncOperations, IAssetBulkLoader,
        IAssetRawFileLoader, IAssetSceneLoader, IYooAssetPackageMaintenance,
        IAssetCatalogQuery, IAssetRuntimeDiagnostics, IAssetStoragePreflight
    {
        private const int MAX_BUNDLE_LOADING_CONCURRENCY = 64;
        private const int MAX_DOWNLOAD_CONCURRENCY = 32;
        private const int MAX_DOWNLOAD_RETRY_COUNT = 16;
        private const int MAX_MAINTENANCE_TIMEOUT_SECONDS = 3_600;
        private const int MAX_SCOPE_VALUE_COUNT = 65_536;
        private const int MAX_SCOPE_VALUE_LENGTH = 4_096;
        private const int MAX_SCOPE_TOTAL_CHARACTERS = 8 * 1024 * 1024;
        private const int MAX_REGISTERED_DOWNLOADERS = 128;
        private const int MAX_REGISTERED_DOWNLOADER_SCOPE_VALUES = 262_144;
        private const string DEFAULT_CACHE_FILE_SYSTEM_CLASS = "YooAsset.SandboxFileSystem";

        private readonly ResourcePackage _rawPackage;
        private readonly YooAssetModule _moduleOwner;
        private readonly Cache.AssetCacheService _cacheService;
        private readonly HashSet<YooDownloader> _downloaders = new HashSet<YooDownloader>();
        private readonly Dictionary<long, YooInstantiateHandle> _instantiateHandles =
            new Dictionary<long, YooInstantiateHandle>();
        private readonly Dictionary<long, YooSceneHandle> _sceneHandles =
            new Dictionary<long, YooSceneHandle>();
        private readonly List<long> _sceneUnloadScratchIds = new List<long>(4);
        private readonly object _sceneOwnerToken = new object();
        private readonly Action<long> _onInstantiateDisposed;
        private readonly Action<YooDownloader> _onDownloaderDisposed;
#if UNITY_STANDALONE || UNITY_EDITOR
        private string _storagePath;
        private bool _storageProbeReliable;
#endif
        private bool _initialized;
        private bool _initializing;
        private UniTask<bool> _initializeTask;
        private int _shutdownRequested;
        private bool _maintenanceMutationInProgress;
        private bool _sceneUnloadSubscribed;
        private bool _destroying;
        private UniTask _destroyTask;
        private int _providerDestroyed;
        private int _destroyed;
        private int _registeredDownloaderScopeValueCount;

        internal YooAssetPackage(ResourcePackage rawPackage, YooAssetModule moduleOwner)
        {
            _rawPackage = rawPackage ?? throw new ArgumentNullException(nameof(rawPackage));
            _moduleOwner = moduleOwner ?? throw new ArgumentNullException(nameof(moduleOwner));
            _cacheService = new Cache.AssetCacheService(this);
            _onInstantiateDisposed = OnInstantiateDisposed;
            _onDownloaderDisposed = OnDownloaderDisposed;
        }

        public string Name => _rawPackage.PackageName;
        public UniTask<bool> InitializeAsync(
            AssetPackageInitOptions options,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfShutdownRequested();
            if (_initialized)
            {
                return UniTask.FromResult(true);
            }

            if (_initializing)
            {
                return _initializeTask;
            }

            _initializing = true;
            _initializeTask = AssetOperationBroadcast.Create(InitializeCoreAsync(options));
            return _initializeTask;
        }

        private async UniTask<bool> InitializeCoreAsync(AssetPackageInitOptions options)
        {
            try
            {
                if (options.CacheTuningOverride.HasValue)
                {
                    _cacheService.Configure(options.CacheTuningOverride.Value);
                }

                if (options.ProviderOptions is not InitializePackageOptions initializeOptions)
                {
                    throw new ArgumentException(
                        "YooAsset initialization requires YooAsset.InitializePackageOptions.",
                        nameof(options));
                }

                if (initializeOptions.BundleLoadingMaxConcurrency == int.MaxValue)
                {
                    initializeOptions.BundleLoadingMaxConcurrency = AssetPlatformDefaults.BundleLoadingMaxConcurrency;
                }
                else if (initializeOptions.BundleLoadingMaxConcurrency <= 0 ||
                         initializeOptions.BundleLoadingMaxConcurrency > MAX_BUNDLE_LOADING_CONCURRENCY)
                {
                    throw new ArgumentOutOfRangeException(
                        nameof(initializeOptions.BundleLoadingMaxConcurrency),
                        $"Bundle loading concurrency must be between 1 and {MAX_BUNDLE_LOADING_CONCURRENCY}, or int.MaxValue to use the platform default.");
                }

                ConfigureStorageProbe(initializeOptions);
                InitializePackageOperation operation = _rawPackage.InitializePackageAsync(initializeOptions);
                // YooAsset cannot cancel initialization. Finish deterministically so callers never observe
                // cancellation while the package becomes initialized later in the background.
                await operation;
                _initialized = operation.Status == EOperationStatus.Succeeded;
                if (_initialized && Volatile.Read(ref _shutdownRequested) == 0)
                {
                    SubscribeToSceneUnloads();
                }

                return _initialized;
            }
            finally
            {
                _initializing = false;
            }
        }

        private void SubscribeToSceneUnloads()
        {
            if (_sceneUnloadSubscribed)
            {
                return;
            }

            SceneManager.sceneUnloaded += OnSceneUnloaded;
            _sceneUnloadSubscribed = true;
        }

        private void UnsubscribeFromSceneUnloads()
        {
            if (!_sceneUnloadSubscribed)
            {
                return;
            }

            SceneManager.sceneUnloaded -= OnSceneUnloaded;
            _sceneUnloadSubscribed = false;
        }

        private void OnSceneUnloaded(Scene scene)
        {
            _sceneUnloadScratchIds.Clear();
            foreach (KeyValuePair<long, YooSceneHandle> pair in _sceneHandles)
            {
                YooSceneHandle handle = pair.Value;
                if (!handle.IsProviderHandleReleased && !handle.MatchesScene(scene))
                {
                    continue;
                }

                if (handle.UnloadStarted)
                {
                    // Keep registry authority until the shared package unload completion resumes, but record the
                    // provider callback so activation/unload races can converge after YooAsset invalidates its handle.
                    handle.OnProviderSceneUnloadObserved(scene);
                    continue;
                }

                _sceneUnloadScratchIds.Add(pair.Key);
            }

            for (int i = 0; i < _sceneUnloadScratchIds.Count; i++)
            {
                long id = _sceneUnloadScratchIds[i];
                if (_sceneHandles.TryGetValue(id, out YooSceneHandle handle))
                {
                    _sceneHandles.Remove(id);
                    handle.OnProviderSceneUnloaded(scene);
                }
            }

            _sceneUnloadScratchIds.Clear();
        }

        public UniTask DestroyAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (Volatile.Read(ref _destroyed) != 0)
            {
                return UniTask.CompletedTask;
            }

            if (_destroying)
            {
                return _destroyTask;
            }

            _moduleOwner.ValidatePackageSceneDrainOrder(this);
            BeginModuleShutdown();
            if (_maintenanceMutationInProgress)
            {
                throw new InvalidOperationException(
                    "YooAsset package shutdown was requested during a manifest or cache mutation. Retry cleanup after the mutation completes.");
            }

            _destroying = true;
            _destroyTask = AssetOperationBroadcast.Create(DestroyCoreAsync());
            return _destroyTask;
        }

        internal void BeginModuleShutdown()
        {
            AssetRuntimeGuard.EnsureMainThread();
            Interlocked.Exchange(ref _shutdownRequested, 1);
            UnsubscribeFromSceneUnloads();
        }

        internal void CopyOwnedSceneHandlesTo(List<YooSceneHandle> destination)
        {
            if (destination == null)
            {
                throw new ArgumentNullException(nameof(destination));
            }

            foreach (YooSceneHandle handle in _sceneHandles.Values)
            {
                destination.Add(handle);
            }
        }

        internal bool HasOwnedScenes => _sceneHandles.Count != 0;

        internal bool TryGetUnresolvedManualScene(out long sceneId)
        {
            sceneId = 0L;
            foreach (YooSceneHandle handle in _sceneHandles.Values)
            {
                if (!handle.RequiresShutdownActivation)
                {
                    continue;
                }

                if (sceneId == 0L || handle.DebugId < sceneId)
                {
                    sceneId = handle.DebugId;
                }
            }

            return sceneId != 0L;
        }

        internal UniTask UnloadOwnedSceneForModuleShutdownAsync(YooSceneHandle sceneHandle)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (sceneHandle == null ||
                !_sceneHandles.TryGetValue(sceneHandle.DebugId, out YooSceneHandle registered) ||
                !ReferenceEquals(registered, sceneHandle))
            {
                return UniTask.CompletedTask;
            }

            return UnloadOwnedSceneAsync(sceneHandle, CancellationToken.None);
        }

        private async UniTask DestroyCoreAsync()
        {
            try
            {
                if (_initializing)
                {
                    try
                    {
                        await _initializeTask;
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        CLogger.LogWarning(
                            $"YooAsset package '{Name}' initialization failed while shutdown was waiting: {ex.Message}");
                    }
                }

                List<Exception> failures = null;
                var scenes = new List<YooSceneHandle>(_sceneHandles.Values);
                scenes.Sort((left, right) => left.DebugId.CompareTo(right.DebugId));
                bool manualBarriersResolved = true;
                for (int i = 0; i < scenes.Count; i++)
                {
                    try
                    {
                        await scenes[i].ResolveShutdownActivationAsync();
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                        manualBarriersResolved = false;
                        break;
                    }
                }

                // A manual scene load stalls Unity's global asynchronous scene queue. Resolve every manual load
                // owned by this package before creating any unload operation; unload creation order alone is not
                // sufficient when a later manual load is already holding the queue.
                for (int i = 0; manualBarriersResolved && i < scenes.Count; i++)
                {
                    YooSceneHandle scene = scenes[i];
                    try
                    {
                        await scene.UnloadAsync(CancellationToken.None);
                        _sceneHandles.Remove(scene.DebugId);
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                        // Preserve unload ordering after failure. The package remains owned and retryable.
                        break;
                    }
                }

                var instances = new List<YooInstantiateHandle>(_instantiateHandles.Values);
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

                var downloaders = new List<YooDownloader>(_downloaders);
                for (int i = 0; i < downloaders.Count; i++)
                {
                    try
                    {
                        downloaders[i].Dispose();
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                    }
                }

                // CancelDownload marks YooAsset's wrapper terminal and aborts active child requests. Keep package
                // ownership until each wrapper has resumed, observed that terminal state, and captured diagnostics.
                for (int i = 0; i < downloaders.Count; i++)
                {
                    try
                    {
                        await downloaders[i].WaitForDisposeCompletionAsync();
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                    }
                }

                // Shutdown is sticky. Outstanding cached handles must become invalid even if a later
                // scene or provider cleanup step needs a retry.
                _cacheService.Dispose();

                if (failures != null)
                {
                    throw new AggregateException(
                        $"YooAsset package '{Name}' failed to unload one or more owned scenes.",
                        failures);
                }

                if (Volatile.Read(ref _providerDestroyed) == 0)
                {
                    DestroyPackageOperation operation = _rawPackage.DestroyPackageAsync();
                    await operation;
                    if (operation.Status != EOperationStatus.Succeeded)
                    {
                        throw new InvalidOperationException(operation.Error ?? "YooAsset package destruction failed.");
                    }

                    Interlocked.Exchange(ref _providerDestroyed, 1);
                }

                YooAssets.RemovePackage(Name);

                _initialized = false;
                UnsubscribeFromSceneUnloads();
                Interlocked.Exchange(ref _destroyed, 1);
            }
            finally
            {
                _destroying = false;
            }
        }

        public async UniTask<bool> UpdatePackageManifestAsync(
            string packageVersion,
            int timeoutSeconds = 60,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            YooAssetStableToken.ValidatePackageVersion(packageVersion, nameof(packageVersion));

            ValidateTimeoutSeconds(timeoutSeconds);
            EnterMaintenanceMutation(nameof(UpdatePackageManifestAsync));
            try
            {
                LoadPackageManifestOperation operation = _rawPackage.LoadPackageManifestAsync(
                    new LoadPackageManifestOptions(packageVersion, timeoutSeconds));
                // Active-manifest mutation is not cancellable in YooAsset. Complete it before reporting a result.
                await operation;
                if (operation.Status != EOperationStatus.Succeeded)
                {
                    return false;
                }

                _cacheService.AdvanceGeneration();
                return true;
            }
            finally
            {
                ExitMaintenanceMutation();
            }
        }

        public UniTask<bool> ClearAllCacheFilesAsync(
            CancellationToken cancellationToken = default)
        {
            return ClearCacheFilesCoreAsync(
                ClearCacheMethods.ClearAllBundleFiles,
                null,
                cancellationToken);
        }

        public UniTask<bool> ClearUnusedCacheFilesAsync(
            CancellationToken cancellationToken = default)
        {
            return ClearCacheFilesCoreAsync(
                ClearCacheMethods.ClearUnusedBundleFiles,
                null,
                cancellationToken);
        }

        public UniTask<bool> ClearCacheFilesByTagsAsync(
            string[] tags,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            string[] validatedTags = CloneAndValidateScopeValues(tags, nameof(tags));
            return ClearCacheFilesCoreAsync(
                ClearCacheMethods.ClearBundleFilesByTags,
                validatedTags,
                cancellationToken);
        }

        private async UniTask<bool> ClearCacheFilesCoreAsync(
            string clearMethod,
            object clearParam,
            CancellationToken cancellationToken)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            EnterMaintenanceMutation(nameof(ClearCacheFilesCoreAsync));
            try
            {
                var options = clearParam == null
                    ? new ClearCacheOptions(clearMethod)
                    : new ClearCacheOptions(clearMethod, clearParam);
                ClearCacheOperation operation = _rawPackage.ClearCacheAsync(options);

                // Cache mutation is not cancellable in YooAsset; finish deterministically once started.
                await operation;
                return operation.Status == EOperationStatus.Succeeded;
            }
            finally
            {
                ExitMaintenanceMutation();
            }
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateDownloadControls(downloadingMaxNumber, failedTryAgain);
            EnsureDownloaderAdmission(0);
            return RegisterDownloader(new YooDownloader(
                _rawPackage.CreateResourceDownloader(
                    new ResourceDownloaderOptions(downloadingMaxNumber, failedTryAgain)),
                _onDownloaderDisposed,
                scopeValueCount: 0));
        }

        public IDownloader CreateDownloaderForTags(
            string[] tags,
            int downloadingMaxNumber,
            int failedTryAgain)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateDownloadControls(downloadingMaxNumber, failedTryAgain);
            EnsureDownloaderAdmission(0);
            string[] validatedTags = CloneAndValidateScopeValues(tags, nameof(tags));
            EnsureDownloaderAdmission(validatedTags.Length);
            return RegisterDownloader(new YooDownloader(
                _rawPackage.CreateResourceDownloader(
                    new ResourceDownloaderOptions(
                        validatedTags,
                        downloadingMaxNumber,
                        failedTryAgain)),
                _onDownloaderDisposed,
                validatedTags.Length));
        }

        public IDownloader CreateDownloaderForLocations(
            string[] locations,
            bool recursiveDownload,
            int downloadingMaxNumber,
            int failedTryAgain)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateDownloadControls(downloadingMaxNumber, failedTryAgain);
            EnsureDownloaderAdmission(0);
            string[] validatedLocations = CloneAndValidateScopeValues(locations, nameof(locations));
            EnsureDownloaderAdmission(validatedLocations.Length);
            var assetInfos = new AssetInfo[validatedLocations.Length];
            for (int i = 0; i < validatedLocations.Length; i++)
            {
                assetInfos[i] = _rawPackage.GetAssetInfo(validatedLocations[i]);
                if (assetInfos[i] == null || !assetInfos[i].IsValid)
                {
                    throw new ArgumentException(
                        $"YooAsset could not resolve downloader location '{validatedLocations[i]}'.",
                        nameof(locations));
                }
            }

            return RegisterDownloader(new YooDownloader(
                _rawPackage.CreateResourceDownloader(
                    new BundleDownloaderOptions(
                        assetInfos,
                        recursiveDownload,
                        downloadingMaxNumber,
                        failedTryAgain)),
                _onDownloaderDisposed,
                validatedLocations.Length));
        }

        private YooDownloader RegisterDownloader(YooDownloader downloader)
        {
            _downloaders.Add(downloader);
            _registeredDownloaderScopeValueCount += downloader.ScopeValueCount;
            return downloader;
        }

        private void OnDownloaderDisposed(YooDownloader downloader)
        {
            if (_downloaders.Remove(downloader))
            {
                _registeredDownloaderScopeValueCount -= downloader.ScopeValueCount;
            }
        }

        private void EnsureDownloaderAdmission(int addedScopeValueCount)
        {
            if (_downloaders.Count >= MAX_REGISTERED_DOWNLOADERS)
            {
                throw new InvalidOperationException(
                    $"YooAsset package '{Name}' reached the limit of {MAX_REGISTERED_DOWNLOADERS} registered " +
                    "downloaders. Dispose completed or abandoned downloaders before creating more.");
            }

            if (addedScopeValueCount >
                MAX_REGISTERED_DOWNLOADER_SCOPE_VALUES - _registeredDownloaderScopeValueCount)
            {
                throw new InvalidOperationException(
                    $"YooAsset package '{Name}' cannot retain more than " +
                    $"{MAX_REGISTERED_DOWNLOADER_SCOPE_VALUES} downloader scope values at once. " +
                    "Dispose existing downloaders or reduce the requested batches.");
            }
        }

        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(
            string location,
            string bucket = null,
            string tag = null,
            string owner = null)
            where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateLocation(location);
            Cache.AssetCacheKey cacheKey = Cache.AssetCacheService.BuildCacheKey(
                location,
                typeof(TAsset),
                AssetCacheEntryKind.Asset);
            IReferenceCounted cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null)
            {
                return AssetHandleLeases.Create((IAssetHandle<TAsset>)cached);
            }

            AssetHandle raw = _rawPackage.LoadAssetSync<TAsset>(location);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = YooAssetHandle<TAsset>.Create(id, this, cacheKey, raw, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"AssetSync {typeof(TAsset).Name} : {location}");
            }
            backend = (YooAssetHandle<TAsset>)_cacheService.RegisterNew(
                cacheKey,
                bucket,
                tag,
                owner,
                backend);
            return AssetHandleLeases.Create(backend);
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(
            string location,
            string bucket = null,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default)
            where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            ValidateLocation(location);
            Cache.AssetCacheKey cacheKey = Cache.AssetCacheService.BuildCacheKey(
                location,
                typeof(TAsset),
                AssetCacheEntryKind.Asset);
            IReferenceCounted cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null)
            {
                return AssetHandleLeases.Create((IAssetHandle<TAsset>)cached, cancellationToken);
            }

            AssetHandle raw = _rawPackage.LoadAssetAsync<TAsset>(location);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = YooAssetHandle<TAsset>.Create(id, this, cacheKey, raw, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"AssetAsync {typeof(TAsset).Name} : {location}");
            }
            backend = (YooAssetHandle<TAsset>)_cacheService.RegisterNew(
                cacheKey,
                bucket,
                tag,
                owner,
                backend);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(backend, location);
#endif
            return AssetHandleLeases.Create(backend, cancellationToken);
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(
            string location,
            string bucket = null,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default)
            where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            ValidateLocation(location);
            Cache.AssetCacheKey cacheKey = Cache.AssetCacheService.BuildCacheKey(
                location,
                typeof(TAsset),
                AssetCacheEntryKind.AllAssets);
            IReferenceCounted cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null)
            {
                return AssetHandleLeases.Create((IAllAssetsHandle<TAsset>)cached, cancellationToken);
            }

            AllAssetsHandle raw = _rawPackage.LoadAllAssetsAsync<TAsset>(location);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = YooAllAssetsHandle<TAsset>.Create(id, cacheKey, raw, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"AllAssets {typeof(TAsset).Name} : {location}");
            }
            backend = (YooAllAssetsHandle<TAsset>)_cacheService.RegisterNew(
                cacheKey,
                bucket,
                tag,
                owner,
                backend);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(backend, location);
#endif
            return AssetHandleLeases.Create(backend, cancellationToken);
        }

        public IRawFileHandle LoadRawFileSync(
            string location,
            string bucket = null,
            string tag = null,
            string owner = null)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateLocation(location);
            Cache.AssetCacheKey cacheKey = Cache.AssetCacheService.BuildCacheKey(
                location,
                null,
                AssetCacheEntryKind.RawFile);
            IReferenceCounted cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null)
            {
                return AssetHandleLeases.Create((IRawFileHandle)cached);
            }

            AssetHandle raw = _rawPackage.LoadAssetSync<RawFileObject>(location);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = YooRawFileHandle.Create(id, cacheKey, raw, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"RawFileSync : {location}");
            }
            backend = (YooRawFileHandle)_cacheService.RegisterNew(
                cacheKey,
                bucket,
                tag,
                owner,
                backend);
            return AssetHandleLeases.Create(backend);
        }

        public IRawFileHandle LoadRawFileAsync(
            string location,
            string bucket = null,
            string tag = null,
            string owner = null,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            ValidateLocation(location);
            Cache.AssetCacheKey cacheKey = Cache.AssetCacheService.BuildCacheKey(
                location,
                null,
                AssetCacheEntryKind.RawFile);
            IReferenceCounted cached = _cacheService.Get(cacheKey, bucket, tag, owner);
            if (cached != null)
            {
                return AssetHandleLeases.Create((IRawFileHandle)cached, cancellationToken);
            }

            AssetHandle raw = _rawPackage.LoadAssetAsync<RawFileObject>(location);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = YooRawFileHandle.Create(id, cacheKey, raw, _cacheService.OnHandleReleased);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"RawFileAsync : {location}");
            }
            backend = (YooRawFileHandle)_cacheService.RegisterNew(
                cacheKey,
                bucket,
                tag,
                owner,
                backend);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(backend, location);
#endif
            return AssetHandleLeases.Create(backend, cancellationToken);
        }

        public IInstantiateHandle InstantiateAsync(
            IAssetHandle<GameObject> handle,
            Transform parent = null,
            bool worldPositionStays = false,
            bool setActive = true)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            if (!TryGetBackend(handle, out YooAssetHandle<GameObject> backend) || backend.Raw == null)
            {
                throw new ArgumentException(
                    "The handle is not an active YooAsset GameObject lease owned by this package.",
                    nameof(handle));
            }

            if (backend.Task.Status != UniTaskStatus.Succeeded || backend.Asset == null)
            {
                throw new InvalidOperationException(
                    "The YooAsset GameObject lease must complete successfully before instantiation.");
            }

            InstantiateOperation operation = backend.Raw.InstantiateAsync(
                new InstantiateOptions(setActive, parent, worldPositionStays));
            long id = AssetRuntimeGuard.NextHandleId();
            var result = YooInstantiateHandle.Create(
                id,
                operation,
                backend,
                _onInstantiateDisposed);
            _instantiateHandles.Add(id, result);
            string assetPath = backend.Raw.GetAssetInfo().AssetPath;
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"InstantiateAsync : {assetPath}");
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(result, assetPath);
#endif
            return result;
        }

        private void OnInstantiateDisposed(long id)
        {
            _instantiateHandles.Remove(id);
        }

        public ISceneHandle LoadSceneAsync(
            string sceneLocation,
            LoadSceneParameters loadParameters,
            SceneActivationMode activationMode,
            int priority = 100,
            string bucket = null)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ValidateSceneActivationMode(activationMode);
            return LoadSceneCore(
                sceneLocation,
                loadParameters,
                activationMode == SceneActivationMode.ActivateOnLoad,
                priority,
                bucket);
        }

        public ISceneHandle LoadSceneAsync(
            string sceneLocation,
            LoadSceneMode loadMode = LoadSceneMode.Single,
            bool activateOnLoad = true,
            int priority = 100,
            string bucket = null)
        {
            return LoadSceneCore(
                sceneLocation,
                new LoadSceneParameters(loadMode),
                activateOnLoad,
                priority,
                bucket);
        }

        private ISceneHandle LoadSceneCore(
            string sceneLocation,
            LoadSceneParameters loadParameters,
            bool activateOnLoad,
            int priority,
            string bucket)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            ValidateLocation(sceneLocation);
            ValidateSceneLoadParameters(loadParameters);
            YooAsset.SceneHandle raw = _rawPackage.LoadSceneAsync(
                sceneLocation,
                sceneMode: loadParameters.loadSceneMode,
                physicsMode: loadParameters.localPhysicsMode,
                allowSceneActivation: activateOnLoad,
                priority: (uint)Math.Max(0, priority));
            long id = AssetRuntimeGuard.NextHandleId();
            var result = YooSceneHandle.Create(
                id,
                _sceneOwnerToken,
                sceneLocation,
                raw,
                activateOnLoad);
            _sceneHandles.Add(id, result);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, Name, $"SceneAsync : {sceneLocation}");
            }
            SceneTracker.Register(id, Name, "YooAsset", sceneLocation, bucket, loadParameters, result);
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(result, sceneLocation);
#endif
            return result;
        }

        public UniTask UnloadSceneAsync(
            ISceneHandle sceneHandle,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (sceneHandle is not YooSceneHandle yooHandle ||
                !ReferenceEquals(yooHandle.OwnerToken, _sceneOwnerToken))
            {
                throw new ArgumentException(
                    "The scene handle is not owned by this YooAsset package.",
                    nameof(sceneHandle));
            }

            if (yooHandle.IsTerminallyReleased)
            {
                return UniTask.CompletedTask;
            }

            if (!_sceneHandles.TryGetValue(yooHandle.DebugId, out YooSceneHandle registered) ||
                !ReferenceEquals(registered, yooHandle))
            {
                throw new InvalidOperationException(
                    "The YooAsset scene handle is not active in its owning package registry.");
            }

            if (!yooHandle.UnloadStarted)
            {
                ThrowIfDestroyed();
            }

            return UnloadOwnedSceneAsync(yooHandle, cancellationToken);
        }

        private async UniTask UnloadOwnedSceneAsync(
            YooSceneHandle sceneHandle,
            CancellationToken cancellationToken)
        {
            await sceneHandle.UnloadAsync(cancellationToken);
            _sceneHandles.Remove(sceneHandle.DebugId);
        }

        public async UniTask UnloadUnusedAssetsAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            _cacheService.ClearAll();
            UnloadUnusedAssetsOperation operation = _rawPackage.UnloadUnusedAssetsAsync();
            await operation;
            if (operation.Status != EOperationStatus.Succeeded)
            {
                throw new InvalidOperationException(operation.Error ?? "YooAsset unload-unused operation failed.");
            }
        }

        public bool IsAssetCached<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            Cache.AssetCacheKey cacheKey = Cache.AssetCacheService.BuildCacheKey(
                location,
                typeof(TAsset),
                AssetCacheEntryKind.Asset);
            return _cacheService.Contains(cacheKey);
        }

        public AssetRuntimeCacheSnapshot GetRuntimeCacheSnapshot()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            return _cacheService.CreateRuntimeSnapshot(Name, "YooAsset");
        }

        public UniTask<bool> TryGetAssetLocationsByTagAsync(
            string tag,
            List<string> results,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            if (results == null)
            {
                throw new ArgumentNullException(nameof(results));
            }

            cancellationToken.ThrowIfCancellationRequested();
            results.Clear();
            if (tag != null && tag.Length > MAX_SCOPE_VALUE_LENGTH)
            {
                throw new ArgumentException(
                    $"Catalog tag length cannot exceed {MAX_SCOPE_VALUE_LENGTH} characters.",
                    nameof(tag));
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                return UniTask.FromResult(false);
            }

            AssetInfo[] infos;
            try
            {
                infos = _rawPackage.GetAssetInfos(tag);
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                CLogger.LogWarning($"[YooAssetPackage] Catalog query failed ({ex.GetType().Name}).");
                return UniTask.FromResult(false);
            }

            var unique = new HashSet<string>(StringComparer.Ordinal);
            int count = infos?.Length ?? 0;
            int totalCharacters = 0;
            for (int i = 0; i < count; i++)
            {
                AssetInfo info = infos[i];
                if (info == null || !info.IsValid)
                {
                    continue;
                }

                string location = string.IsNullOrEmpty(info.Address) ? info.AssetPath : info.Address;
                if (!string.IsNullOrEmpty(location) && unique.Add(location))
                {
                    if (results.Count >= MAX_SCOPE_VALUE_COUNT ||
                        totalCharacters > MAX_SCOPE_TOTAL_CHARACTERS - location.Length)
                    {
                        results.Clear();
                        CLogger.LogWarning(
                            "[YooAssetPackage] Catalog query output exceeded the bounded result budget.");
                        return UniTask.FromResult(false);
                    }

                    totalCharacters += location.Length;
                    results.Add(location);
                }
            }

            return UniTask.FromResult(results.Count > 0);
        }

        public UniTask<AssetStoragePreflightResult> CheckStorageAsync(
            AssetStoragePreflightRequest request,
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            if (request.RequiredFreeBytes < 0L)
            {
                return UniTask.FromResult(
                    new AssetStoragePreflightResult(
                        AssetStorageCapacityStatus.Failed,
                        error: "Required storage cannot be negative."));
            }

#if UNITY_STANDALONE || UNITY_EDITOR
            if (!_storageProbeReliable || string.IsNullOrEmpty(_storagePath))
            {
                return UniTask.FromResult(
                    AssetStoragePreflightResult.Unknown(
                        "The active YooAsset file system does not expose a reliable cache volume."));
            }

            try
            {
                if (!AssetStorageVolumeUtility.TryGetAvailableBytes(_storagePath, out long availableBytes))
                {
                    return UniTask.FromResult(
                        new AssetStoragePreflightResult(
                            AssetStorageCapacityStatus.Unknown,
                            storageLocation: _storagePath,
                            error: "The cache volume does not expose reliable free-space information."));
                }

                AssetStorageCapacityStatus status = availableBytes >= request.RequiredFreeBytes
                    ? AssetStorageCapacityStatus.Available
                    : AssetStorageCapacityStatus.Insufficient;
                return UniTask.FromResult(
                    new AssetStoragePreflightResult(status, availableBytes, _storagePath));
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                return UniTask.FromResult(AssetStoragePreflightResult.Unknown(ex.Message));
            }
#else
            return UniTask.FromResult(
                AssetStoragePreflightResult.Unknown(
                    "This platform does not expose a reliable provider-cache capacity probe."));
#endif
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

        internal void ConfigureCache(AssetCacheTuning tuning)
        {
            _cacheService.Configure(tuning);
        }

        private void ConfigureStorageProbe(InitializePackageOptions options)
        {
#if UNITY_STANDALONE || UNITY_EDITOR
            _storagePath = null;
            _storageProbeReliable = false;
            if (options is HostPlayModeOptions host &&
                host.CacheFileSystemParameters != null &&
                string.Equals(
                    host.CacheFileSystemParameters.FileSystemTypeName,
                    DEFAULT_CACHE_FILE_SYSTEM_CLASS,
                    StringComparison.Ordinal) &&
                !string.IsNullOrWhiteSpace(host.CacheFileSystemParameters.PackageRoot))
            {
                _storagePath = host.CacheFileSystemParameters.PackageRoot;
                _storageProbeReliable = true;
            }
#endif
        }

        private bool TryGetBackend(
            IAssetHandle<GameObject> handle,
            out YooAssetHandle<GameObject> backend)
        {
            backend = null;
            return handle is IAssetHandleLease lease &&
                   lease.TryGetBackend(out backend) &&
                   ReferenceEquals(backend.Owner, this);
        }

#if UNITY_STANDALONE || UNITY_EDITOR
#endif

        private void ThrowIfDestroyed()
        {
            if (Volatile.Read(ref _destroyed) != 0 ||
                Volatile.Read(ref _providerDestroyed) != 0 ||
                Volatile.Read(ref _shutdownRequested) != 0 ||
                _destroying)
            {
                throw new ObjectDisposedException(nameof(YooAssetPackage));
            }

            if (!_initialized)
            {
                throw new InvalidOperationException(
                    $"YooAsset package '{Name}' has not completed initialization.");
            }
        }

        private void EnterMaintenanceMutation(string operation)
        {
            if (_maintenanceMutationInProgress)
            {
                throw new InvalidOperationException(
                    $"YooAsset maintenance mutation '{operation}' cannot overlap another manifest or cache mutation.");
            }

            _maintenanceMutationInProgress = true;
        }

        private void ExitMaintenanceMutation()
        {
            _maintenanceMutationInProgress = false;
        }

        private void ThrowIfShutdownRequested()
        {
            if (Volatile.Read(ref _destroyed) != 0 ||
                Volatile.Read(ref _providerDestroyed) != 0 ||
                Volatile.Read(ref _shutdownRequested) != 0 ||
                _destroying)
            {
                throw new ObjectDisposedException(nameof(YooAssetPackage));
            }
        }

        private static void ValidateLocation(string location)
        {
            if (string.IsNullOrWhiteSpace(location))
            {
                throw new ArgumentException("Asset location cannot be null or empty.", nameof(location));
            }
        }

        private static void ValidateSceneActivationMode(SceneActivationMode activationMode)
        {
            if (activationMode != SceneActivationMode.ActivateOnLoad &&
                activationMode != SceneActivationMode.Manual)
            {
                throw new ArgumentOutOfRangeException(nameof(activationMode));
            }
        }

        private static void ValidateSceneLoadMode(LoadSceneMode loadMode)
        {
            if (loadMode != LoadSceneMode.Single && loadMode != LoadSceneMode.Additive)
            {
                throw new ArgumentOutOfRangeException(nameof(loadMode));
            }
        }

        private static void ValidateSceneLoadParameters(LoadSceneParameters loadParameters)
        {
            ValidateSceneLoadMode(loadParameters.loadSceneMode);
            ValidateLocalPhysicsMode(loadParameters.localPhysicsMode);
        }

        private static void ValidateLocalPhysicsMode(LocalPhysicsMode physicsMode)
        {
            const LocalPhysicsMode supportedModes =
                LocalPhysicsMode.Physics2D | LocalPhysicsMode.Physics3D;
            if ((physicsMode & ~supportedModes) != LocalPhysicsMode.None)
            {
                throw new ArgumentOutOfRangeException(nameof(physicsMode));
            }
        }

        private static void ValidateTimeoutSeconds(int timeoutSeconds)
        {
            if (timeoutSeconds <= 0 || timeoutSeconds > MAX_MAINTENANCE_TIMEOUT_SECONDS)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(timeoutSeconds),
                    $"Maintenance timeout must be between 1 and {MAX_MAINTENANCE_TIMEOUT_SECONDS} seconds.");
            }
        }

        private static void ValidateDownloadControls(int downloadingMaxNumber, int failedTryAgain)
        {
            if (downloadingMaxNumber <= 0 || downloadingMaxNumber > MAX_DOWNLOAD_CONCURRENCY)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(downloadingMaxNumber),
                    $"Download concurrency must be between 1 and {MAX_DOWNLOAD_CONCURRENCY}.");
            }

            if (failedTryAgain < 0 || failedTryAgain > MAX_DOWNLOAD_RETRY_COUNT)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(failedTryAgain),
                    $"Download retry count must be between 0 and {MAX_DOWNLOAD_RETRY_COUNT}.");
            }
        }

        private static string[] CloneAndValidateScopeValues(string[] values, string parameterName)
        {
            if (values == null || values.Length == 0)
            {
                throw new ArgumentException(
                    "Download or cache-maintenance scope must contain at least one value.",
                    parameterName);
            }

            if (values.Length > MAX_SCOPE_VALUE_COUNT)
            {
                throw new ArgumentException(
                    $"Scope value count cannot exceed {MAX_SCOPE_VALUE_COUNT}.",
                    parameterName);
            }

            int totalCharacters = 0;
            var copy = new string[values.Length];
            for (int i = 0; i < values.Length; i++)
            {
                string value = values[i];
                if (value != null && value.Length > MAX_SCOPE_VALUE_LENGTH)
                {
                    throw new ArgumentException(
                        $"Scope values cannot exceed {MAX_SCOPE_VALUE_LENGTH} characters each.",
                        parameterName);
                }

                if (string.IsNullOrWhiteSpace(value))
                {
                    throw new ArgumentException(
                        "Scope values cannot contain null or whitespace entries.",
                        parameterName);
                }

                if (totalCharacters > MAX_SCOPE_TOTAL_CHARACTERS - value.Length)
                {
                    throw new ArgumentException(
                        $"Scope values cannot exceed {MAX_SCOPE_VALUE_LENGTH} characters each or {MAX_SCOPE_TOTAL_CHARACTERS} characters in total.",
                        parameterName);
                }

                totalCharacters += value.Length;
                copy[i] = value;
            }

            return copy;
        }
    }
}
#endif
