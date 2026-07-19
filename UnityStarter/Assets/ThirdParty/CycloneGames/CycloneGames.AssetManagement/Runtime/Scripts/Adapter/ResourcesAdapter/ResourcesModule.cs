using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class ResourcesModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[ResourcesAssetModule]";
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool _initialized;
        private bool _destroying;
        private UniTask _destroyTask;
        private IReadOnlyList<string> _packageNamesCache;
        private AssetCacheTuning _defaultCacheTuning;

        public bool Initialized => _initialized;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            AssetRuntimeGuard.EnsureMainThread();
            if (_destroying)
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Module destruction is in progress.");
            }

            if (_initialized)
            {
                return UniTask.CompletedTask;
            }

            if (_packages.Count > 0)
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Complete package cleanup before reinitializing the module.");
            }

            options = options.Normalized();
            _defaultCacheTuning = options.DefaultCacheTuning;
            _initialized = true;
            return UniTask.CompletedTask;
        }

        public UniTask DestroyAsync()
        {
            AssetRuntimeGuard.EnsureMainThread();
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
            var packageNames = new List<string>(_packages.Keys);
            List<Exception> failures = null;
            try
            {
                for (int i = 0; i < packageNames.Count; i++)
                {
                    string packageName = packageNames[i];
                    if (!_packages.TryGetValue(packageName, out IAssetPackage package))
                    {
                        continue;
                    }

                    try
                    {
                        await package.DestroyAsync();
                        _packages.Remove(packageName);
                        _packageNamesCache = null;
                    }
                    catch (Exception ex) when (AssetRuntimeGuard.IsRecoverableException(ex))
                    {
                        failures ??= new List<Exception>();
                        failures.Add(ex);
                    }
                }
            }
            finally
            {
                _destroying = false;
            }

            if (failures != null)
            {
                throw new AggregateException($"{DEBUG_FLAG} One or more packages failed to shut down.", failures);
            }

            _initialized = false;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException($"{DEBUG_FLAG} Package name is null or empty", nameof(packageName));
            if (!_initialized) throw new InvalidOperationException($"{DEBUG_FLAG} Asset module not initialized");

            if (_packages.ContainsKey(packageName))
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");
            }

            var package = new ResourcesAssetPackage(packageName);
            package.ConfigureCache(_defaultCacheTuning);
            _packages.Add(packageName, package);
            _packageNamesCache = null;
            return package;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrEmpty(packageName)) return null;
            _packages.TryGetValue(packageName, out var package);
            return package;
        }

        public async UniTask<bool> RemovePackageAsync(string packageName)
        {
            AssetRuntimeGuard.EnsureMainThread();
            EnsureOperational();
            if (string.IsNullOrEmpty(packageName)) return false;

            if (!_packages.TryGetValue(packageName, out IAssetPackage package))
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
                var packageNames = new List<string>(_packages.Count);
                foreach (string packageName in _packages.Keys)
                {
                    packageNames.Add(packageName);
                }

                _packageNamesCache = new ReadOnlyCollection<string>(packageNames);
            }

            return _packageNamesCache;
        }

        private void EnsureOperational()
        {
            if (_destroying)
            {
                throw new InvalidOperationException($"{DEBUG_FLAG} Module destruction is in progress.");
            }
        }
    }
}
