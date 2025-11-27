#if YOOASSET_PRESENT
using System;
using System.Collections.Generic;
using System.Linq;
using YooAsset;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[YooAssetModule]";
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool _initialized;
        private List<string> _packageNamesCache;

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
            YooAssets.Destroy();
            _packages.Clear();
            _initialized = false;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException($"{DEBUG_FLAG} Package name is null or empty", nameof(packageName));
            if (!_initialized) throw new InvalidOperationException($"{DEBUG_FLAG} Asset module not initialized");
            if (_packages.ContainsKey(packageName)) throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");

            var yooPackage = YooAssets.CreatePackage(packageName);
            var wrapped = new YooAssetPackage(yooPackage);
            _packages.Add(packageName, wrapped);
            _packageNamesCache = null; // Invalidate cache
            return wrapped;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return null;
            _packages.TryGetValue(packageName, out var pkg);
            return pkg;
        }

        public async UniTask<bool> RemovePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            if (!_packages.TryGetValue(packageName, out var pkg)) return false;

            // Ensure resources are released before removing the package.
            // We await the destruction to ensure all async cleanup (if any) completes.
            await pkg.DestroyAsync();

            _packages.Remove(packageName);
            // Since YooAssetPackage.DestroyAsync already calls YooAssets.RemovePackage(packageName),

            _packageNamesCache = null; // Invalidate cache
            return true;
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            if (_packageNamesCache == null)
            {
                _packageNamesCache = _packages.Keys.ToList();
            }
            return _packageNamesCache;
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