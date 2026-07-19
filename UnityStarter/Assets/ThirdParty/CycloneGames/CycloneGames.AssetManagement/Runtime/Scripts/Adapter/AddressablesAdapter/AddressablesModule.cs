#if CYCLONEGAMES_HAS_ADDRESSABLES
using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Threading;

using UnityEngine;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AddressablesModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[AddressablesAssetModule]";

        private static int _globalOwner;

        private readonly Dictionary<string, IAssetPackage> _packages =
            new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);

        private bool _initialized;
        private bool _ownsGlobalRuntime;
        private bool _ownsAssetBundleRuntime;
        private bool _initializationStarted;
        private UniTask _initializationTask;
        private bool _shutdownRequested;
        private bool _destroying;
        private UniTask _destroyTask;
        private ReadOnlyCollection<string> _packageNamesCache;
        private AssetCacheTuning _defaultCacheTuning;

        public bool Initialized => _initialized && !_shutdownRequested;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_shutdownRequested)
            {
                throw new ObjectDisposedException(
                    nameof(AddressablesModule),
                    $"{DEBUG_FLAG} Shutdown was requested and this module instance cannot be reused.");
            }

            if (_initialized)
            {
                return UniTask.CompletedTask;
            }

            if (_initializationStarted)
            {
                return _initializationTask;
            }

            options = options.Normalized();
            if (Interlocked.CompareExchange(ref _globalOwner, 1, 0) != 0)
            {
                throw new InvalidOperationException(
                    $"{DEBUG_FLAG} Addressables is process-global and already has an AssetManagement owner.");
            }

            _ownsGlobalRuntime = true;
            try
            {
                AssetBundleRuntimeOwnership.Acquire(this, "Addressables");
                _ownsAssetBundleRuntime = true;
                _defaultCacheTuning = options.DefaultCacheTuning;
                _initializationStarted = true;
                _initializationTask = AssetOperationBroadcast.Create(InitializeCoreAsync());
                return _initializationTask;
            }
            catch
            {
                ReleaseGlobalOwner();
                throw;
            }
        }

        private async UniTask InitializeCoreAsync()
        {
            AsyncOperationHandle initializationHandle = default;
            bool initializationSucceeded = false;
            try
            {
                initializationHandle = Addressables.InitializeAsync(autoReleaseHandle: false);
                if (!initializationHandle.IsValid())
                {
                    throw new InvalidOperationException($"{DEBUG_FLAG} Addressables returned an invalid initialization handle.");
                }

                await initializationHandle.ToUniTask();
                if (initializationHandle.Status != AsyncOperationStatus.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"{DEBUG_FLAG} Initialization failed: {initializationHandle.OperationException?.Message ?? initializationHandle.Status.ToString()}.",
                        initializationHandle.OperationException);
                }

                initializationSucceeded = true;
            }
            finally
            {
                if (initializationHandle.IsValid())
                {
                    Addressables.Release(initializationHandle);
                }

                // ToUniTask resumes from Addressables' completion callback. Let that callback unwind after our
                // external handle is released so the provider can drop its internal running reference. The module
                // does not publish successful initialization or release failed initialization ownership earlier.
                await UniTask.Yield();
                if (!initializationSucceeded)
                {
                    ReleaseGlobalOwner();
                }

                _initializationStarted = false;
            }

            _initialized = true;
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
                if (_initializationStarted)
                {
                    await _initializationTask;
                }

                if (!_initialized)
                {
                    return;
                }

                List<Exception> failures = null;
                var packageNames = new List<string>(_packages.Keys);
                for (int i = 0; i < packageNames.Count; i++)
                {
                    string packageName = packageNames[i];
                    IAssetPackage package = _packages[packageName];
                    try
                    {
                        await package.DestroyAsync();
                        _packages.Remove(packageName);
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

                _initialized = false;
                ReleaseGlobalOwner();
            }
            finally
            {
                _destroying = false;
            }
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrWhiteSpace(packageName))
            {
                throw new ArgumentException($"{DEBUG_FLAG} Package name is null or empty.", nameof(packageName));
            }

            EnsureInitialized();
            if (_packages.ContainsKey(packageName))
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");
            }

            if (_packages.Count != 0)
            {
                throw new InvalidOperationException(
                    $"{DEBUG_FLAG} Addressables owns one global catalog/cache runtime. Create only one logical package per module instance.");
            }

            var package = new AddressablesAssetPackage(packageName);
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

            _packages.TryGetValue(packageName, out IAssetPackage package);
            return package;
        }

        public async UniTask<bool> RemovePackageAsync(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrEmpty(packageName) || !_packages.TryGetValue(packageName, out IAssetPackage package))
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
                    nameof(AddressablesModule),
                    $"{DEBUG_FLAG} Shutdown was requested and the module no longer accepts business operations.");
            }
        }

        private void ReleaseGlobalOwner()
        {
            if (!_ownsGlobalRuntime)
            {
                return;
            }

            if (_ownsAssetBundleRuntime)
            {
                AssetBundleRuntimeOwnership.Release(this);
                _ownsAssetBundleRuntime = false;
            }

            _ownsGlobalRuntime = false;
            Interlocked.Exchange(ref _globalOwner, 0);
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetGlobalOwner()
        {
            Interlocked.Exchange(ref _globalOwner, 0);
        }
    }
}
#endif
