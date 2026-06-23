using System;
using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class ResourcesModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[ResourcesAssetModule]";
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private readonly object _packagesLock = new object();
        private volatile bool _initialized;
        private volatile List<string> _packageNamesCache;
        private long _defaultIdleMemoryBudgetBytes;

        public bool Initialized => _initialized;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            _defaultIdleMemoryBudgetBytes = options.DefaultIdleMemoryBudgetBytes;
            if (_initialized) return UniTask.CompletedTask;
            _initialized = true;
            return UniTask.CompletedTask;
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

                var package = new ResourcesAssetPackage(packageName);
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
            throw new NotSupportedException($"{DEBUG_FLAG} Resources does not support the patch workflow.");
        }
    }
}
