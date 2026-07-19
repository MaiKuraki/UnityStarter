#if CYCLONEGAMES_HAS_YOOASSET
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

using UnityEngine;

using Cysharp.Threading.Tasks;
using YooAsset;

using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[YooAssetModule]";
        private const long MIN_OPERATION_TIME_SLICE_MS = 10L;
        private const long MAX_OPERATION_TIME_SLICE_MS = 100L;

        private static int _globalOwner;

        private readonly Dictionary<string, YooAssetPackage> _packages =
            new Dictionary<string, YooAssetPackage>(StringComparer.Ordinal);
        private readonly long _asyncOperationMaxTimeSliceMs;

        private bool _initialized;
        private bool _ownsAssetBundleRuntime;
        private bool _shutdownRequested;
        private bool _destroying;
        private UniTask _destroyTask;
        private ReadOnlyCollection<string> _packageNamesCache;
        private AssetCacheTuning _defaultCacheTuning;

        public YooAssetModule(long asyncOperationMaxTimeSliceMs = 16L)
        {
            if (asyncOperationMaxTimeSliceMs < MIN_OPERATION_TIME_SLICE_MS ||
                asyncOperationMaxTimeSliceMs > MAX_OPERATION_TIME_SLICE_MS)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(asyncOperationMaxTimeSliceMs),
                    $"YooAsset operation time slice must be between {MIN_OPERATION_TIME_SLICE_MS} and {MAX_OPERATION_TIME_SLICE_MS} milliseconds.");
            }

            _asyncOperationMaxTimeSliceMs = asyncOperationMaxTimeSliceMs;
        }

        public bool Initialized => _initialized && !_shutdownRequested;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_shutdownRequested)
            {
                throw new ObjectDisposedException(
                    nameof(YooAssetModule),
                    $"{DEBUG_FLAG} Shutdown was requested and this module instance cannot be reused.");
            }

            if (_initialized)
            {
                return UniTask.CompletedTask;
            }

            if (YooAssets.IsInitialized)
            {
                throw new InvalidOperationException(
                    $"{DEBUG_FLAG} YooAssets was initialized outside this module. Configure one explicit global owner before using the adapter.");
            }

            if (Interlocked.CompareExchange(ref _globalOwner, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    $"{DEBUG_FLAG} YooAssets is a process-global runtime and already has an AssetManagement owner.");
            }

            try
            {
                options = options.Normalized();
                AssetBundleRuntimeOwnership.Acquire(this, "YooAsset");
                _ownsAssetBundleRuntime = true;
                _defaultCacheTuning = options.DefaultCacheTuning;
                YooAssets.Initialize();
                YooAssets.SetAsyncOperationMaxTimeSlice(_asyncOperationMaxTimeSliceMs);
                _initialized = true;
                return UniTask.CompletedTask;
            }
            catch (Exception)
            {
                try
                {
                    if (YooAssets.IsInitialized)
                    {
                        YooAssets.Destroy();
                    }
                }
                catch (Exception cleanupException) when (AssetRuntimeGuard.IsRecoverableException(cleanupException))
                {
                    CLogger.LogWarning(
                        $"{DEBUG_FLAG} YooAssets cleanup after initialization failure also failed: {cleanupException.Message}");
                }
                finally
                {
                    ReleaseAssetBundleRuntimeOwnership();
                    Interlocked.Exchange(ref _globalOwner, 0);
                }

                throw;
            }
        }

        public UniTask DestroyAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
            _shutdownRequested = true;
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
                if (!_initialized)
                {
                    return;
                }

                var packages = new List<YooAssetPackage>(_packages.Values);
                for (int i = 0; i < packages.Count; i++)
                {
                    packages[i].BeginModuleShutdown();
                }

                // Unity scene operations share one global queue. Resolve every manual activation barrier across
                // all packages before the first unload operation is created, then drain by handle creation order.
                await DrainScenesAfterResolvingManualBarriersAsync(packages);

                List<Exception> failures = null;
                for (int i = 0; i < packages.Count; i++)
                {
                    YooAssetPackage package = packages[i];
                    try
                    {
                        await package.DestroyAsync();
                        _packages.Remove(package.Name);
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                    }
                }

                _packageNamesCache = null;
                if (failures != null)
                {
                    throw new AggregateException(
                        $"{DEBUG_FLAG} One or more packages failed to shut down.",
                        failures);
                }

                YooAssets.Destroy();
                _initialized = false;
                ReleaseAssetBundleRuntimeOwnership();
                Interlocked.Exchange(ref _globalOwner, 0);
            }
            finally
            {
                _destroying = false;
            }
        }

        private async UniTask DrainScenesAfterResolvingManualBarriersAsync(List<YooAssetPackage> packages)
        {
            var sceneHandles = new List<YooSceneHandle>();
            var drainPlan = new List<SceneDrainEntry>();
            for (int i = 0; i < packages.Count; i++)
            {
                sceneHandles.Clear();
                packages[i].CopyOwnedSceneHandlesTo(sceneHandles);
                for (int sceneIndex = 0; sceneIndex < sceneHandles.Count; sceneIndex++)
                {
                    drainPlan.Add(new SceneDrainEntry(packages[i], sceneHandles[sceneIndex]));
                }
            }

            drainPlan.Sort((left, right) => left.Handle.DebugId.CompareTo(right.Handle.DebugId));
            for (int i = 0; i < drainPlan.Count; i++)
            {
                await drainPlan[i].Handle.ResolveShutdownActivationAsync();
            }

            for (int i = 0; i < drainPlan.Count; i++)
            {
                SceneDrainEntry entry = drainPlan[i];
                await entry.Package.UnloadOwnedSceneForModuleShutdownAsync(entry.Handle);
            }
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            YooAssetStableToken.ValidatePackageName(packageName, nameof(packageName));

            EnsureInitialized();
            if (_packages.ContainsKey(packageName))
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");
            }

            ResourcePackage rawPackage = YooAssets.CreatePackage(packageName);
            var package = new YooAssetPackage(rawPackage, this);
            package.ConfigureCache(_defaultCacheTuning);
            _packages.Add(packageName, package);
            _packageNamesCache = null;
            return package;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrEmpty(packageName))
            {
                return null;
            }

            _packages.TryGetValue(packageName, out YooAssetPackage package);
            return package;
        }

        public async UniTask<bool> RemovePackageAsync(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrEmpty(packageName) || !_packages.TryGetValue(packageName, out YooAssetPackage package))
            {
                return false;
            }

            await package.DestroyAsync();
            _packages.Remove(packageName);
            _packageNamesCache = null;
            return true;
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (_packageNamesCache == null)
            {
                _packageNamesCache = new List<string>(_packages.Keys).AsReadOnly();
            }

            return _packageNamesCache;
        }

        internal void ValidatePackageSceneDrainOrder(YooAssetPackage targetPackage)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (targetPackage == null || !targetPackage.HasOwnedScenes)
            {
                return;
            }

            string targetPackageName = "<unregistered>";
            foreach (KeyValuePair<string, YooAssetPackage> pair in _packages)
            {
                if (ReferenceEquals(pair.Value, targetPackage))
                {
                    targetPackageName = pair.Key;
                    break;
                }
            }

            foreach (KeyValuePair<string, YooAssetPackage> pair in _packages)
            {
                YooAssetPackage package = pair.Value;
                if (ReferenceEquals(package, targetPackage))
                {
                    continue;
                }

                if (package.TryGetUnresolvedManualScene(out long blockingSceneId))
                {
                    throw new InvalidOperationException(
                        $"YooAsset package '{targetPackageName}' cannot shut down while an unresolved manual scene " +
                        $"(handle {blockingSceneId}) in package '{pair.Key}' is waiting for activation. " +
                        "Resolve every manual scene in the owning module first, or shut down the module so it can clear all activation barriers before unloading scenes.");
                }
            }
        }

        private readonly struct SceneDrainEntry
        {
            public readonly YooAssetPackage Package;
            public readonly YooSceneHandle Handle;

            public SceneDrainEntry(YooAssetPackage package, YooSceneHandle handle)
            {
                Package = package;
                Handle = handle;
            }
        }

        private void EnsureInitialized()
        {
            if (!_initialized)
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Asset module is not initialized.");
            }
        }

        private void EnsureOperational()
        {
            if (_shutdownRequested)
            {
                throw new ObjectDisposedException(
                    nameof(YooAssetModule),
                    $"{DEBUG_FLAG} Shutdown was requested and the module no longer accepts business operations.");
            }
        }

        private void ReleaseAssetBundleRuntimeOwnership()
        {
            if (!_ownsAssetBundleRuntime)
            {
                return;
            }

            AssetBundleRuntimeOwnership.Release(this);
            _ownsAssetBundleRuntime = false;
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobalOwner()
        {
            Interlocked.Exchange(ref _globalOwner, 0);
        }
    }
}
#endif
