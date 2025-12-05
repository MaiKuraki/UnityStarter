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
        private const string DEBUG_FLAG = "[AddressablesAssetModule]";
        private readonly Dictionary<string, IAssetPackage> packages = new Dictionary<string, IAssetPackage>(StringComparer.Ordinal);
        private bool initialized;
        private AsyncOperationHandle initializationHandle;
        private List<string> packageNamesCache;

        public bool Initialized => initialized;

        public async UniTask InitializeAsync(AssetManagementOptions options = default)
        {
            if (initialized) return;
            
            // Check if Addressables is already initialized
            try
            {
                var resourceLocators = Addressables.ResourceLocators;
                if (resourceLocators != null)
                {
                    // Addressables is already initialized
                    initialized = true;
                    UnityEngine.Debug.Log($"{DEBUG_FLAG} Addressables already initialized, skipping initialization.");
                    return;
                }
            }
            catch
            {
                // ResourceLocators access failed, need to initialize
            }
            
            // Initialize Addressables if not already initialized
            try
            {
                initializationHandle = Addressables.InitializeAsync();
                
                if (!initializationHandle.IsValid())
                {
                    initialized = true;
                    UnityEngine.Debug.Log($"{DEBUG_FLAG} Addressables initialization handle invalid, assuming already initialized.");
                    return;
                }
                
                await initializationHandle;
                
                if (initializationHandle.IsValid())
                {
                    if (initializationHandle.Status == AsyncOperationStatus.Succeeded)
                    {
                        initialized = true;
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialization failed. Status: {initializationHandle.Status}, Exception: {initializationHandle.OperationException}");
                    }
                }
                else
                {
                    initialized = true;
                    UnityEngine.Debug.Log($"{DEBUG_FLAG} Initialization handle became invalid after await, assuming initialization succeeded.");
                }
            }
            catch (Exception ex)
            {
                try
                {
                    var resourceLocators = Addressables.ResourceLocators;
                    if (resourceLocators != null)
                    {
                        initialized = true;
                        UnityEngine.Debug.Log($"{DEBUG_FLAG} Initialization exception caught but Addressables appears initialized: {ex.Message}");
                    }
                    else
                    {
                        UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialization exception: {ex.Message}");
                    }
                }
                catch
                {
                    UnityEngine.Debug.LogError($"{DEBUG_FLAG} Initialization exception and cannot verify status: {ex.Message}");
                }
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
            if (string.IsNullOrEmpty(packageName)) throw new ArgumentException($"{DEBUG_FLAG} Package name is null or empty", nameof(packageName));
            if (!initialized) throw new InvalidOperationException($"{DEBUG_FLAG} Asset module not initialized");
            if (packages.ContainsKey(packageName)) throw new InvalidOperationException($"{DEBUG_FLAG} Package already exists: {packageName}");

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

        public UniTask<bool> RemovePackageAsync(string packageName)
        {
            if (string.IsNullOrEmpty(packageName)) return UniTask.FromResult(false);
            if (!packages.TryGetValue(packageName, out var package)) return UniTask.FromResult(false);
            
            // Addressables doesn't support destroying packages, 
            // In current impl, DestroyAsync is no-op/completed task.
            // await package.DestroyAsync(); 

            packages.Remove(packageName);
            packageNamesCache = null;
            return UniTask.FromResult(true);
        }

        public IReadOnlyList<string> GetAllPackageNames()
        {
            if (packageNamesCache == null)
            {
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
            throw new NotSupportedException($"{DEBUG_FLAG} Addressables does not support the patch workflow provided by this module.");
        }
    }
}
#endif // ADDRESSABLES_PRESENT