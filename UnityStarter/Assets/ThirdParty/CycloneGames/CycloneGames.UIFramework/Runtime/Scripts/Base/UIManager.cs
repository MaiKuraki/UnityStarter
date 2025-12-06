using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logger;
using CycloneGames.Service.Runtime;         // For IMainCameraService
using CycloneGames.Factory.Runtime;         // For IUnityObjectSpawner
using CycloneGames.AssetManagement.Runtime; // For IAssetPathBuilderFactory

namespace CycloneGames.UIFramework.Runtime
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        private IAssetPathBuilder assetPathBuilder;
        private IUnityObjectSpawner objectSpawner; // Should be IObjectSpawner<UnityEngine.Object> or similar
        private IMainCameraService mainCameraService;
        private IAssetPackage assetPackage; // Generic asset package for loading configs/prefabs
        private IUIWindowTransitionDriver transitionDriver; // Optional transition driver applied to spawned windows
        private UIRoot uiRoot;
        private class AssetCacheEntry<T> where T : UnityEngine.Object
        {
            public IAssetHandle<T> Handle;
            public int RefCount;
        }

        // Prefab Cache (GameObject)
        private readonly Dictionary<string, AssetCacheEntry<GameObject>> prefabHandleCache = new Dictionary<string, AssetCacheEntry<GameObject>>(16);
        private readonly List<string> prefabHandleLru = new List<string>(16);
        private const int PrefabHandleCacheMax = 16;

        // Config Cache (UIWindowConfiguration)
        private readonly Dictionary<string, AssetCacheEntry<UIWindowConfiguration>> configHandleCache = new Dictionary<string, AssetCacheEntry<UIWindowConfiguration>>(16);
        private readonly List<string> configHandleLru = new List<string>(16);
        private const int ConfigHandleCacheMax = 16;

        // Throttling instantiate per frame
        private int maxInstantiatesPerFrame = 2;
        private int instantiatesThisFrame = 0;

        // Tracks ongoing opening operations to prevent duplicate concurrent opens
        // and to allow CloseUI to wait for opening to complete.
        private Dictionary<string, UniTaskCompletionSource<UIWindow>> uiOpenTCS = new Dictionary<string, UniTaskCompletionSource<UIWindow>>();

        // Tracks active windows for quick access and management
        private Dictionary<string, UIWindow> activeWindows = new Dictionary<string, UIWindow>();


        /// <summary>
        /// Initializes the UIManager with necessary services. Attempts to resolve the asset package from locator if not provided.
        /// </summary>
        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IUnityObjectSpawner spawner, IMainCameraService cameraService)
        {
            Initialize(assetPathBuilderFactory, spawner, cameraService, null);
        }

        /// <summary>
        /// Initializes the UIManager with necessary services and an explicit asset package.
        /// </summary>
        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IUnityObjectSpawner spawner, IMainCameraService cameraService, IAssetPackage package)
        {
            if (assetPathBuilderFactory == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} AssetPathBuilderFactory is null. UIManager cannot function.");
                return;
            }
            this.assetPathBuilder = assetPathBuilderFactory.Create("UI"); // Assuming "UI" is a valid type
            if (this.assetPathBuilder == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to create AssetPathBuilder for type 'UI'. Check your factory configuration.");
                // Potentially disable UIManager functionality or throw an exception
                return;
            }

            this.objectSpawner = spawner;
            if (this.objectSpawner == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} ObjectSpawner is null. UIManager cannot instantiate UIWindows.");
                return;
            }

            this.mainCameraService = cameraService;
            // mainCameraService can be null if not essential for all UI setups, handle gracefully.
            if (this.mainCameraService == null)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} MainCameraService is null. UI Camera stacking might not work.");
            }

            // Resolve asset package
            this.assetPackage = package ?? AssetManagementLocator.DefaultPackage;
            if (this.assetPackage == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} IAssetPackage is null. Ensure AssetManagement is initialized and DefaultPackage assigned or pass a package explicitly.");
            }

            AddUICameraToMainCameraStack();
        }

        private UIRoot TryGetUIRoot()
        {
            if (uiRoot == null)
            {
                uiRoot = GameObject.FindFirstObjectByType<UIRoot>();
                if (uiRoot == null)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} UIRoot not found in the scene. UIManager requires a UIRoot to function.");
                }
            }
            return uiRoot;
        }

        /// <summary>
        /// Initializes the UIManager with services, asset package and a transition driver.
        /// </summary>
        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IUnityObjectSpawner spawner, IMainCameraService cameraService, IAssetPackage package, IUIWindowTransitionDriver driver)
        {
            Initialize(assetPathBuilderFactory, spawner, cameraService, package);
            this.transitionDriver = driver;
        }

        private void Awake()
        {
            UnityEngine.Application.onBeforeRender += ResetPerFrameBudget;
            // It's better to get UIRoot in Initialize if UIManager is created and initialized from code.
            // If UIManager is a scene object and Initialize is called later, Awake can find UIRoot.
            TryGetUIRoot();
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            // clean up the window handles to prevent leaks.
            if (uiRoot == null || uiRoot.gameObject.scene == scene)
            {
                CleanupAllWindows();
            }
        }

        private void CleanupAllWindows()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Cleaning up all active windows due to scene unload.");

            // Config Cache Cleanup: Dispose all handles
            if (configHandleCache != null)
            {
                foreach (var kv in configHandleCache)
                {
                    kv.Value.Handle?.Dispose();
                }
                configHandleCache.Clear();
                configHandleLru.Clear();
            }

            activeWindows.Clear();

            foreach (var kv in uiOpenTCS)
            {
                kv.Value.TrySetCanceled();
            }
            uiOpenTCS.Clear();
            uiRoot = null;
        }

        private void ResetPerFrameBudget()
        {
            instantiatesThisFrame = 0;
        }

        // Start is not typically used if Initialize sets up dependencies.
        // private void Start() { }

        /// <summary>
        /// Opens a UI window by its name.
        /// </summary>
        /// <param name="windowName">The unique name of the window (often matches configuration file name).</param>
        /// <param name="onUIWindowCreated">Optional callback when the window is instantiated and added.</param>
        public void OpenUI(string windowName, System.Action<UIWindow> onUIWindowCreated = null)
        {
            if (uiRoot == null || assetPathBuilder == null || objectSpawner == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UIManager not properly initialized. Cannot open UI: {windowName}");
                onUIWindowCreated?.Invoke(null); // Notify failure
                return;
            }
            OpenUIAsync(windowName, onUIWindowCreated).Forget(); // Fire and forget UniTask
        }

        /// <summary>
        /// Closes a UI window by its name.
        /// </summary>
        public void CloseUI(string windowName)
        {
            if (uiRoot == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UIManager not properly initialized or UIRoot missing. Cannot close UI: {windowName}");
                return;
            }
            CloseUIAsync(windowName).Forget(); // Fire and forget UniTask
        }

        internal async UniTask<UIWindow> OpenUIAsync(string windowName, System.Action<UIWindow> onUIWindowCreated = null, System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} WindowName cannot be null or empty.");
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            if (activeWindows.ContainsKey(windowName))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' is already open or opening.");
                UIWindow existingWindow = activeWindows[windowName];
                onUIWindowCreated?.Invoke(existingWindow);
                return existingWindow;
            }

            // Check if an opening operation is already in progress
            if (uiOpenTCS.TryGetValue(windowName, out var existingTcs))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Window '{windowName}' open operation already in progress. Awaiting existing task.");
                UIWindow window = await existingTcs.Task.AttachExternalCancellation(cancellationToken); // Wait for the existing operation
                onUIWindowCreated?.Invoke(window);
                return window;
            }

            var tcs = new UniTaskCompletionSource<UIWindow>();
            uiOpenTCS[windowName] = tcs;

            CLogger.LogInfo($"{DEBUG_FLAG} Attempting to open UI: {windowName}");
            string configPath = assetPathBuilder.GetAssetPath(windowName);
            if (string.IsNullOrEmpty(configPath))
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to get asset path for UI: {windowName}. Check AssetPathBuilder.");
                uiOpenTCS.Remove(windowName); // Clean up before setting exception
                tcs.TrySetException(new System.InvalidOperationException($"Asset path not found for {windowName}"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            UIWindowConfiguration windowConfig = null;
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (assetPackage == null)
                {
                    throw new System.InvalidOperationException("IAssetPackage is not available.");
                }

                AssetCacheEntry<UIWindowConfiguration> configEntry;
                if (!configHandleCache.TryGetValue(windowName, out configEntry))
                {
                    var windowConfigHandle = assetPackage.LoadAssetAsync<UIWindowConfiguration>(configPath);
                    while (!windowConfigHandle.IsDone)
                    {
                        await UniTask.Yield(cancellationToken);
                    }

                    if (!string.IsNullOrEmpty(windowConfigHandle.Error) || windowConfigHandle.Asset == null)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Failed to load UIWindowConfiguration at path: {configPath} for WindowName: {windowName}. Error: {windowConfigHandle.Error}");
                        windowConfigHandle.Dispose();
                        uiOpenTCS.Remove(windowName); // Clean up
                        tcs.TrySetException(new System.Exception($"Failed to load UIWindowConfiguration for {windowName}"));
                        onUIWindowCreated?.Invoke(null);
                        return null;
                    }

                    configEntry = new AssetCacheEntry<UIWindowConfiguration> { Handle = windowConfigHandle, RefCount = 0 };
                    configHandleCache[windowName] = configEntry;
                    configHandleLru.Add(windowName); // Add to end (MRU)

                    // Enforce Config Cache Limit
                    EnforceConfigCacheSize();
                }
                else
                {
                    TouchConfigCache(windowName);
                }

                // Increment RefCount
                configEntry.RefCount++;
                windowConfig = configEntry.Handle.Asset;

                if (windowConfig.Source == UIWindowConfiguration.PrefabSource.PrefabReference && windowConfig.WindowPrefab == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} WindowPrefab is null in WindowConfig for: {windowName}");
                    uiOpenTCS.Remove(windowName); // Clean up
                    // We need to decrement RefCount if we fail here
                    ReleaseConfigAsset(windowName);

                    tcs.TrySetException(new System.NullReferenceException($"WindowPrefab null for {windowName}"));
                    onUIWindowCreated?.Invoke(null);
                    return null;
                }
            }
            catch (System.OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Open UI operation for '{windowName}' was canceled.");
                uiOpenTCS.Remove(windowName);
                tcs.TrySetCanceled();
                onUIWindowCreated?.Invoke(null);
                return null;
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Exception while loading UIWindowConfiguration for {windowName}: {ex.Message}\n{ex.StackTrace}");
                uiOpenTCS.Remove(windowName);
                tcs.TrySetException(ex);
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            if (windowConfig.Layer == null || string.IsNullOrEmpty(windowConfig.Layer.LayerName))
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayerConfiguration or LayerName is not set in WindowConfig for: {windowName}");
                uiOpenTCS.Remove(windowName);
                tcs.TrySetException(new System.NullReferenceException($"LayerConfig null for {windowName}"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }
            string layerName = windowConfig.Layer.LayerName;
            UILayer uiLayer = uiRoot.GetUILayer(layerName);

            if (uiLayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayer not found: {layerName} (for window {windowName})");
                uiOpenTCS.Remove(windowName);
                tcs.TrySetException(new System.Exception($"UILayer '{layerName}' not found"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            // Redundant check if activeWindows check above is comprehensive, but good for safety.
            if (uiLayer.HasWindow(windowName)) // This check also needs to handle the TCS correctly
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' already exists in layer '{uiLayer.LayerName}'. Aborting duplicate open.");
                if (activeWindows.TryGetValue(windowName, out var existingWindowInstance))
                {
                    uiOpenTCS.Remove(windowName); // Remove the TCS for *this* duplicate open attempt
                    tcs.TrySetResult(existingWindowInstance);
                    onUIWindowCreated?.Invoke(existingWindowInstance);
                    return existingWindowInstance;
                }
                uiOpenTCS.Remove(windowName); // Remove the TCS for *this* duplicate open attempt
                tcs.TrySetException(new System.InvalidOperationException($"Window '{windowName}' exists in layer but not in UIManager's active list."));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            UIWindow uiWindowInstance = null;
            try
            {
                // Respect config source to avoid ambiguity
                if (windowConfig.Source == UIWindowConfiguration.PrefabSource.Location)
                {
                    if (string.IsNullOrEmpty(windowConfig.PrefabLocation) || assetPackage == null)
                    {
                        throw new System.InvalidOperationException("Prefab source is 'Location' but PrefabLocation or AssetPackage is not available.");
                    }
                    // Try cache first
                    AssetCacheEntry<GameObject> cacheEntry;
                    if (!prefabHandleCache.TryGetValue(windowConfig.PrefabLocation, out cacheEntry))
                    {
                        var prefabLoadHandle = assetPackage.LoadAssetAsync<GameObject>(windowConfig.PrefabLocation);
                        while (!prefabLoadHandle.IsDone)
                        {
                            await UniTask.Yield(cancellationToken);
                        }

                        if (!string.IsNullOrEmpty(prefabLoadHandle.Error) || prefabLoadHandle.Asset == null)
                        {
                            prefabLoadHandle.Dispose();
                            throw new System.Exception($"Failed to load UI prefab at '{windowConfig.PrefabLocation}': {prefabLoadHandle?.Error}");
                        }

                        cacheEntry = new AssetCacheEntry<GameObject> { Handle = prefabLoadHandle, RefCount = 0 };
                        prefabHandleCache[windowConfig.PrefabLocation] = cacheEntry;
                        prefabHandleLru.Add(windowConfig.PrefabLocation); // Add to end (MRU)

                        // Ensure cache constraints (eviction)
                        EnforcePrefabCacheSize();
                    }
                    else
                    {
                        // Refresh LRU
                        TouchPrefabCache(windowConfig.PrefabLocation);
                    }

                    // Increment RefCount for this usage
                    cacheEntry.RefCount++;

                    var go = cacheEntry.Handle.Asset;
                    // Spawn instance via spawner to allow pooling or custom instantiation
                    await ThrottleInstantiate(cancellationToken);
                    var spawnedGo = objectSpawner.Create(go);
                    uiWindowInstance = spawnedGo != null ? spawnedGo.GetComponent<UIWindow>() : null;

                    if (uiWindowInstance != null)
                    {
                        uiWindowInstance.SetSourceAssetPath(windowConfig.PrefabLocation);
                        uiWindowInstance.OnReleaseAssetReference = ReleaseWindowAsset;
                    }
                }
                else // PrefabReference
                {
                    await ThrottleInstantiate(cancellationToken);
                    uiWindowInstance = objectSpawner.Create(windowConfig.WindowPrefab) as UIWindow;
                }

                if (uiWindowInstance == null)
                {
                    throw new System.NullReferenceException($"Spawned GameObject for {windowName} does not have a UIWindow component.");
                }

                // Apply transition driver if provided
                if (transitionDriver != null)
                {
                    uiWindowInstance.SetTransitionDriver(transitionDriver);
                }
            }
            catch (System.OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Open UI operation for '{windowName}' was canceled during instantiation.");
                uiOpenTCS.Remove(windowName);
                tcs.TrySetCanceled();
                onUIWindowCreated?.Invoke(null);
                return null;
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to instantiate UIWindow prefab for {windowName}: {ex.Message}\n{ex.StackTrace}");
                uiOpenTCS.Remove(windowName); // Clean up on instantiation failure
                tcs.TrySetException(ex);
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            uiWindowInstance.SetWindowName(windowName);
            uiLayer.AddWindow(uiWindowInstance);
            activeWindows[windowName] = uiWindowInstance;

            // Yield once before opening to spread work across frames if needed
            await UniTask.Yield(cancellationToken);
            // The Open method itself might not be cancellable without modification,
            // but the preceding heavy operations (loading, instantiation) are now cancellable.
            await uiWindowInstance.Open();

            onUIWindowCreated?.Invoke(uiWindowInstance);
            tcs.TrySetResult(uiWindowInstance);
            uiOpenTCS.Remove(windowName);
            return uiWindowInstance;
        }

        internal UniTask<UIWindow> OpenUIAndWait(string windowName, System.Threading.CancellationToken cancellationToken = default)
        {
            return OpenUIAsync(windowName, null, cancellationToken);
        }

        internal async UniTask CloseUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} WindowName cannot be null or empty for CloseUI.");
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If an open operation is still in progress for this window, wait for it to complete.
                if (uiOpenTCS.TryGetValue(windowName, out var openTcs))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Close requested for '{windowName}' which is still opening. Awaiting open completion.");
                    await openTcs.Task.AttachExternalCancellation(cancellationToken); // Wait for opening to finish
                }

                if (!activeWindows.TryGetValue(windowName, out UIWindow windowToClose))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Window '{windowName}' not found in active windows. Skipping close (may not have been opened).");
                    return;
                }

                // Check if window is already closing or closed to prevent duplicate close operations
                if (windowToClose == null || windowToClose?.gameObject == null)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' is null or destroyed. Cannot close.");
                    activeWindows.Remove(windowName);
                    return;
                }

                CLogger.LogInfo($"{DEBUG_FLAG} Attempting to close UI: {windowName}");
                UILayer layer = windowToClose.ParentLayer;

                if (layer != null)
                {
                    layer.RemoveWindow(windowName);
                }
                else
                {
                    // Window is active but has no parent layer (should be rare if managed correctly)
                    CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' has no parent layer but is active. Attempting direct close.");
                    windowToClose.Close();
                }

                activeWindows.Remove(windowName);
                uiOpenTCS.Remove(windowName);

                // Release the configuration asset loaded for this window
                ReleaseConfigAsset(windowName);
            }
            catch (System.OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Close UI operation for '{windowName}' was canceled.");
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Exception during CloseUIAsync for '{windowName}': {ex.Message}\n{ex.StackTrace}");
            }
        }

        /// <summary>
        /// Checks if a UI window is currently considered valid and active.
        /// </summary>
        public bool IsUIWindowValid(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return false;
            if (activeWindows.TryGetValue(windowName, out UIWindow window))
            {
                // Valid if it exists, its GameObject is not null, and it's active in hierarchy.
                // You might have other criteria for "valid" (e.g., in 'Opened' state).
                return window != null && window.gameObject != null && window.gameObject.activeInHierarchy;
            }
            return false;
        }

        /// <summary>
        /// Gets an active UI window instance by its name.
        /// Returns null if not found or not active.
        /// </summary>
        public UIWindow GetUIWindow(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return null;
            activeWindows.TryGetValue(windowName, out UIWindow window);
            return window; // Returns null if not found
        }

        public void AddUICameraToMainCameraStack()
        {
            var root = TryGetUIRoot();
            if (root != null && root.UICamera != null && mainCameraService != null)
            {
                mainCameraService.AddCameraToStack(root.UICamera, 0); // Specify position if needed
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Cannot add UI Camera to stack: UIRoot, UICamera, or MainCameraService is missing.");
            }
        }

        public void RemoveUICameraFromMainCameraStack()
        {
            var root = TryGetUIRoot();
            if (root != null && root.UICamera != null && mainCameraService != null)
            {
                mainCameraService.RemoveCameraFromStack(root.UICamera);
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Cannot remove UI Camera from stack: UIRoot, UICamera, or MainCameraService is missing.");
            }
        }

        public (float, float) GetRootCanvasSize()
        {
            return TryGetUIRoot()?.GetRootCanvasSize() ?? default;
        }

        protected void OnDestroy()
        {
            UnityEngine.Application.onBeforeRender -= ResetPerFrameBudget;
            // Dispose cached prefab handles
            if (prefabHandleCache != null)
            {
                foreach (var kv in prefabHandleCache)
                {
                    kv.Value.Handle?.Dispose();
                }
                prefabHandleCache.Clear();
                prefabHandleLru.Clear();
            }
            // Clean up any remaining handles if the UIManager itself is destroyed.
            // This is a fallback; ideally, handles are released when windows are closed.
            if (configHandleCache != null)
            {
                foreach (var kv in configHandleCache)
                {
                    kv.Value.Handle?.Dispose();
                }
                configHandleCache.Clear();
                configHandleLru.Clear();
            }

            // Clear other collections
            activeWindows.Clear();
            uiOpenTCS.Clear();

            CLogger.LogInfo($"{DEBUG_FLAG} UIManager is being destroyed.");
        }

        public void ReleaseWindowAsset(string assetPath)
        {
            if (string.IsNullOrEmpty(assetPath)) return;

            if (prefabHandleCache.TryGetValue(assetPath, out var entry))
            {
                entry.RefCount--;
                if (entry.RefCount < 0)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} RefCount for {assetPath} dropped below zero. Check logic.");
                    entry.RefCount = 0;
                }
                // We don't dispose immediately. LRU EnforcePrefabCacheSize handles cleanup when cache fills.
            }
        }

        private void ReleaseConfigAsset(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return;

            if (configHandleCache.TryGetValue(windowName, out var entry))
            {
                entry.RefCount--;
                if (entry.RefCount < 0)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Config RefCount for {windowName} dropped below zero.");
                    entry.RefCount = 0;
                }
            }
        }

        private void TouchPrefabCache(string key)
        {
            // Move accessed key to end (MRU)
            if (prefabHandleLru.Remove(key))
            {
                prefabHandleLru.Add(key);
            }
        }

        private void TouchConfigCache(string key)
        {
            if (configHandleLru.Remove(key))
            {
                configHandleLru.Add(key);
            }
        }

        private void EnforcePrefabCacheSize()
        {
            // Try to reduce size to Max by evicting unused items (RefCount == 0) from the front (LRU)
            while (prefabHandleCache.Count > PrefabHandleCacheMax)
            {
                bool evictedAny = false;

                for (int i = 0; i < prefabHandleLru.Count; i++)
                {
                    string key = prefabHandleLru[i];
                    if (prefabHandleCache.TryGetValue(key, out var entry))
                    {
                        if (entry.RefCount <= 0)
                        {
                            // Safe to evict
                            entry.Handle?.Dispose();
                            prefabHandleCache.Remove(key);
                            prefabHandleLru.RemoveAt(i);
                            evictedAny = true;
                            break;
                        }
                    }
                    else
                    {
                        prefabHandleLru.RemoveAt(i);
                        evictedAny = true;
                        break;
                    }
                }

                if (!evictedAny) break;
            }
        }

        private void EnforceConfigCacheSize()
        {
            while (configHandleCache.Count > ConfigHandleCacheMax)
            {
                bool evictedAny = false;

                for (int i = 0; i < configHandleLru.Count; i++)
                {
                    string key = configHandleLru[i];
                    if (configHandleCache.TryGetValue(key, out var entry))
                    {
                        if (entry.RefCount <= 0)
                        {
                            entry.Handle?.Dispose();
                            configHandleCache.Remove(key);
                            configHandleLru.RemoveAt(i);
                            evictedAny = true;
                            break;
                        }
                    }
                    else
                    {
                        configHandleLru.RemoveAt(i);
                        evictedAny = true;
                        break;
                    }
                }

                if (!evictedAny) break;
            }
        }

        private async UniTask ThrottleInstantiate(System.Threading.CancellationToken cancellationToken = default)
        {
            while (instantiatesThisFrame >= maxInstantiatesPerFrame)
            {
                await UniTask.Yield(cancellationToken);
            }
            instantiatesThisFrame++;
        }
    }
}