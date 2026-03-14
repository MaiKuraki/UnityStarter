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
        // Direct handle ownership: UIManager holds exactly one IAssetHandle<T> per asset.
        // Lifecycle is fully delegated to AssetCacheService (W-TinyLFU + RefCount).
        // Calling handle.Dispose() decrements the AssetCacheService RefCount, allowing
        // idle assets to be promoted to the trial/main pool and eventually evicted.
        private readonly Dictionary<string, IAssetHandle<UIWindowConfiguration>> _configHandles = new Dictionary<string, IAssetHandle<UIWindowConfiguration>>(16);
        // Prefab handles: keyed by PrefabLocation, shared across windows showing the same prefab.
        private readonly Dictionary<string, IAssetHandle<GameObject>> _prefabHandles = new Dictionary<string, IAssetHandle<GameObject>>(16);
        // Track per-window prefab location so we can release when the window closes.
        private readonly Dictionary<string, string> _windowToPrefabLocation = new Dictionary<string, string>(16);

        // Throttling instantiate per frame
        private int maxInstantiatesPerFrame = 2;
        private int instantiatesThisFrame = 0;

        // Tracks ongoing opening operations to prevent duplicate concurrent opens
        // and to allow CloseUI to wait for opening to complete.
        private Dictionary<string, UniTaskCompletionSource<UIWindow>> uiOpenTCS = new Dictionary<string, UniTaskCompletionSource<UIWindow>>();

        // Tracks active windows for quick access and management
        private Dictionary<string, UIWindow> activeWindows = new Dictionary<string, UIWindow>();

        // Window binders for custom initialization/MVP decoupling
        private List<IUIWindowBinder> windowBinders = new List<IUIWindowBinder>(4);
        private IUIWindowBinder[] _windowBindersCache = null;

        // Optional navigation service — set via SetNavigationService()
        private IUINavigationService _navigationService;


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

        /// <summary>
        /// Registers a window binder for global integration hooks (like MVP Presenter creation).
        /// </summary>
        public void RegisterWindowBinder(IUIWindowBinder binder)
        {
            if (binder != null && !windowBinders.Contains(binder))
            {
                windowBinders.Add(binder);
                _windowBindersCache = windowBinders.ToArray();
            }
        }

        /// <summary>
        /// Unregisters a window binder.
        /// </summary>
        public void UnregisterWindowBinder(IUIWindowBinder binder)
        {
            if (binder != null)
            {
                windowBinders.Remove(binder);
                _windowBindersCache = windowBinders.ToArray();
            }
        }

        /// <summary>
        /// Attaches an IUINavigationService that will be automatically notified whenever
        /// a window opens or closes. Pass null to detach.
        /// </summary>
        public void SetNavigationService(IUINavigationService nav)
        {
            _navigationService = nav;
        }

        // Optional coordinator: when set, NavigateToAsync() uses it to fire both
        // window animations simultaneously instead of sequentially.
        private IUITransitionCoordinator _transitionCoordinator;

        public void SetTransitionCoordinator(IUITransitionCoordinator coordinator)
        {
            _transitionCoordinator = coordinator;
        }

        public IUITransitionCoordinator TransitionCoordinator => _transitionCoordinator;

        private void CleanupAllWindows()
        {
            CLogger.LogInfo($"{DEBUG_FLAG} Cleaning up all active windows due to scene unload.");

            foreach (var kv in _configHandles) kv.Value?.Dispose();
            _configHandles.Clear();

            foreach (var kv in _prefabHandles) kv.Value?.Dispose();
            _prefabHandles.Clear();
            _windowToPrefabLocation.Clear();

            activeWindows.Clear();
            _navigationService?.Clear();

            foreach (var kv in uiOpenTCS) kv.Value.TrySetCanceled();
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

        internal async UniTask<UIWindow> OpenUIAsync(string windowName, System.Action<UIWindow> onUIWindowCreated = null, System.Threading.CancellationToken cancellationToken = default, bool silentOpen = false)
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

                if (!_configHandles.TryGetValue(windowName, out var configHandle))
                {
                    // Cache miss: load from package, tagged under 'UIFramework' bucket.
                    configHandle = assetPackage.LoadAssetAsync<UIWindowConfiguration>(configPath, "UIFramework", cancellationToken: cancellationToken);
                    await configHandle.Task;

                    if (!string.IsNullOrEmpty(configHandle.Error) || configHandle.Asset == null)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Failed to load UIWindowConfiguration at path: {configPath} for WindowName: {windowName}. Error: {configHandle.Error}");
                        configHandle.Dispose();
                        uiOpenTCS.Remove(windowName);
                        tcs.TrySetException(new System.Exception($"Failed to load UIWindowConfiguration for {windowName}"));
                        onUIWindowCreated?.Invoke(null);
                        return null;
                    }

                    // UIManager owns exactly one handle reference; AssetCacheService RefCount = 1.
                    _configHandles[windowName] = configHandle;
                }

                windowConfig = configHandle.Asset;

                if (windowConfig.Source == UIWindowConfiguration.PrefabSource.PrefabReference && windowConfig.WindowPrefab == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} WindowPrefab is null in WindowConfig for: {windowName}");
                    uiOpenTCS.Remove(windowName);
                    ReleaseConfigHandle(windowName);
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
                    if (!_prefabHandles.TryGetValue(windowConfig.PrefabLocation, out var prefabHandle))
                    {
                        prefabHandle = assetPackage.LoadAssetAsync<GameObject>(windowConfig.PrefabLocation, "UIFramework", cancellationToken: cancellationToken);
                        await prefabHandle.Task;

                        if (!string.IsNullOrEmpty(prefabHandle.Error) || prefabHandle.Asset == null)
                        {
                            prefabHandle.Dispose();
                            throw new System.Exception($"Failed to load UI prefab at '{windowConfig.PrefabLocation}': {prefabHandle?.Error}");
                        }

                        _prefabHandles[windowConfig.PrefabLocation] = prefabHandle;
                    }

                    var go = prefabHandle.Asset;
                    await ThrottleInstantiate(cancellationToken);
                    var spawnedGo = objectSpawner.Create(go);
                    uiWindowInstance = spawnedGo != null ? spawnedGo.GetComponent<UIWindow>() : null;

                    if (uiWindowInstance != null)
                    {
                        // Track which prefab this window uses so we can release on close.
                        _windowToPrefabLocation[windowName] = windowConfig.PrefabLocation;
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
            uiWindowInstance.SetConfiguration(windowConfig);
            uiWindowInstance._binders = _windowBindersCache;
            uiLayer.AddWindow(uiWindowInstance);
            activeWindows[windowName] = uiWindowInstance;
            _navigationService?.Register(windowName);

            // Trigger window binders (e.g., MVP integration) before open
            for (int i = 0; i < windowBinders.Count; i++)
            {
                try
                {
                    windowBinders[i].OnWindowCreated(uiWindowInstance);
                }
                catch (System.Exception ex)
                {
                    CLogger.LogError($"{DEBUG_FLAG} WindowBinder {windowBinders[i].GetType().Name} failed during OnWindowCreated for {windowName}: {ex.Message}");
                }
            }

            // Yield once before opening to allow binders to settle
            await UniTask.Yield(cancellationToken);

            if (silentOpen)
                await uiWindowInstance.OpenSilentAsync(cancellationToken);
            else
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

        // Loads, instantiates, and initialises the window (including binders/MVP), but
        // calls OpenSilentAsync instead of Open() so no entry animation plays yet.
        // The window is ready for the coordinator to animate both sides simultaneously.
        internal async UniTask<UIWindow> OpenUIReadyAsync(string windowName, System.Threading.CancellationToken ct = default)
        {
            UIWindow window = await OpenUIAsync(windowName, null, ct, silentOpen: true);
            return window;
        }

        // Coordinates a simultaneous transition: loads 'toWindow' silently, fires both
        // CloseAsync (leaving) and the coordinator's TransitionAsync (entering) at the same time,
        // then tears down the leaving window after both complete.
        internal async UniTask CoordinatedNavigateAsync(
            string fromName, string toName,
            NavigationDirection direction,
            IUITransitionCoordinator coordinator,
            System.Threading.CancellationToken ct = default)
        {
            if (coordinator == null)
            {
                OpenUI(toName);
                return;
            }

            activeWindows.TryGetValue(fromName, out UIWindow leaving);

            // Load and initialise the entering window without playing its animation
            UIWindow entering = await OpenUIReadyAsync(toName, ct);
            if (ct.IsCancellationRequested || entering == null) return;

            // Fire both animations simultaneously
            await UniTask.WhenAll(
                coordinator.TransitionAsync(leaving, entering, direction, ct),
                leaving != null ? leaving.CloseAsync(ct) : UniTask.CompletedTask
            );

            // Cleanup: leaving window was animated out — remove it if still tracked
            if (leaving != null && activeWindows.ContainsKey(fromName))
            {
                _navigationService?.Unregister(fromName);
                activeWindows.Remove(fromName);
                uiOpenTCS.Remove(fromName);
                ReleaseConfigHandle(fromName);
                ReleaseWindowAsset(fromName);
            }
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

                // Trigger window binders for destruction cleanup
                for (int i = 0; i < windowBinders.Count; i++)
                {
                    try
                    {
                        windowBinders[i].OnWindowDestroying(windowToClose);
                    }
                    catch (System.Exception ex)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} WindowBinder {windowBinders[i].GetType().Name} failed during OnWindowDestroying for {windowName}: {ex.Message}");
                    }
                }

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
                _navigationService?.Unregister(windowName);
                uiOpenTCS.Remove(windowName);

                ReleaseConfigHandle(windowName);
                ReleaseWindowAsset(windowName);
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

        public Camera GetUICamera()
        {
            return TryGetUIRoot()?.UICamera;
        }

        protected void OnDestroy()
        {
            UnityEngine.Application.onBeforeRender -= ResetPerFrameBudget;

            foreach (var kv in _configHandles) kv.Value?.Dispose();
            _configHandles.Clear();

            foreach (var kv in _prefabHandles) kv.Value?.Dispose();
            _prefabHandles.Clear();
            _windowToPrefabLocation.Clear();

            activeWindows.Clear();
            uiOpenTCS.Clear();

            CLogger.LogInfo($"{DEBUG_FLAG} UIManager is being destroyed.");
        }

        // Called by UIWindow.OnFinishedClose via the OnReleaseAssetReference callback.
        // Releases the prefab handle when no window is using this prefab location anymore.
        public void ReleaseWindowAsset(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return;

            if (!_windowToPrefabLocation.TryGetValue(windowName, out var prefabLocation)) return;
            _windowToPrefabLocation.Remove(windowName);

            // Check if any other open window shares the same prefab before disposing.
            bool stillInUse = false;
            foreach (var kv in _windowToPrefabLocation)
            {
                if (kv.Value == prefabLocation) { stillInUse = true; break; }
            }

            if (!stillInUse && _prefabHandles.TryGetValue(prefabLocation, out var handle))
            {
                handle.Dispose(); // RefCount → 0 in AssetCacheService → enters idle pool.
                _prefabHandles.Remove(prefabLocation);
            }
        }

        private void ReleaseConfigHandle(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return;
            if (_configHandles.TryGetValue(windowName, out var handle))
            {
                handle.Dispose();
                _configHandles.Remove(windowName);
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