#if ADDRESSABLES_PRESENT
using System;
using System.Collections.Generic;
using System.Threading;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;
using CycloneGames.Logger;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AddressablesModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[AddressablesAssetModule]";
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private readonly object _packagesLock = new object();
        private volatile bool _initialized;
        private volatile List<string> _packageNamesCache;
        private readonly SemaphoreSlim _initSemaphore = new SemaphoreSlim(1, 1);
        private long _defaultIdleMemoryBudgetBytes;

        public bool Initialized => _initialized;

        public async UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            _defaultIdleMemoryBudgetBytes = options.DefaultIdleMemoryBudgetBytes;
            if (_initialized) return;

            await _initSemaphore.WaitAsync();
            try
            {
                if (_initialized) return;

                try
                {
                    var resourceLocators = Addressables.ResourceLocators;
                    if (resourceLocators != null)
                    {
                        _initialized = true;
                        CLogger.LogInfo($"{DEBUG_FLAG} Addressables already initialized, skipping initialization.");
                        return;
                    }
                }
                catch
                {
                    // ResourceLocators access failed, need to initialize
                }

                try
                {
                    var initializationHandle = Addressables.InitializeAsync(autoReleaseHandle: false);

                    try
                    {
                        if (!initializationHandle.IsValid())
                        {
                            _initialized = true;
                            CLogger.LogInfo($"{DEBUG_FLAG} Addressables initialization handle invalid, assuming already initialized.");
                            return;
                        }

                        await initializationHandle;

                        if (initializationHandle.IsValid())
                        {
                            if (initializationHandle.Status == AsyncOperationStatus.Succeeded)
                            {
                                _initialized = true;
                            }
                            else
                            {
                                CLogger.LogError($"{DEBUG_FLAG} Initialization failed. Status: {initializationHandle.Status}, Exception: {initializationHandle.OperationException}");
                            }
                        }
                        else
                        {
                            _initialized = true;
                            CLogger.LogInfo($"{DEBUG_FLAG} Initialization handle became invalid after await, assuming initialization succeeded.");
                        }
                    }
                    finally
                    {
                        if (initializationHandle.IsValid())
                        {
                            Addressables.Release(initializationHandle);
                        }
                    }
                }
                catch (Exception ex)
                {
                    try
                    {
                        var resourceLocators = Addressables.ResourceLocators;
                        if (resourceLocators != null)
                        {
                            _initialized = true;
                            CLogger.LogInfo($"{DEBUG_FLAG} Initialization exception caught but Addressables appears initialized: {ex.Message}");
                        }
                        else
                        {
                            CLogger.LogError($"{DEBUG_FLAG} Initialization exception: {ex.Message}");
                        }
                    }
                    catch
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Initialization exception and cannot verify status: {ex.Message}");
                    }
                }
            }
            finally
            {
                _initSemaphore.Release();
            }
        }

        public async UniTask DestroyAsync(CancellationToken cancellationToken = default)
        {
            if (!_initialized) return;
            _initialized = false;

            List<IAssetPackage> packages;
            lock (_packagesLock)
            {
                packages = new List<IAssetPackage>(_packages.Values);
                _packages.Clear();
                _packageNamesCache = null;
            }

            for (int i = 0; i < packages.Count; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await packages[i].DestroyAsync();
            }
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException($"{DEBUG_FLAG} Package name is null or empty", nameof(packageName));
            if (!_initialized) throw new InvalidOperationException($"{DEBUG_FLAG} Asset module not initialized");

            lock (_packagesLock)
            {
                if (_packages.ContainsKey(packageName))
                {
                    throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");
                }

                var package = new AddressablesAssetPackage(packageName);
                if (_defaultIdleMemoryBudgetBytes > 0) package.SetCacheIdleMemoryBudget(_defaultIdleMemoryBudgetBytes);
                _packages.Add(packageName, package);
                _packageNamesCache = null;
                return package;
            }
        }

        public IAssetPackage GetPackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return null;
            lock (_packagesLock)
            {
                _packages.TryGetValue(packageName, out var pkg);
                return pkg;
            }
        }

        public async UniTask<bool> RemovePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;

            IAssetPackage package;
            lock (_packagesLock)
            {
                if (!_packages.TryGetValue(packageName, out package))
                {
                    return false;
                }

                _packages.Remove(packageName);
                _packageNamesCache = null;
            }

            await package.DestroyAsync();
            return true;
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            var cache = _packageNamesCache;
            if (cache == null)
            {
                lock (_packagesLock)
                {
                    cache = _packageNamesCache;
                    if (cache == null)
                    {
                        cache = new List<string>(_packages.Count);
                        foreach (var key in _packages.Keys)
                        {
                            cache.Add(key);
                        }
                        _packageNamesCache = cache;
                    }
                }
            }
            return cache;
        }

        public IPatchService CreatePatchService(string packageName)
        {
            throw new NotSupportedException($"{DEBUG_FLAG} Addressables does not support the patch workflow provided by this module.");
        }
    }
}
#endif
