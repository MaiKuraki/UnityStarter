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
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool _initialized;
        private List<string> _packageNamesCache;

        public bool Initialized => _initialized;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (_initialized) return UniTask.CompletedTask;
            
            // The user's original code had a more complex initialization.
            // For now, let's stick to the basics to ensure compilation.
            // We can add the logger adapter back later if needed.
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
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException("Package name is null or empty", nameof(packageName));
            if (!_initialized) throw new InvalidOperationException("Asset module not initialized");
            if (_packages.ContainsKey(packageName)) throw new InvalidOperationException($"Package already exists: {packageName}");

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

        public bool RemovePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            if (!_packages.TryGetValue(packageName, out var pkg)) return false;
            
            _packages.Remove(packageName);
            YooAssets.RemovePackage(packageName);
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
                throw new ArgumentException($"Package not found: {packageName}", nameof(packageName));
            }
            return new YooAssetPatchService(package);
        }
    }
}
#endif // YOOASSET_PRESENT
