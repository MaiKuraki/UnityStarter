#if ADDRESSABLES_PRESENT
using Cysharp.Threading.Tasks;
using System;
using System.IO;
using System.Threading;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations;

namespace CycloneGames.AssetManagement.Runtime
{
    internal sealed class AddressablesAssetPackage : IAssetPackage
    {
        private readonly string packageName;
        private int nextId = 1;

        public string Name => packageName;

        public AddressablesAssetPackage(string name)
        {
            packageName = name;
        }

        public UniTask<bool> InitializeAsync(AssetPackageInitOptions options, CancellationToken cancellationToken = default)
        {
            // Addressables initializes globally and automatically.
            return UniTask.FromResult(true);
        }

        public UniTask DestroyAsync()
        {
            // Addressables does not have a package-level destroy concept.
            return UniTask.CompletedTask;
        }

        public async UniTask<string> RequestPackageVersionAsync(bool appendTimeTicks = true, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            // Addressables does not provide a runtime API to retrieve the catalog version.
            // We support multiple game scenarios with intelligent fallback:
            // 
            // Scenarios supported:
            // 1. Standalone game: Uses StreamingAssets (bundled version)
            // 2. Standalone + DLC: Uses PersistentDataPath (DLC version) or StreamingAssets (base version)
            // 3. Online game: Uses remote server (server version) or StreamingAssets (fallback)
            // 4. Hot-update game: Uses remote server → PersistentDataPath (cached) → StreamingAssets (baseline)
            //
            // Priority order (adaptive based on catalog type):
            // 1. Remote server (if remote catalog detected) - for online/hot-update games
            // 2. Persistent data path (if exists) - for DLC/hot-update cached versions
            // 3. StreamingAssets (always available) - for standalone games and baseline version
            string version = string.Empty;

            bool hasRemoteCatalog = HasRemoteCatalog();

            // Priority 1
            if (hasRemoteCatalog)
            {
                version = await TryLoadVersionFromRemoteAsync(timeoutSeconds, cancellationToken);
            }

            // Priority 2
            if (string.IsNullOrEmpty(version))
            {
                version = await TryLoadVersionFromPersistentDataAsync(cancellationToken);
            }

            // Priority 3
            if (string.IsNullOrEmpty(version))
            {
                version = await TryLoadVersionFromStreamingAssetsAsync(cancellationToken);
            }

            if (!string.IsNullOrEmpty(version))
            {
                if (appendTimeTicks)
                {
                    version = $"{version}.{DateTime.Now.Ticks}";
                }
                return version;
            }

            Debug.LogWarning("[AddressablesAssetPackage] Version data not found. Make sure Addressables content was built with the build pipeline.");
            return string.Empty;
        }

