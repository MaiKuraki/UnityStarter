#if ADDRESSABLES_PRESENT
using System;
using System.Collections.Generic;
using UnityEngine.ResourceManagement.AsyncOperations;
using Cysharp.Threading.Tasks;
using UnityEngine.AddressableAssets;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class AddressablesModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[AddressablesAssetModule]";
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private readonly object _packagesLock = new object();
        private volatile bool _initialized;
        private AsyncOperationHandle _initializationHandle;
        private volatile List<string> _packageNamesCache;

        public bool Initialized => _initialized;

        public async UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (_initialized) return;

            try
            {
                var resourceLocators = Addressables.ResourceLocators;
                if (resourceLocators != null)
                {
                    _initialized = true;
                    UnityEngine.Debug.Log($"{DEBUG_FLAG} Addressables already initialized, skipping initialization.");
                    return;
                }
            }
            catch
            {
                // ResourceLocators access failed, need to initialize
            }

            try
            {
                _initializationHandle = Addressables.InitializeAsync();

                if (!_initializationHandle.IsValid())
                {
                    _initialized = true;
                    UnityEngine.Debug.Log($"{DEBUG_FLAG} Addressables initialization handle invalid, assuming already initialized.");
                    return;
                }

                await _initializationHandle;

                if (_initializationHandle.IsValid())
                {
                    if (_initializationHandle.Status == AsyncOperationStatus.Succeeded)
                    {
                        _initialized = true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialization failed. Status: {_initializationHandle.Status}, Exception: {_initializationHandle.OperationException}");
                    }
                }
                else
                {
                    _initialized = true;
                    UnityEngine.Debug.Log($"{DEBUG_FLAG} Initialization handle became invalid after await, assuming initialization succeeded.");
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
                        UnityEngine.Debug.Log($"{DEBUG_FLAG} Initialization exception caught but Addressables appears initialized: {ex.Message}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialization exception: {ex.Message}");
                    }
                }
                catch
                {
                    UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialization exception and cannot verify status: {ex.Message}");
                }
            }
        }

        public void Destroy()
        {
            if (!_initialized) return;

            if (_initializationHandle.IsValid())
            {
                Addressables.Release(_initializationHandle);
            }

            lock (_packagesLock)
            {
                _packages.Clear();
                _packageNamesCache = null;
            }
            _initialized = false;
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

        public UniTask<bool> RemovePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return UniTask.FromResult(false);

            lock (_packagesLock)
            {
                if (!_packages.TryGetValue(packageName, out _))
                {
                    return UniTask.FromResult(false);
                }

                _packages.Remove(packageName);
                _packageNamesCache = null;
            }
            return UniTask.FromResult(true);
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