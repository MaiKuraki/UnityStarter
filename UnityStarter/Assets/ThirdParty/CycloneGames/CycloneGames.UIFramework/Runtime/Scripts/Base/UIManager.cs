using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logger;  // Assuming CLogger is your custom logger
using CycloneGames.Service; // For IAssetPathBuilderFactory, IMainCameraService
using CycloneGames.Factory; // For IUnityObjectSpawner
using UnityEngine.AddressableAssets;
using UnityEngine.ResourceManagement.AsyncOperations; // For AsyncOperationHandle

namespace CycloneGames.UIFramework // Added namespace
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        private IAssetPathBuilder assetPathBuilder;
        private IUnityObjectSpawner objectSpawner; // Should be IObjectSpawner<UnityEngine.Object> or similar
        private IMainCameraService mainCameraService; // Renamed for clarity
        private UIRoot uiRoot;

        // Tracks ongoing opening operations to prevent duplicate concurrent opens
        // and to allow CloseUI to wait for opening to complete.
        private Dictionary<string, UniTaskCompletionSource<UIWindow>> uiOpenTCS = new Dictionary<string, UniTaskCompletionSource<UIWindow>>();

        // Tracks active windows for quick access and management
        private Dictionary<string, UIWindow> activeWindows = new Dictionary<string, UIWindow>();
        // Tracks loaded configurations if they need to be released explicitly and are not bound to GameObject lifetime
        private Dictionary<string, AsyncOperationHandle<UIWindowConfiguration>> loadedConfigHandles = new Dictionary<string, AsyncOperationHandle<UIWindowConfiguration>>();


        /// <summary>
        /// Initializes the UIManager with necessary services.
        /// </summary>
        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IUnityObjectSpawner spawner, IMainCameraService cameraService)
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

        private void Awake()
        {
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

        private async UniTask<UIWindow> OpenUIAsync(string windowName, System.Action<UIWindow> onUIWindowCreated = null)
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

            AsyncOperationHandle<UIWindowConfiguration> windowConfigHandle = default;
            UIWindowConfiguration windowConfig = null;
            try
            {
                windowConfigHandle = Addressables.LoadAssetAsync<UIWindowConfiguration>(configPath);
                await windowConfigHandle.Task;

                if (windowConfigHandle.Status != AsyncOperationStatus.Succeeded || windowConfigHandle.Result == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load UIWindowConfiguration at path: {configPath} for WindowName: {windowName}. Status: {windowConfigHandle.Status}");
                    if (windowConfigHandle.IsValid()) Addressables.Release(windowConfigHandle);
                    uiOpenTCS.Remove(windowName); // Clean up
                    tcs.TrySetException(new System.Exception($"Failed to load UIWindowConfiguration for {windowName}"));
                    onUIWindowCreated?.Invoke(null);
                    return null;
                }
                windowConfig = windowConfigHandle.Result;
                loadedConfigHandles[windowName] = windowConfigHandle;

                if (windowConfig.WindowPrefab == null)
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
            catch (System.Exception ex) // Catches exceptions from Addressables.LoadAssetAsync or await
            {
                CLogger.LogError($"{DEBUG_FLAG} Exception while loading UIWindowConfiguration for {windowName}: {ex.Message}\n{ex.StackTrace}");
                if (windowConfigHandle.IsValid()) Addressables.Release(windowConfigHandle);
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
                uiWindowInstance = objectSpawner.Create(windowConfig.WindowPrefab) as UIWindow;
                if (uiWindowInstance == null)
                {
                    throw new System.NullReferenceException($"Spawned GameObject for {windowName} does not have a UIWindow component.");
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

            onUIWindowCreated?.Invoke(uiWindowInstance);
            tcs.TrySetResult(uiWindowInstance); // Resolve the task for this open operation
            uiOpenTCS.Remove(windowName);
            return uiWindowInstance;
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
                if (configHandle.IsValid())
                {
                    Addressables.Release(configHandle);
                }
                loadedConfigHandles.Remove(windowName);
                CLogger.LogInfo($"{DEBUG_FLAG} Released UIWindowConfiguration for {windowName}.");
            }
            // as Addressables would release when the GameObject is destroyed. Double-check Addressables best practices.
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
            // Clean up any remaining Addressable handles if the UIManager itself is destroyed.
            // This is a fallback; ideally, handles are released when windows are closed.
            foreach (var handleEntry in loadedConfigHandles)
            {
                if (handleEntry.Value.IsValid())
                {
                    Addressables.Release(handleEntry.Value);
                    CLogger.LogInfo($"{DEBUG_FLAG} Releasing config for {handleEntry.Key} during UIManager.OnDestroy.");
                }
            }
            loadedConfigHandles.Clear();

            // Clear other collections
            activeWindows.Clear();
            uiOpenTCS.Clear();

            CLogger.LogInfo($"{DEBUG_FLAG} UIManager is being destroyed.");
        }
    }
}