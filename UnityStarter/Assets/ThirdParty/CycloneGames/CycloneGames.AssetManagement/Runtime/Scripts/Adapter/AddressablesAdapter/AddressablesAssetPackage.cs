#if CYCLONEGAMES_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.AddressableAssets.ResourceLocators;
using UnityEngine.Networking;
using UnityEngine.ResourceManagement.AsyncOperations;
using UnityEngine.ResourceManagement.ResourceLocations;
using UnityEngine.SceneManagement;

using Cysharp.Threading.Tasks;

using CycloneGames.IO;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class AddressablesAssetPackage : IAssetPackage, IAssetBulkLoader, IAssetSceneLoader,
        IAddressablesCatalogMaintenance, IAssetCatalogQuery, IAssetRuntimeDiagnostics, IAssetStoragePreflight
    {
        private const int MAX_VERSION_FILE_BYTES = 1024 * 1024;
        private const int MAX_TRACKED_PROVIDER_OPERATION_TAILS = 16_384;
        private const int MAX_PENDING_CATALOG_QUERIES = 32;
        private const int MAX_REGISTERED_DOWNLOADERS = 128;
        private const int MAX_REGISTERED_DOWNLOADER_SCOPE_VALUES = 262_144;
        private const int MAX_CATALOG_TAG_LENGTH = 4_096;
        private const int MAX_CATALOG_LOCATION_LENGTH = 4_096;
        private const int MAX_CATALOG_QUERY_RESULTS = 65_536;
        private const int MAX_CATALOG_QUERY_TOTAL_CHARACTERS = 8 * 1024 * 1024;

        private sealed class CatalogQueryState
        {
            public readonly List<string> Locations = new List<string>(32);
            public bool Succeeded;
        }

        private readonly string _packageName;
        private readonly Cache.AssetCacheService _cacheService;
        private readonly HashSet<AddressableDownloader> _downloaders =
            new HashSet<AddressableDownloader>();
        private readonly Dictionary<long, AddressableInstantiateHandle> _instantiateHandles =
            new Dictionary<long, AddressableInstantiateHandle>();
        private readonly Dictionary<long, AddressableSceneHandle> _sceneHandles =
            new Dictionary<long, AddressableSceneHandle>();
        private readonly List<long> _sceneUnloadScratchIds = new List<long>(4);
        private readonly object _sceneOwnerToken = new object();
        private readonly Action<long> _onInstantiateDisposed;
        private readonly Action<AddressableDownloader> _onDownloaderDisposed;
        private UniTaskCompletionSource _providerOperationTailsDrained;
        private int _pendingProviderOperationTailCount;
        private int _pendingCatalogQueryCount;
        private int _registeredDownloaderScopeValueCount;
        private bool _initialized;
        private int _shutdownRequested;
        private bool _maintenanceMutationInProgress;
        private bool _sceneUnloadSubscribed;
        private bool _destroying;
        private UniTask _destroyTask;
        private int _destroyed;

        public AddressablesAssetPackage(string name)
        {
            _packageName = name ?? throw new ArgumentNullException(nameof(name));
            _cacheService = new Cache.AssetCacheService(this);
            _onInstantiateDisposed = OnInstantiateDisposed;
            _onDownloaderDisposed = OnDownloaderDisposed;
        }

        public string Name => _packageName;

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

            if (options.ProviderOptions != null)
            {
                throw new ArgumentException(
                    "Addressables package initialization does not accept provider options.",
                    nameof(options));
            }

            if (options.CacheTuningOverride.HasValue)
            {
                _cacheService.Configure(options.CacheTuningOverride.Value);
            }

            _initialized = true;
            SubscribeToSceneUnloads();
            return UniTask.FromResult(true);
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
            foreach (KeyValuePair<long, AddressableSceneHandle> pair in _sceneHandles)
            {
                AddressableSceneHandle handle = pair.Value;
                if (!handle.IsProviderHandleReleased && !handle.MatchesScene(scene))
                {
                    continue;
                }

                if (handle.UnloadStarted)
                {
                    // Keep registry authority until the shared package unload completion resumes, but record the
                    // provider callback so an activation/unload race can converge without retrying an invalid handle.
                    handle.OnProviderSceneUnloadObserved(scene);
                    continue;
                }

                _sceneUnloadScratchIds.Add(pair.Key);
            }

            for (int i = 0; i < _sceneUnloadScratchIds.Count; i++)
            {
                long id = _sceneUnloadScratchIds[i];
                if (_sceneHandles.TryGetValue(id, out AddressableSceneHandle handle))
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
            Interlocked.Exchange(ref _shutdownRequested, 1);
            UnsubscribeFromSceneUnloads();
            if (_maintenanceMutationInProgress)
            {
                throw new InvalidOperationException(
                    "Addressables package shutdown was requested during a catalog or cache mutation. Retry cleanup after the mutation completes.");
            }

            if (Volatile.Read(ref _destroyed) != 0)
            {
                return UniTask.CompletedTask;
            }

            if (_destroying)
            {
                return _destroyTask;
            }

            _destroying = true;
            _destroyTask = AssetOperationBroadcast.Create(DestroyCoreAsync());
            return _destroyTask;
        }

        private async UniTask DestroyCoreAsync()
        {
            try
            {
                List<Exception> failures = null;
                var scenes = new List<AddressableSceneHandle>(_sceneHandles.Values);
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

                // Unity stalls subsequently queued scene operations while any earlier load is held at a manual
                // activation barrier. Resolve every tracked manual load before starting the first unload; sorting
                // unloads alone cannot prevent a later-created manual load from blocking an earlier scene unload.
                for (int i = 0; manualBarriersResolved && i < scenes.Count; i++)
                {
                    AddressableSceneHandle scene = scenes[i];
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

                var instances = new List<AddressableInstantiateHandle>(_instantiateHandles.Values);
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

                var downloaders = new List<AddressableDownloader>(_downloaders);
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

                // Addressables cannot abort an in-flight dependency download. Every wrapper wait is cancelled above,
                // but package shutdown retains process-global ownership until each provider operation reaches its
                // terminal cleanup and releases its handle exactly once.
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

                // Shutdown is sticky. Outstanding cached and generation-detached handles become invalid now.
                // Addressables may retain an internal running reference after the external handle is released;
                // the provider-tail registry below keeps module ownership until those operations unwind.
                try
                {
                    _cacheService.Dispose();
                }
                catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                {
                    failures ??= new List<Exception>();
                    failures.Add(ex);
                }

                // A pending Addressables operation owns an internal running reference. Releasing the wrapper's
                // external handle does not abort it. Wait through terminal publication and one player-loop yield
                // before the module is allowed to release the process-wide AssetBundle ownership guard.
                await WaitForProviderOperationTailsAsync();

                // Scene and downloader wrappers have their own drain paths and are not duplicated in the generic
                // tail registry. Addressables can resume those awaiters from its completion callback before the
                // callback stack releases the provider's internal running reference. This shutdown-only barrier
                // lets every terminal callback unwind before process-global AssetBundle ownership is released.
                await UniTask.Yield();

                if (failures != null)
                {
                    throw new AggregateException(
                        $"Addressables package '{_packageName}' failed to clean up one or more owned operations.",
                        failures);
                }

                _initialized = false;
                UnsubscribeFromSceneUnloads();
                Interlocked.Exchange(ref _destroyed, 1);
            }
            finally
            {
                _destroying = false;
            }
        }

        public async UniTask<string> ReadReleaseMetadataVersionAsync(
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            string version = await TryLoadVersionFromPersistentDataAsync(cancellationToken);
            if (string.IsNullOrEmpty(version))
            {
                version = await TryLoadVersionFromStreamingAssetsAsync(cancellationToken);
            }

            if (string.IsNullOrEmpty(version))
            {
                CLogger.LogWarning(
                    "[AddressablesAssetPackage] Version metadata is unavailable. Addressables catalogs do not expose a provider-neutral content version.");
            }

            return version ?? string.Empty;
        }

        public async UniTask<bool> UpdateLatestCatalogsAsync(
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            EnterMaintenanceMutation(nameof(UpdateLatestCatalogsAsync));
            try
            {
                List<string> catalogs;
                AsyncOperationHandle<List<string>> checkOperation = default;
                try
                {
                    checkOperation = Addressables.CheckForCatalogUpdates(autoReleaseHandle: false);
                    // Addressables does not abort this operation when a UniTask caller view is cancelled. The
                    // completion mutates locator update flags, so keep package ownership until the provider is
                    // terminal and honor cancellation only at the next safe boundary.
                    await checkOperation.ToUniTask();
                    cancellationToken.ThrowIfCancellationRequested();
                    if (checkOperation.Status != AsyncOperationStatus.Succeeded)
                    {
                        CLogger.LogWarning(
                            $"[AddressablesAssetPackage] Catalog update check failed: {checkOperation.OperationException?.Message}");
                        return false;
                    }

                    catalogs = checkOperation.Result == null
                        ? new List<string>()
                        : new List<string>(checkOperation.Result);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                {
                    CLogger.LogWarning($"[AddressablesAssetPackage] Catalog update check failed: {ex.Message}");
                    return false;
                }
                finally
                {
                    if (checkOperation.IsValid())
                    {
                        Addressables.Release(checkOperation);
                    }
                }

                if (catalogs.Count == 0)
                {
                    return true;
                }

                cancellationToken.ThrowIfCancellationRequested();
                AsyncOperationHandle<List<IResourceLocator>> updateOperation = default;
                bool updateAttempted = false;
                try
                {
                    updateAttempted = true;
                    updateOperation = Addressables.UpdateCatalogs(catalogs, autoReleaseHandle: false);
                    // Catalog activation cannot be rolled back safely. Once started, finish deterministically
                    // instead of reporting cancellation while provider state continues mutating in the background.
                    await updateOperation.ToUniTask();
                    if (updateOperation.Status != AsyncOperationStatus.Succeeded)
                    {
                        return false;
                    }
                }
                catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                {
                    CLogger.LogWarning($"[AddressablesAssetPackage] Catalog update failed: {ex.Message}");
                    return false;
                }
                finally
                {
                    try
                    {
                        if (updateOperation.IsValid())
                        {
                            Addressables.Release(updateOperation);
                        }
                    }
                    finally
                    {
                        if (updateAttempted)
                        {
                            // Addressables does not guarantee rollback of locator mutations on a failed update.
                            // Detach every wrapper cache key after any attempted catalog activation.
                            _cacheService.AdvanceGeneration();
                        }
                    }
                }

                return true;
            }
            finally
            {
                ExitMaintenanceMutation();
            }
        }

        public async UniTask<bool> CleanUnusedBundleCacheAsync(
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            EnterMaintenanceMutation(nameof(CleanUnusedBundleCacheAsync));
            AsyncOperationHandle<bool> cleanOperation = default;
            try
            {
                cleanOperation = Addressables.CleanBundleCache();
                // Unity cache cleanup cannot be rolled back. Once started, do not report caller cancellation while
                // the provider continues mutating shared cache state in the background.
                await cleanOperation.ToUniTask();
                return cleanOperation.Status == AsyncOperationStatus.Succeeded && cleanOperation.Result;
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                CLogger.LogWarning($"[AddressablesAssetPackage] Unused bundle cache cleanup failed: {ex.Message}");
                return false;
            }
            finally
            {
                try
                {
                    if (cleanOperation.IsValid())
                    {
                        Addressables.Release(cleanOperation);
                    }
                }
                finally
                {
                    ExitMaintenanceMutation();
                }
            }
        }

        public UniTask<bool> ClearAllCacheFilesAsync(
            CancellationToken cancellationToken = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            cancellationToken.ThrowIfCancellationRequested();
            ThrowIfDestroyed();
            EnterMaintenanceMutation(nameof(ClearAllCacheFilesAsync));
            try
            {
                CLogger.LogWarning(
                    "[AddressablesAssetPackage] Clearing Unity's process-wide AssetBundle cache. This operation is not scoped to the current package.");
                return UniTask.FromResult(Caching.ClearCache());
            }
            finally
            {
                ExitMaintenanceMutation();
            }
        }

        public IDownloader CreateDownloaderForTags(string[] tags)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            EnsureDownloaderAdmission(0);
            string[] validatedTags = CloneAndValidateKeys(tags, nameof(tags));
            EnsureDownloaderAdmission(validatedTags.Length);
            return RegisterDownloader(new AddressableDownloader(
                validatedTags,
                _onDownloaderDisposed));
        }

        public IDownloader CreateDownloaderForLocations(
            string[] locations)
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            EnsureDownloaderAdmission(0);
            string[] validatedLocations = CloneAndValidateKeys(locations, nameof(locations));
            EnsureDownloaderAdmission(validatedLocations.Length);
            return RegisterDownloader(new AddressableDownloader(
                validatedLocations,
                _onDownloaderDisposed));
        }

        private AddressableDownloader RegisterDownloader(AddressableDownloader downloader)
        {
            _downloaders.Add(downloader);
            _registeredDownloaderScopeValueCount += downloader.ScopeValueCount;
            return downloader;
        }

        private void OnDownloaderDisposed(AddressableDownloader downloader)
        {
            if (_downloaders.Remove(downloader))
            {
                _registeredDownloaderScopeValueCount -= downloader.ScopeValueCount;
            }
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

            EnsureProviderOperationTailCapacity();
            AsyncOperationHandle<TAsset> raw = Addressables.LoadAssetAsync<TAsset>(location);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = AddressableAssetHandle<TAsset>.Create(
                id,
                this,
                cacheKey,
                location,
                raw,
                _cacheService.OnHandleReleased);
            TrackProviderOperationTail(backend.Task);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, _packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            }
            backend = (AddressableAssetHandle<TAsset>)_cacheService.RegisterNew(
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

            EnsureProviderOperationTailCapacity();
            AsyncOperationHandle<IList<TAsset>> raw = Addressables.LoadAssetsAsync<TAsset>(location, null);
            long id = AssetRuntimeGuard.NextHandleId();
            var backend = AddressableAllAssetsHandle<TAsset>.Create(
                id,
                cacheKey,
                raw,
                _cacheService.OnHandleReleased);
            TrackProviderOperationTail(backend.Task);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, _packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            }
            backend = (AddressableAllAssetsHandle<TAsset>)_cacheService.RegisterNew(
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
            if (handle is not IAssetHandleLease lease ||
                !lease.TryGetBackend<AddressableAssetHandle<GameObject>>(out AddressableAssetHandle<GameObject> backend) ||
                !ReferenceEquals(backend.Owner, this) ||
                string.IsNullOrEmpty(backend.Location))
            {
                throw new ArgumentException(
                    "The handle is not an active Addressables GameObject lease owned by this package.",
                    nameof(handle));
            }

            if (backend.Task.Status != UniTaskStatus.Succeeded || backend.Asset == null)
            {
                throw new InvalidOperationException(
                    "The Addressables GameObject lease must complete successfully before instantiation.");
            }

            EnsureProviderOperationTailCapacity();
            AsyncOperationHandle<GameObject> operation = Addressables.InstantiateAsync(
                backend.Location,
                parent,
                worldPositionStays,
                trackHandle: false);
            long id = AssetRuntimeGuard.NextHandleId();
            var result = AddressableInstantiateHandle.Create(
                id,
                operation,
                setActive,
                _onInstantiateDisposed);
            TrackProviderOperationTail(result.Task);
            _instantiateHandles.Add(id, result);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, _packageName, $"InstantiateAsync : {backend.Location}");
            }
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            AssetLoadProfiler.TrackAsync(result, backend.Location);
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
            AsyncOperationHandle<UnityEngine.ResourceManagement.ResourceProviders.SceneInstance> operation =
                Addressables.LoadSceneAsync(sceneLocation, loadParameters, activateOnLoad, priority);
            long id = AssetRuntimeGuard.NextHandleId();
            var result = AddressableSceneHandle.Create(
                id,
                _sceneOwnerToken,
                sceneLocation,
                operation,
                activateOnLoad);
            _sceneHandles.Add(id, result);
            if (HandleTracker.Enabled)
            {
                HandleTracker.Register(id, _packageName, $"SceneAsync : {sceneLocation}");
            }
            SceneTracker.Register(id, _packageName, "Addressables", sceneLocation, bucket, loadParameters, result);
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
            if (sceneHandle is not AddressableSceneHandle addressableHandle ||
                !ReferenceEquals(addressableHandle.OwnerToken, _sceneOwnerToken))
            {
                throw new ArgumentException(
                    "The scene handle is not owned by this Addressables package.",
                    nameof(sceneHandle));
            }

            if (addressableHandle.IsTerminallyReleased)
            {
                return UniTask.CompletedTask;
            }

            if (!_sceneHandles.TryGetValue(addressableHandle.DebugId, out AddressableSceneHandle registered) ||
                !ReferenceEquals(registered, addressableHandle))
            {
                throw new InvalidOperationException(
                    "The Addressables scene handle is not active in its owning package registry.");
            }

            if (!addressableHandle.UnloadStarted)
            {
                ThrowIfDestroyed();
            }

            return UnloadOwnedSceneAsync(addressableHandle, cancellationToken);
        }

        private async UniTask UnloadOwnedSceneAsync(
            AddressableSceneHandle sceneHandle,
            CancellationToken cancellationToken)
        {
            await sceneHandle.UnloadAsync(cancellationToken);
            _sceneHandles.Remove(sceneHandle.DebugId);
        }

        public UniTask UnloadUnusedAssetsAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            ThrowIfDestroyed();
            _cacheService.ClearAll();
            return UniTask.CompletedTask;
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
            return _cacheService.CreateRuntimeSnapshot(_packageName, "Addressables");
        }

        public async UniTask<bool> TryGetAssetLocationsByTagAsync(
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
            if (tag != null && tag.Length > MAX_CATALOG_TAG_LENGTH)
            {
                throw new ArgumentException(
                    $"Catalog tag length cannot exceed {MAX_CATALOG_TAG_LENGTH} characters.",
                    nameof(tag));
            }

            if (string.IsNullOrWhiteSpace(tag))
            {
                return false;
            }

            EnsureProviderOperationTailCapacity();
            EnsureCatalogQueryCapacity();
            var state = new CatalogQueryState();
            _pendingCatalogQueryCount++;
            UniTask providerTask = AssetOperationBroadcast.Create(
                QueryCatalogLocationsTrackedCoreAsync(tag, state));
            TrackProviderOperationTail(providerTask);
            await WaitWithCallerCancellationOnMainThreadAsync(providerTask, cancellationToken);
            if (!state.Succeeded)
            {
                return false;
            }

            results.AddRange(state.Locations);
            return results.Count > 0;
        }

        private async UniTask QueryCatalogLocationsTrackedCoreAsync(
            string tag,
            CatalogQueryState state)
        {
            try
            {
                await QueryCatalogLocationsCoreAsync(tag, state);
            }
            finally
            {
                _pendingCatalogQueryCount--;
            }
        }

        private static async UniTask QueryCatalogLocationsCoreAsync(
            string tag,
            CatalogQueryState state)
        {
            AsyncOperationHandle<IList<IResourceLocation>> operation =
                Addressables.LoadResourceLocationsAsync(tag, typeof(UnityEngine.Object));
            try
            {
                // The provider operation is intentionally non-cancellable. Cancelling a UniTask view does not
                // abort Addressables and must not allow late writes into the caller-owned result list.
                await operation.ToUniTask();
                if (operation.Status != AsyncOperationStatus.Succeeded)
                {
                    return;
                }

                IList<IResourceLocation> locations = operation.Result;
                int count = locations?.Count ?? 0;
                if (count > MAX_CATALOG_QUERY_RESULTS)
                {
                    CLogger.LogWarning(
                        "[AddressablesAssetPackage] Catalog query input exceeded the bounded scan budget.");
                    return;
                }

                int totalCharacters = 0;
                var unique = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < count; i++)
                {
                    string location = locations[i]?.PrimaryKey;
                    if (string.IsNullOrEmpty(location))
                    {
                        continue;
                    }

                    if (location.Length > MAX_CATALOG_LOCATION_LENGTH)
                    {
                        state.Locations.Clear();
                        CLogger.LogWarning(
                            "[AddressablesAssetPackage] Catalog query location exceeded the bounded length budget.");
                        return;
                    }

                    if (!unique.Add(location))
                    {
                        continue;
                    }

                    if (state.Locations.Count >= MAX_CATALOG_QUERY_RESULTS ||
                        totalCharacters > MAX_CATALOG_QUERY_TOTAL_CHARACTERS - location.Length)
                    {
                        state.Locations.Clear();
                        CLogger.LogWarning(
                            "[AddressablesAssetPackage] Catalog query output exceeded the bounded result budget.");
                        return;
                    }

                    totalCharacters += location.Length;
                    state.Locations.Add(location);
                }

                state.Succeeded = state.Locations.Count > 0;
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                CLogger.LogWarning(
                    $"[AddressablesAssetPackage] Catalog location query failed ({ex.GetType().Name}).");
            }
            finally
            {
                if (operation.IsValid())
                {
                    Addressables.Release(operation);
                }
            }
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
            try
            {
                UnityEngine.Cache cache = Caching.currentCacheForWriting;
                if (!cache.valid)
                {
                    return UniTask.FromResult(
                        AssetStoragePreflightResult.Unknown(
                            "Unity does not expose a valid cache for provider downloads."));
                }

                string storagePath = cache.path;
                if (!AssetStorageVolumeUtility.TryGetAvailableBytes(storagePath, out long availableBytes))
                {
                    return UniTask.FromResult(
                        new AssetStoragePreflightResult(
                            AssetStorageCapacityStatus.Unknown,
                            storageLocation: storagePath,
                            error: "The cache volume does not expose reliable free-space information."));
                }

                long maximumStorageBytes = cache.maximumAvailableStorageSpace;
                long occupiedBytes = cache.spaceOccupied;
                if (maximumStorageBytes < 0L || occupiedBytes < 0L || occupiedBytes > maximumStorageBytes)
                {
                    return UniTask.FromResult(
                        new AssetStoragePreflightResult(
                            AssetStorageCapacityStatus.Unknown,
                            storageLocation: storagePath,
                            error: "Unity cache quota could not be measured reliably."));
                }

                long quotaAvailableBytes = maximumStorageBytes - occupiedBytes;
                long effectiveAvailableBytes = Math.Min(availableBytes, quotaAvailableBytes);
                AssetStorageCapacityStatus status = effectiveAvailableBytes >= request.RequiredFreeBytes
                    ? AssetStorageCapacityStatus.Available
                    : AssetStorageCapacityStatus.Insufficient;
                return UniTask.FromResult(
                    new AssetStoragePreflightResult(status, effectiveAvailableBytes, storagePath));
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

        private async UniTask<string> TryLoadVersionFromPersistentDataAsync(CancellationToken cancellationToken)
        {
            string path = AddressablesVersionPathHelper.GetPersistentVersionPath();
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            try
            {
                string json = await SystemFileStore.Default.ReadTextAsync(
                    path,
                    MAX_VERSION_FILE_BYTES,
                    cancellationToken: cancellationToken);
                return ParseVersion(json);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                CLogger.LogWarning($"[AddressablesAssetPackage] Persistent version metadata is invalid: {ex.Message}");
                return string.Empty;
            }
        }

        private async UniTask<string> TryLoadVersionFromStreamingAssetsAsync(CancellationToken cancellationToken)
        {
            string[] paths = AddressablesVersionPathHelper.GetStreamingAssetsVersionPaths();
            for (int i = 0; i < paths.Length; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                try
                {
                    string json = await ReadStreamingAssetsFileAsync(paths[i], cancellationToken);
                    string version = ParseVersion(json);
                    if (!string.IsNullOrEmpty(version))
                    {
                        return version;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                {
                    // The next known layout may contain the metadata.
                }
            }

            return string.Empty;
        }

        private static async UniTask<string> ReadStreamingAssetsFileAsync(
            string path,
            CancellationToken cancellationToken)
        {
#if UNITY_ANDROID || UNITY_WEBGL
            var downloadHandler = new BoundedDownloadHandler(MAX_VERSION_FILE_BYTES);
            using (var request = new UnityWebRequest(
                       path,
                       UnityWebRequest.kHttpVerbGET,
                       downloadHandler,
                       null))
            {
                await request.SendWebRequest().WithCancellation(cancellationToken);
                if (request.result != UnityWebRequest.Result.Success || downloadHandler.ExceededLimit)
                {
                    return string.Empty;
                }

                return downloadHandler.DecodeText();
            }
#else
            if (!File.Exists(path))
            {
                return string.Empty;
            }

            return await SystemFileStore.Default.ReadTextAsync(
                path,
                MAX_VERSION_FILE_BYTES,
                cancellationToken: cancellationToken);
#endif
        }

        private static string ParseVersion(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
            {
                return string.Empty;
            }

            VersionDataJson data = JsonUtility.FromJson<VersionDataJson>(json);
            return data?.contentVersion ?? string.Empty;
        }

#if UNITY_ANDROID || UNITY_WEBGL
        private sealed class BoundedDownloadHandler : DownloadHandlerScript
        {
            private const int RECEIVE_BUFFER_BYTES = 16 * 1024;

            private readonly byte[] _payload;
            private int _length;

            public BoundedDownloadHandler(int maximumBytes)
                : base(new byte[RECEIVE_BUFFER_BYTES])
            {
                if (maximumBytes <= 0)
                {
                    throw new ArgumentOutOfRangeException(nameof(maximumBytes));
                }

                _payload = new byte[maximumBytes];
            }

            public bool ExceededLimit { get; private set; }

            public string DecodeText()
            {
                return TextCodec.Decode(new ReadOnlySpan<byte>(_payload, 0, _length));
            }

            protected override void ReceiveContentLengthHeader(ulong contentLength)
            {
                if (contentLength > (ulong)_payload.Length)
                {
                    ExceededLimit = true;
                }
            }

            protected override bool ReceiveData(byte[] data, int dataLength)
            {
                if (ExceededLimit || data == null || dataLength < 0 || dataLength > data.Length)
                {
                    ExceededLimit = true;
                    return false;
                }

                if (dataLength > _payload.Length - _length)
                {
                    ExceededLimit = true;
                    return false;
                }

                Buffer.BlockCopy(data, 0, _payload, _length, dataLength);
                _length += dataLength;
                return true;
            }
        }
#endif

#if UNITY_STANDALONE || UNITY_EDITOR
#endif

        private void EnsureProviderOperationTailCapacity()
        {
            if (_pendingProviderOperationTailCount >= MAX_TRACKED_PROVIDER_OPERATION_TAILS)
            {
                throw new InvalidOperationException(
                    $"Addressables package '{_packageName}' reached the limit of " +
                    $"{MAX_TRACKED_PROVIDER_OPERATION_TAILS} pending provider operations. " +
                    "Apply load admission control or await existing operations before starting more work.");
            }
        }

        private void EnsureCatalogQueryCapacity()
        {
            if (_pendingCatalogQueryCount >= MAX_PENDING_CATALOG_QUERIES)
            {
                throw new InvalidOperationException(
                    $"Addressables package '{_packageName}' reached the limit of " +
                    $"{MAX_PENDING_CATALOG_QUERIES} pending catalog queries. " +
                    "Await or cancel caller work and let existing provider queries reach terminal before retrying.");
            }
        }

        private void EnsureDownloaderAdmission(int addedScopeValueCount)
        {
            if (_downloaders.Count >= MAX_REGISTERED_DOWNLOADERS)
            {
                throw new InvalidOperationException(
                    $"Addressables package '{_packageName}' reached the limit of " +
                    $"{MAX_REGISTERED_DOWNLOADERS} registered downloaders. Dispose completed or abandoned " +
                    "downloaders before creating more.");
            }

            if (addedScopeValueCount >
                MAX_REGISTERED_DOWNLOADER_SCOPE_VALUES - _registeredDownloaderScopeValueCount)
            {
                throw new InvalidOperationException(
                    $"Addressables package '{_packageName}' cannot retain more than " +
                    $"{MAX_REGISTERED_DOWNLOADER_SCOPE_VALUES} downloader scope values at once. " +
                    "Dispose existing downloaders or reduce the requested batches.");
            }
        }

        private void TrackProviderOperationTail(UniTask providerTask)
        {
            if (_pendingProviderOperationTailCount == 0)
            {
                _providerOperationTailsDrained = null;
            }

            _pendingProviderOperationTailCount++;
            CompleteProviderOperationTailAsync(providerTask).Forget();
        }

        private async UniTask CompleteProviderOperationTailAsync(UniTask providerTask)
        {
            try
            {
                await providerTask;
            }
            catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
            {
                // Provider failure is already retained by the caller-visible broadcast task. Tail tracking only
                // proves that Addressables has reached a terminal callback before ownership can be released.
            }
            finally
            {
                if (!PlayerLoopHelper.IsMainThread)
                {
                    await UniTask.SwitchToMainThread();
                }

                // Addressables 2.11.1 decrements its internal running reference after completion callbacks return.
                // Yield once so the provider cleanup stack can unwind before declaring the tail drained. This
                // bookkeeping must also run when a fatal provider exception propagates out of this observer.
                await UniTask.Yield();
                _pendingProviderOperationTailCount--;
                if (_pendingProviderOperationTailCount == 0)
                {
                    _providerOperationTailsDrained?.TrySetResult();
                }
            }
        }

        private UniTask WaitForProviderOperationTailsAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_pendingProviderOperationTailCount == 0)
            {
                return UniTask.CompletedTask;
            }

            _providerOperationTailsDrained ??= new UniTaskCompletionSource();
            return _providerOperationTailsDrained.Task;
        }

        private static async UniTask WaitWithCallerCancellationOnMainThreadAsync(
            UniTask providerTask,
            CancellationToken cancellationToken)
        {
            try
            {
                await AssetOperationBroadcast.CreateCallerView(providerTask, cancellationToken);
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

        private void ThrowIfDestroyed()
        {
            ThrowIfShutdownRequested();
            if (!_initialized)
            {
                throw new InvalidOperationException(
                    $"Addressables package '{Name}' has not completed initialization.");
            }
        }

        private void ThrowIfShutdownRequested()
        {
            if (Volatile.Read(ref _destroyed) != 0 ||
                Volatile.Read(ref _shutdownRequested) != 0 ||
                _destroying)
            {
                throw new ObjectDisposedException(nameof(AddressablesAssetPackage));
            }
        }

        private void EnterMaintenanceMutation(string operation)
        {
            if (_maintenanceMutationInProgress)
            {
                throw new InvalidOperationException(
                    $"Addressables maintenance mutation '{operation}' cannot overlap another catalog or cache mutation.");
            }

            _maintenanceMutationInProgress = true;
        }

        private void ExitMaintenanceMutation()
        {
            _maintenanceMutationInProgress = false;
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

        private static string[] CloneAndValidateKeys(string[] keys, string parameterName)
        {
            const int MAX_KEY_COUNT = 65_536;
            const int MAX_KEY_LENGTH = 4_096;
            const int MAX_TOTAL_CHARACTERS = 8 * 1024 * 1024;

            if (keys == null || keys.Length == 0)
            {
                throw new ArgumentException(
                    "Download scope must contain at least one key.",
                    parameterName);
            }

            if (keys.Length > MAX_KEY_COUNT)
            {
                throw new ArgumentException(
                    $"Download scope key count cannot exceed {MAX_KEY_COUNT}.",
                    parameterName);
            }

            int totalCharacters = 0;
            var copy = new string[keys.Length];
            for (int i = 0; i < keys.Length; i++)
            {
                string key = keys[i];
                if (key != null && key.Length > MAX_KEY_LENGTH)
                {
                    throw new ArgumentException(
                        $"Download keys cannot exceed {MAX_KEY_LENGTH} characters each.",
                        parameterName);
                }

                if (string.IsNullOrWhiteSpace(key))
                {
                    throw new ArgumentException(
                        "Download scope values cannot contain null or empty entries.",
                        parameterName);
                }

                if (totalCharacters > MAX_TOTAL_CHARACTERS - key.Length)
                {
                    throw new ArgumentException(
                        $"Download keys cannot exceed {MAX_KEY_LENGTH} characters each or {MAX_TOTAL_CHARACTERS} characters in total.",
                        parameterName);
                }

                totalCharacters += key.Length;
                copy[i] = key;
            }

            return copy;
        }

        [Serializable]
        private sealed class VersionDataJson
        {
            public string contentVersion = string.Empty;
        }
    }
}
#endif
