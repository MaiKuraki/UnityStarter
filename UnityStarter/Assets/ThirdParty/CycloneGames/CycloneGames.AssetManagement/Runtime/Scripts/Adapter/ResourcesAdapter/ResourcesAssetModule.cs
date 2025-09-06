using System;
using System.Collections.Generic;
using System.Linq;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class ResourcesAssetModule : IAssetModule
    {
        private readonly Dictionary<string, IAssetPackage> packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool initialized;

        public bool Initialized => initialized;

        public void Initialize(AssetManagementOptions options = default)
        {
            if (initialized) return;
            
            // Resources don't require special initialization.
            initialized = true;
        }

        public void Destroy()
        {
            if (!initialized) return;
            
            packages.Clear();
            initialized = false;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException("[ResourcesAssetModule] Package name is null or empty", nameof(packageName));
            if (!initialized) throw new InvalidOperationException("[ResourcesAssetModule] Asset module not initialized");
            if (packages.ContainsKey(packageName)) throw new InvalidOperationException($"[ResourcesAssetModule] Package already exists: {packageName}");

            var package = new ResourcesAssetPackage(packageName);
            packages.Add(packageName, package);
            return package;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return null;
            packages.TryGetValue(packageName, out var pkg);
            return pkg;
        }

        public bool RemovePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            if (!packages.ContainsKey(packageName)) return false;
            
            packages.Remove(packageName);
            return true;
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            return packages.Keys.ToList();
        }
    }
}