        /// <summary>
        /// Checks if Addressables is using a remote catalog.
        /// This helps optimize version retrieval for standalone games (skip remote requests).
        /// </summary>
        private bool HasRemoteCatalog()
        {
            try
            {
                var resourceLocators = Addressables.ResourceLocators;
                if (resourceLocators != null)
                {
                    foreach (var locator in resourceLocators)
                    {
                        if (locator.Keys != null)
                        {
                            foreach (var key in locator.Keys)
                            {
                                if (key is string keyStr && !string.IsNullOrEmpty(keyStr))
                                {
                                    // If we find any remote URL, we're using remote catalog
                                    if (keyStr.StartsWith("http://") || keyStr.StartsWith("https://"))
                                    {
                                        return true;
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch
            {

            }

            return false;
        }

        private async UniTask<string> TryLoadVersionFromRemoteAsync(int timeoutSeconds, CancellationToken cancellationToken)
        {
            try
            {
                // Get remote version URL from Addressables catalog URL
                string remoteUrl = GetRemoteVersionUrl();
                if (string.IsNullOrEmpty(remoteUrl))
                {
                    return string.Empty;
                }

                using (var request = UnityEngine.Networking.UnityWebRequest.Get(remoteUrl))
                {
                    request.timeout = timeoutSeconds;
                    await request.SendWebRequest().WithCancellation(cancellationToken);

                    if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                    {
                        string jsonContent = request.downloadHandler.text;
                        var versionData = JsonUtility.FromJson<VersionDataJson>(jsonContent);
                        if (versionData != null && !string.IsNullOrEmpty(versionData.contentVersion))
                        {
                            Debug.Log($"[AddressablesAssetPackage] Loaded version from remote: {versionData.contentVersion}");

                            await SaveVersionToPersistentDataAsync(versionData.contentVersion, cancellationToken);

                            return versionData.contentVersion;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesAssetPackage] Failed to load version from remote: {ex.Message}");
            }

            return string.Empty;
        }

        private async UniTask SaveVersionToPersistentDataAsync(string version, CancellationToken cancellationToken)
        {
            try
            {
                string versionFilePath = AddressablesVersionPathHelper.GetPersistentVersionPath();
                string directory = Path.GetDirectoryName(versionFilePath);

                if (!Directory.Exists(directory))
                {
                    Directory.CreateDirectory(directory);
                }

                var versionData = new VersionDataJson { contentVersion = version };
                string jsonContent = JsonUtility.ToJson(versionData, true);

                await System.Threading.Tasks.Task.Run(() => File.WriteAllText(versionFilePath, jsonContent), cancellationToken);
                Debug.Log($"[AddressablesAssetPackage] Saved version to persistent data: {version}");
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesAssetPackage] Failed to save version to persistent data: {ex.Message}");
            }
        }

        private string GetRemoteVersionUrl()
        {
            try
            {
                // Try to get remote catalog URL from Addressables ResourceLocators
                var resourceLocators = Addressables.ResourceLocators;
                if (resourceLocators != null)
                {
                    foreach (var locator in resourceLocators)
                    {
                        if (locator.Keys != null)
                        {
                            foreach (var key in locator.Keys)
                            {
                                if (key is string keyStr && !string.IsNullOrEmpty(keyStr))
                                {
                                    // Check if this is a remote URL
                                    if (keyStr.StartsWith("http://") || keyStr.StartsWith("https://"))
                                    {
                                        // Derive version URL from catalog URL
                                        return AddressablesVersionPathHelper.GetRemoteVersionUrl(keyStr);
                                    }
                                }
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesAssetPackage] Failed to determine remote version URL: {ex.Message}");
            }

            return string.Empty;
        }

        private async UniTask<string> TryLoadVersionFromPersistentDataAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Load version from persistent data path (writable, for hot updates)
                // This is where downloaded version info is saved after first hot update
                // NOTE: This may not exist on first install (before any hot updates)
                // If it doesn't exist, we'll fall back to StreamingAssets
                string versionFilePath = AddressablesVersionPathHelper.GetPersistentVersionPath();

                // Check if file exists before attempting to read
                // On first install, PersistentDataPath may not have been created yet
                if (!File.Exists(versionFilePath))
                {
                    return string.Empty;
                }

                string jsonContent = await ReadFileAsync(versionFilePath, cancellationToken);
                if (!string.IsNullOrEmpty(jsonContent))
                {
                    var versionData = JsonUtility.FromJson<VersionDataJson>(jsonContent);
                    if (versionData != null && !string.IsNullOrEmpty(versionData.contentVersion))
                    {
                        Debug.Log($"[AddressablesAssetPackage] Loaded version from persistent data (cached): {versionData.contentVersion}");
                        return versionData.contentVersion;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesAssetPackage] Failed to load version from persistent data: {ex.Message}");
            }

            return string.Empty;
        }

        private async UniTask<string> ReadFileAsync(string filePath, CancellationToken cancellationToken)
        {
#if UNITY_ANDROID || UNITY_WEBGL
            // On Android and WebGL, use UnityWebRequest for file access
            using (var request = UnityEngine.Networking.UnityWebRequest.Get(filePath))
            {
                await request.SendWebRequest().WithCancellation(cancellationToken);
                if (request.result == UnityEngine.Networking.UnityWebRequest.Result.Success)
                {
                    return request.downloadHandler.text;
                }
            }
            return string.Empty;
#else
            // On other platforms, use direct file I/O
            if (File.Exists(filePath))
            {
                return await System.Threading.Tasks.Task.Run(() => File.ReadAllText(filePath), cancellationToken);
            }
            return string.Empty;
#endif
        }

        private async UniTask<string> TryLoadVersionFromStreamingAssetsAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Load version from StreamingAssets (read-only, initial version bundled with app)
                // Addressables stores content in StreamingAssets/aa/<Platform> structure
                // This is ALWAYS available in full package and serves as the baseline version
                // On first install (before PersistentDataPath exists), this is the primary source

                // Try all possible paths in order of priority
                string[] possiblePaths = AddressablesVersionPathHelper.GetStreamingAssetsVersionPaths();

                foreach (string versionFilePath in possiblePaths)
                {
                    try
                    {
                        string jsonContent = await ReadFileAsync(versionFilePath, cancellationToken);
                        if (!string.IsNullOrEmpty(jsonContent))
                        {
                            var versionData = JsonUtility.FromJson<VersionDataJson>(jsonContent);
                            if (versionData != null && !string.IsNullOrEmpty(versionData.contentVersion))
                            {
                                Debug.Log($"[AddressablesAssetPackage] Loaded version from StreamingAssets: {versionFilePath} -> {versionData.contentVersion}");
                                return versionData.contentVersion;
                            }
                        }
                    }
                    catch
                    {
                        // Continue to next path if this one fails
                        continue;
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"[AddressablesAssetPackage] Failed to load version from StreamingAssets: {ex.Message}");
            }

            return string.Empty;
        }

        [System.Serializable]
        private class VersionDataJson
        {
            public string contentVersion;
        }

        public async UniTask<bool> UpdatePackageManifestAsync(string packageVersion, int timeoutSeconds = 60, CancellationToken cancellationToken = default)
        {
            // This corresponds to Addressables.UpdateCatalogs.
            var handle = Addressables.UpdateCatalogs();
            await handle.WithCancellation(cancellationToken);
            var success = handle.Status == AsyncOperationStatus.Succeeded;
            Addressables.Release(handle);
            return success;
        }

        public UniTask<bool> ClearCacheFilesAsync(ClearCacheMode clearMode = ClearCacheMode.All, object tags = null, CancellationToken cancellationToken = default)
        {
            if (clearMode == ClearCacheMode.ByTags)
            {
                Debug.LogWarning("[AddressablesAssetPackage] ClearCacheFilesAsync by tags is not supported by Addressables. All cache will be cleared.");
            }
            return UniTask.FromResult(Caching.ClearCache());
        }

        public IDownloader CreateDownloaderForAll(int downloadingMaxNumber, int failedTryAgain)
        {
            throw new NotSupportedException("Addressables does not support creating a downloader for 'all' assets. Use tags or locations.");
        }

        public IDownloader CreateDownloaderForTags(string[] tags, int downloadingMaxNumber, int failedTryAgain)
        {
            if (tags == null || tags.Length == 0) return new AddressableDownloader(default);
            var handle = Addressables.DownloadDependenciesAsync(tags);
            return new AddressableDownloader(handle);
        }

        public IDownloader CreateDownloaderForLocations(string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain)
        {
            if (locations == null || locations.Length == 0) return new AddressableDownloader(default);
            var handle = Addressables.DownloadDependenciesAsync(locations);
            return new AddressableDownloader(handle);
        }

        public UniTask<IDownloader> CreatePreDownloaderForAllAsync(string packageVersion, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Addressables does not support pre-downloading for a specific, non-active catalog version."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForTagsAsync(string packageVersion, string[] tags, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Addressables does not support pre-downloading for a specific, non-active catalog version."));
        }

        public UniTask<IDownloader> CreatePreDownloaderForLocationsAsync(string packageVersion, string[] locations, bool recursiveDownload, int downloadingMaxNumber, int failedTryAgain, CancellationToken cancellationToken = default)
        {
            return UniTask.FromException<IDownloader>(new NotSupportedException("Addressables does not support pre-downloading for a specific, non-active catalog version."));
        }

        [Obsolete("Synchronous asset loading is deprecated and can cause performance issues. Use LoadAssetAsync instead.", true)]
        public IAssetHandle<TAsset> LoadAssetSync<TAsset>(string location) where TAsset : UnityEngine.Object
        {
            throw new NotSupportedException("Synchronous asset loading is not supported by the Addressables provider.");
        }

        public IAssetHandle<TAsset> LoadAssetAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetAsync<TAsset>(location);
            var id = RegisterHandle();
            var wrapped = AddressableAssetHandle<TAsset>.Create(id, handle, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AssetAsync {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        public IAllAssetsHandle<TAsset> LoadAllAssetsAsync<TAsset>(string location, CancellationToken cancellationToken = default) where TAsset : UnityEngine.Object
        {
            var handle = Addressables.LoadAssetsAsync<TAsset>(location, null);
            var id = RegisterHandle();
            var wrapped = AddressableAllAssetsHandle<TAsset>.Create(id, handle, cancellationToken);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"AllAssets {typeof(TAsset).Name} : {location}");
            return wrapped;
        }

        [Obsolete("Synchronous instantiation is deprecated and can cause performance issues. Use InstantiateAsync instead.", true)]
        public GameObject InstantiateSync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false)
        {
            throw new NotSupportedException("Synchronous instantiation is not supported by the Addressables provider.");
        }

        public IInstantiateHandle InstantiateAsync(IAssetHandle<GameObject> handle, Transform parent = null, bool worldPositionStays = false, bool setActive = true)
        {
            if (handle?.AssetObject == null)
            {
                return new FailedInstantiateHandle("Cannot instantiate from a null or invalid asset handle.");
            }

            var op = Addressables.InstantiateAsync(handle.AssetObject, parent, worldPositionStays, setActive);
            var id = RegisterHandle();
            // Pass CancellationToken.None as instantiation cancellation is typically handled by releasing the handle, which Addressables does automatically.
            var wrapped = AddressableInstantiateHandle.Create(id, op, CancellationToken.None);
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"InstantiateAsync : {handle.AssetObject.name}");
            return wrapped;
        }

        public ISceneHandle LoadSceneAsync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single, bool activateOnLoad = true, int priority = 100)
        {
            var op = Addressables.LoadSceneAsync(sceneLocation, loadMode, activateOnLoad, priority);
            var id = RegisterHandle();
            var h = AddressableSceneHandle.Create(id, op, CancellationToken.None); // Scene loading cancellation is handled by unloading.
            if (HandleTracker.Enabled) HandleTracker.Register(id, packageName, $"SceneAsync : {sceneLocation}");
            return h;
        }

        [Obsolete("Synchronous scene loading is deprecated and can cause performance issues. Use LoadSceneAsync instead.", true)]
        public ISceneHandle LoadSceneSync(string sceneLocation, LoadSceneMode loadMode = LoadSceneMode.Single)
        {
            throw new NotSupportedException("Synchronous scene loading is not supported by the Addressables provider.");
        }

        public async UniTask UnloadSceneAsync(ISceneHandle sceneHandle)
        {
            if (sceneHandle is AddressableSceneHandle sh)
            {
                if (sh.Raw.IsValid())
                {
                    await Addressables.UnloadSceneAsync(sh.Raw);
                }
                // Return to pool manually since ISceneHandle is not IDisposable in this architecture
                sh.ReturnToPool();
            }
        }

        public UniTask UnloadUnusedAssetsAsync()
        {
            Debug.LogWarning("[AddressablesAssetPackage] UnloadUnusedAssetsAsync is not recommended. Please release individual asset handles via Dispose() for precise memory management.");
            return UniTask.CompletedTask;
        }

        private int RegisterHandle()
        {
            return Interlocked.Increment(ref nextId);
        }
    }
}
#endif