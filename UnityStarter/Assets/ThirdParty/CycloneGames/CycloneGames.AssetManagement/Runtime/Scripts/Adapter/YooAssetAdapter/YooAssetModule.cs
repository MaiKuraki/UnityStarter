#if YOOASSET_PRESENT
using System;
using System.Collections.Generic;
using YooAsset;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[YooAssetModule]";
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private readonly object _packagesLock = new object();
        private volatile bool _initialized;
        private volatile List<string> _packageNamesCache;

        public bool Initialized => _initialized;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (_initialized) return UniTask.CompletedTask;

            YooAssets.Initialize();
            if (options.OperationSystemMaxTimeSliceMs > 0)
            {
                YooAssets.SetOperationSystemMaxTimeSlice(options.OperationSystemMaxTimeSliceMs);
            }
            HandleTracker.Enabled = options.EnableHandleTracking;
            _initialized = true;
            return UniTask.CompletedTask;
        }

        public void Destroy()
        {
            if (!_initialized) return;

            lock (_packagesLock)
            {
                _packages.Clear();
                _packageNamesCache = null;
            }

            YooAssets.Destroy();
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

                var yooPackage = YooAssets.CreatePackage(packageName);
                var wrapped = new YooAssetPackage(yooPackage);
                _packages.Add(packageName, wrapped);
                _packageNamesCache = null;
                return wrapped;
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

            IAssetPackage pkg;
            lock (_packagesLock)
            {
                if (!_packages.TryGetValue(packageName, out pkg))
                {
                    return false;
                }
            }

            // Ensure resources are released before removing the package.
            await pkg.DestroyAsync();

            lock (_packagesLock)
            {
                _packages.Remove(packageName);
                _packageNamesCache = null;
            }
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
            var package = GetPackage(packageName);
            if (package == null)
            {
                throw new ArgumentException($"{DEBUG_FLAG} Package not found: {packageName}", nameof(packageName));
            }
            return new YooAssetPatchService(package);
        }
    }
}
#endif // YOOASSET_PRESENT