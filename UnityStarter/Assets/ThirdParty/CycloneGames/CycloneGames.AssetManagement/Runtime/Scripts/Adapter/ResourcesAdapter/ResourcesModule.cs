using System;
using System.Collections.Generic;
using System.Linq;
using Cysharp.Threading.Tasks;

namespace CycloneGames.AssetManagement.Runtime
{
    public sealed class ResourcesModule : IAssetModule
    {
        private const string DEBUG_FLAG = "[ResourcesAssetModule]";
        private readonly Dictionary<string, IAssetPackage> packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool initialized;
        private List<string> packageNamesCache;

        public bool Initialized => initialized;

        public UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (initialized) return UniTask.CompletedTask;

            // Resources don't require special initialization.
            initialized = true;
            return UniTask.CompletedTask;
        }

        public void Destroy()
        {
            if (!initialized) return;

            packages.Clear();
            initialized = false;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException($"{DEBUG_FLAG} Package name is null or empty", nameof(packageName));
            if (!initialized) throw new InvalidOperationException($"{DEBUG_FLAG} Asset module not initialized");
            if (packages.ContainsKey(packageName)) throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");

            var package = new ResourcesAssetPackage(packageName);
            packages.Add(packageName, package);
            packageNamesCache = null; // Invalidate cache
            return package;
        }

        public IAssetPackage GetPackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return null;
            packages.TryGetValue(packageName, out var pkg);
            return pkg;
        }

        public UniTask<bool> RemovePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return UniTask.FromResult(false);
            if (!packages.TryGetValue(packageName, out var package)) return UniTask.FromResult(false);

            // await package.DestroyAsync(); // Currently no-op for Resources

            packages.Remove(packageName);
            packageNamesCache = null; // Invalidate cache
            return UniTask.FromResult(true);
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            if (packageNamesCache == null)
            {
                packageNamesCache = packages.Keys.ToList();
            }
            return packageNamesCache;
        }

        public IPatchService CreatePatchService(string packageName)
        {
            throw new NotSupportedException($"{DEBUG_FLAG} Resources does not support the patch workflow.");
        }
    }
}