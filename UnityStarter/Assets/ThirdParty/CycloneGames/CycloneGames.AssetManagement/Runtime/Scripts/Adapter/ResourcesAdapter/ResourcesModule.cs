using System;
using System.Collections.Generic;
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

        public bool Initialized => _initialized;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (_initialized) return UniTask.CompletedTask;
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

                var package = new ResourcesAssetPackage(packageName);
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
            throw new NotSupportedException($"{DEBUG_FLAG} Resources does not support the patch workflow.");
        }
    }
}