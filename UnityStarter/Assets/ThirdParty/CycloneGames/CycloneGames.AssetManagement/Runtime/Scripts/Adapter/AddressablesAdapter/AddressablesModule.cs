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
        private readonly Dictionary<string, IAssetPackage> packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool initialized;
        private AsyncOperationHandle initializationHandle;
        private List<string> packageNamesCache;

        public bool Initialized => initialized;

        public async UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (initialized) return;
            
            initializationHandle = Addressables.InitializeAsync();
            await initializationHandle;
            
            if (initializationHandle.Status == AsyncOperationStatus.Succeeded)
            {
                initialized = true;
            }
            else
            {
                UnityEngine.Debug.LogError($"[AddressablesAssetModule] Initialization failed. Status: {initializationHandle.Status}, Exception: {initializationHandle.OperationException}");
            }
        }

        public void Destroy()
        {
            if (!initialized) return;
            
            if (initializationHandle.IsValid())
            {
                Addressables.Release(initializationHandle);
            }
            
            packages.Clear();
            initialized = false;
        }

        public IAssetPackage CreatePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException("[AddressablesAssetModule] Package name is null or empty", nameof(packageName));
            if (!initialized) throw new InvalidOperationException("[AddressablesAssetModule] Asset module not initialized");
            if (packages.ContainsKey(packageName)) throw new InvalidOperationException($"[AddressablesAssetModule] Package already exists: {packageName}");

            var package = new AddressablesAssetPackage(packageName);
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

        public bool RemovePackage(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return false;
            if (!packages.Remove(packageName)) return false;
            
            packageNamesCache = null; // Invalidate cache
            return true;
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            if (packageNamesCache == null)
            {
                // This is a simplified ToList() to avoid LINQ dependency for clarity.
                packageNamesCache = new List<string>(packages.Count);
                foreach (var kvp in packages)
                {
                    packageNamesCache.Add(kvp.Key);
                }
            }
            return packageNamesCache;
        }

        public IPatchService CreatePatchService(string packageName)
        {
            throw new NotSupportedException("Addressables does not support the patch workflow provided by this module.");
        }
    }
}
#endif // ADDRESSABLES_PRESENT
