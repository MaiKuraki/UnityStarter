using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logger; 
using CycloneGames.Service; // For IMainCameraService
using CycloneGames.AssetManagement; // For IAssetPathBuilderFactory
using CycloneGames.Factory.Runtime; // For IUnityObjectSpawner
using CycloneGames.AssetManagement.Integrations.Common;

namespace CycloneGames.UIFramework
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        private IAssetPathBuilder assetPathBuilder;
        private IUnityObjectSpawner objectSpawner; // Should be IObjectSpawner<UnityEngine.Object> or similar
        private IMainCameraService mainCameraService; // Renamed for clarity
        private IAssetPackage assetPackage; // Generic asset package for loading configs/prefabs
        private IUIWindowTransitionDriver transitionDriver; // Optional transition driver applied to spawned windows
        private UIRoot uiRoot;
        // Optional: retain a small cache of prefab handles to reduce repeated IO (LRU)
        private readonly System.Collections.Generic.Dictionary<string, IAssetHandle<GameObject>> prefabHandleCache = new System.Collections.Generic.Dictionary<string, IAssetHandle<GameObject>>(16);
        private readonly System.Collections.Generic.LinkedList<string> prefabHandleLru = new System.Collections.Generic.LinkedList<string>();
        private const int PrefabHandleCacheMax = 16;

        // Throttling instantiate per frame
        private int maxInstantiatesPerFrame = 2;
        private int instantiatesThisFrame = 0;

        // Tracks ongoing opening operations to prevent duplicate concurrent opens
        // and to allow CloseUI to wait for opening to complete.
        private Dictionary<string, UniTaskCompletionSource<UIWindow>> uiOpenTCS = new Dictionary<string, UniTaskCompletionSource<UIWindow>>();

        // Tracks active windows for quick access and management
        private Dictionary<string, UIWindow> activeWindows = new Dictionary<string, UIWindow>();
        // Tracks loaded configurations if they need explicit release (via handle disposal)
        private Dictionary<string, IAssetHandle<UIWindowConfiguration>> loadedConfigHandles = new Dictionary<string, IAssetHandle<UIWindowConfiguration>>();


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

            // Find UIRoot. This assumes UIRoot is already in the scene.
            // If UIRoot could be instantiated by UIManager, that logic would be here.
            uiRoot = GameObject.FindFirstObjectByType<UIRoot>();
            if (uiRoot == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UIRoot not found in the scene. UIManager requires a UIRoot to function.");
            }
            else
            {
                // Initial camera setup if UIRoot and mainCameraService are available
                AddUICameraToMainCameraStack();
            }
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
            if (uiRoot == null)
            {
                uiRoot = GameObject.FindFirstObjectByType<UIRoot>();
                if (uiRoot == null)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} UIRoot not found in Awake. Ensure it exists or Initialize is called with a valid scene setup.");
                }
            }
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

        internal async UniTask<UIWindow> OpenUIAsync(string windowName, System.Action<UIWindow> onUIWindowCreated = null)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} WindowName cannot be null or empty.");
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            // Check if already active
            if (activeWindows.ContainsKey(windowName))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' is already open or opening.");
                // Optionally, could bring to front or return existing instance
                UIWindow existingWindow = activeWindows[windowName];
                onUIWindowCreated?.Invoke(existingWindow); // Notify with existing
                return existingWindow;
            }

            // Check if an opening operation is already in progress
            if (uiOpenTCS.TryGetValue(windowName, out var existingTcs))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Window '{windowName}' open operation already in progress. Awaiting existing task.");
                UIWindow window = await existingTcs.Task; // Wait for the existing operation
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
                if (assetPackage == null)
                {
                    throw new System.InvalidOperationException("IAssetPackage is not available.");
                }

                var windowConfigHandle = assetPackage.LoadAssetAsync<UIWindowConfiguration>(configPath);
                while (!windowConfigHandle.IsDone)
                {
                    await UniTask.Yield();
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
                windowConfig = windowConfigHandle.Asset;
                loadedConfigHandles[windowName] = windowConfigHandle;

                if (windowConfig.Source == UIWindowConfiguration.PrefabSource.PrefabReference && windowConfig.WindowPrefab == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} WindowPrefab is null in WindowConfig for: {windowName}");
                    uiOpenTCS.Remove(windowName); // Clean up
                                                  // No need to release windowConfigHandle here if it's stored in loadedConfigHandles, 
                                                  // CloseUI or OnDestroy will handle it.
                    tcs.TrySetException(new System.NullReferenceException($"WindowPrefab null for {windowName}"));
                    onUIWindowCreated?.Invoke(null);
                    return null;
                }
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
                    tcs.TrySetResult(existingWindowInstance); // Resolve with the existing instance
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
                    IAssetHandle<GameObject> prefabHandle;
                    if (!prefabHandleCache.TryGetValue(windowConfig.PrefabLocation, out prefabHandle) || prefabHandle == null)
                    {
                        prefabHandle = assetPackage.LoadAssetAsync<GameObject>(windowConfig.PrefabLocation);
                        while (!prefabHandle.IsDone)
                        {
                            await UniTask.Yield();
                        }
                        if (!string.IsNullOrEmpty(prefabHandle.Error) || prefabHandle.Asset == null)
                        {
                            prefabHandle?.Dispose();
                            throw new System.Exception($"Failed to load UI prefab at '{windowConfig.PrefabLocation}': {prefabHandle?.Error}");
                        }
                        // LRU add
                        TouchCache(windowConfig.PrefabLocation, prefabHandle);
                    }
                    else
                    {
                        // LRU touch
                        TouchCache(windowConfig.PrefabLocation, prefabHandle);
                    }
                    var go = prefabHandle.Asset;
                    // Spawn instance via spawner to allow pooling or custom instantiation
                    await ThrottleInstantiate();
                    var spawnedGo = objectSpawner.Create(go);
                    uiWindowInstance = spawnedGo != null ? spawnedGo.GetComponent<UIWindow>() : null;
                    // Keep handle cached for subsequent opens (avoid immediate dispose)
                }
                else // PrefabReference
                {
                    await ThrottleInstantiate();
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
            await UniTask.Yield();
            await uiWindowInstance.Open();

            onUIWindowCreated?.Invoke(uiWindowInstance);
            tcs.TrySetResult(uiWindowInstance); // Resolve the task for this open operation
            uiOpenTCS.Remove(windowName);
            return uiWindowInstance;
        }

        public UniTask<UIWindow> OpenUIAndWait(string windowName)
        {
            return OpenUIAsync(windowName, null);
        }

        private async UniTask CloseUIAsync(string windowName)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} WindowName cannot be null or empty for CloseUI.");
                return;
            }

            // If an open operation is still in progress for this window, wait for it to complete.
            if (uiOpenTCS.TryGetValue(windowName, out var openTcs))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Close requested for '{windowName}' which is still opening. Awaiting open completion.");
                await openTcs.Task; // Wait for opening to finish
                // Do not remove from uiOpenTCS here, the OpenUIAsync will resolve it.
                // Or, if Close is called *after* Open resolves but before Open removes its TCS,
                // it might be okay. Let's assume OpenUIAsync's TCS is for the *completion* of opening.
            }

            if (!activeWindows.TryGetValue(windowName, out UIWindow windowToClose))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' not found in active windows. Cannot close.");
                return;
            }

            CLogger.LogInfo($"{DEBUG_FLAG} Attempting to close UI: {windowName}");
            UILayer layer = windowToClose.ParentLayer; // Get layer directly from window

            if (layer != null)
            {
                layer.RemoveWindow(windowName); // This tells the window to initiate its Close() sequence
            }
            else
            {
                // Window is active but has no parent layer (should be rare if managed correctly)
                CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' has no parent layer but is active. Attempting direct close.");
                windowToClose.Close(); // Tell it to close itself
            }

            // Remove from active tracking. The window's OnDestroy will handle UILayer's internal list.
            activeWindows.Remove(windowName);
            uiOpenTCS.Remove(windowName); // Clean up any residual open task completer for this window name

            // Release the configuration asset loaded for this window
            if (loadedConfigHandles.TryGetValue(windowName, out var configHandle))
            {
                configHandle?.Dispose();
                loadedConfigHandles.Remove(windowName);
                CLogger.LogInfo($"{DEBUG_FLAG} Released UIWindowConfiguration for {windowName}.");
            }
            // Handles are disposed explicitly; prefab instances follow normal GameObject lifecycle.
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
            if (uiRoot != null && uiRoot.UICamera != null && mainCameraService != null)
            {
                mainCameraService.AddCameraToStack(uiRoot.UICamera, 0); // Specify position if needed
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Cannot add UI Camera to stack: UIRoot, UICamera, or MainCameraService is missing.");
            }
        }

        public void RemoveUICameraFromMainCameraStack()
        {
            if (uiRoot != null && uiRoot.UICamera != null && mainCameraService != null)
            {
                mainCameraService.RemoveCameraFromStack(uiRoot.UICamera);
            }
            else
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Cannot remove UI Camera from stack: UIRoot, UICamera, or MainCameraService is missing.");
            }
        }

        public (float, float) GetRootCanvasSize()
        {
            return uiRoot.GetRootCanvasSize();
        }

        protected void OnDestroy()
        {
            UnityEngine.Application.onBeforeRender -= ResetPerFrameBudget;
            // Dispose cached prefab handles
            if (prefabHandleCache != null)
            {
                foreach (var kv in prefabHandleCache)
                {
                    kv.Value?.Dispose();
                }
                prefabHandleCache.Clear();
                prefabHandleLru.Clear();
            }
            // Clean up any remaining handles if the UIManager itself is destroyed.
            // This is a fallback; ideally, handles are released when windows are closed.
            foreach (var handleEntry in loadedConfigHandles)
            {
                handleEntry.Value?.Dispose();
                CLogger.LogInfo($"{DEBUG_FLAG} Releasing config for {handleEntry.Key} during UIManager.OnDestroy.");
            }
            loadedConfigHandles.Clear();

            // Clear other collections
            activeWindows.Clear();
            uiOpenTCS.Clear();

            CLogger.LogInfo($"{DEBUG_FLAG} UIManager is being destroyed.");
        }

        private void TouchCache(string key, IAssetHandle<GameObject> handle)
        {
            if (prefabHandleCache.ContainsKey(key))
            {
                // move to tail
                var node = prefabHandleLru.Find(key);
                if (node != null)
                {
                    prefabHandleLru.Remove(node);
                    prefabHandleLru.AddLast(node);
                }
                else
                {
                    prefabHandleLru.AddLast(key);
                }
                prefabHandleCache[key] = handle; // refresh
            }
            else
            {
                if (prefabHandleCache.Count >= PrefabHandleCacheMax)
                {
                    var oldest = prefabHandleLru.First != null ? prefabHandleLru.First.Value : null;
                    if (oldest != null && prefabHandleCache.TryGetValue(oldest, out var oldHandle))
                    {
                        oldHandle?.Dispose();
                        prefabHandleCache.Remove(oldest);
                    }
                    if (prefabHandleLru.First != null) prefabHandleLru.RemoveFirst();
                }
                prefabHandleCache[key] = handle;
                prefabHandleLru.AddLast(key);
            }
        }

        private async UniTask ThrottleInstantiate()
        {
            while (instantiatesThisFrame >= maxInstantiatesPerFrame)
            {
                await UniTask.Yield();
                // Budget resets in onBeforeRender; to be safe, also reset if new frame detected by Time.frameCount changes
                instantiatesThisFrame = 0;
            }
            instantiatesThisFrame++;
        }
    }
}