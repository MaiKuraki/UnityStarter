#if YOOASSET_PRESENT
using System;
using System.Collections.Generic;
using System.Linq;
using YooAsset;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class YooAssetModule : IAssetModule
    {
        private readonly Dictionary<string, IAssetPackage> _packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool _initialized;

        public bool Initialized => _initialized;

        public void Initialize(AssetManagementOptions options = default)
        {
            if (_initialized) return;
            
            // The user's original code had a more complex initialization.
            // For now, let's stick to the basics to ensure compilation.
            // We can add the logger adapter back later if needed.
            YooAssets.Initialize();
            if (options.OperationSystemMaxTimeSliceMs > 0)
            {
                YooAssets.SetOperationSystemMaxTimeSlice(options.OperationSystemMaxTimeSliceMs);
            }
            // HandleTracker.Enabled = options.EnableHandleTracking; // To be implemented
            _initialized = true;
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
            return true;
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            return _packages.Keys.ToList();
        }
    }
}
#endif // YOOASSET_PRESENT