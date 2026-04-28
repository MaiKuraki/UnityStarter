using System.Collections.Generic;
using System.Threading;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;
using CycloneGames.Logger;
using CycloneGames.Service.Runtime;         // For IMainCameraService
using CycloneGames.Factory.Runtime;         // For IUnityObjectSpawner
using CycloneGames.AssetManagement.Runtime; // For IAssetPathBuilderFactory

namespace CycloneGames.UIFramework.Runtime
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        [SerializeField] private bool enableAssetLifecycleDebugLog = false;
        private IAssetPathBuilder assetPathBuilder;
        private IUnityObjectSpawner objectSpawner;
        private IMainCameraService mainCameraService;
        private IAssetPackage assetPackage;
        private IUIWindowTransitionDriver transitionDriver;
        private UIRoot uiRoot;

        // Direct handle ownership: UIManager holds exactly one IAssetHandle<T> per asset.
        // Lifecycle is fully delegated to AssetCacheService (W-TinyLFU + RefCount).
        private readonly Dictionary<string, IAssetHandle<UIWindowConfiguration>> _configHandles = new Dictionary<string, IAssetHandle<UIWindowConfiguration>>(16);
        // Prefab handles: keyed by PrefabLocation, shared across windows showing the same prefab.
        private readonly Dictionary<string, IAssetHandle<GameObject>> _prefabHandles = new Dictionary<string, IAssetHandle<GameObject>>(16);
        // Track per-window prefab location so we can release when the window closes.
        private readonly Dictionary<string, string> _windowToPrefabLocation = new Dictionary<string, string>(16);
        // Reusable scratch list for release scans to avoid per-call allocations.
        private readonly List<string> _releaseScratchWindowNames = new List<string>(8);
        // Low-allocation tracking of windows that should auto-close after a scene switch.
        private readonly List<UIWindow> _sceneBoundWindows = new List<UIWindow>(8);
        private readonly Dictionary<UIWindow, int> _sceneBoundWindowIndices = new Dictionary<UIWindow, int>(8);
        private readonly List<UIWindow> _sceneBoundSweepScratch = new List<UIWindow>(8);

        // Throttling instantiate per frame
        private int maxInstantiatesPerFrame = 2;
        private int instantiatesThisFrame = 0;
        private int _currentActiveSceneHandle = -1;
        private int _pendingSceneSweepTargetHandle = -1;
        private bool _hasPendingSceneBoundSweep;
        private int _pendingSceneBoundSweepDelayFrames;

        // Tracks ongoing opening operations to prevent duplicate concurrent opens
        private Dictionary<string, UniTaskCompletionSource<UIWindow>> uiOpenTCS = new Dictionary<string, UniTaskCompletionSource<UIWindow>>();

        // Per-window CTS for cancelling in-flight open operations when Close arrives early
        private readonly Dictionary<string, CancellationTokenSource> _openCancellations = new Dictionary<string, CancellationTokenSource>(8);

        // Tracks active windows for quick access and management
        private Dictionary<string, UIWindow> activeWindows = new Dictionary<string, UIWindow>();

        // Window binders for custom initialization/MVP decoupling
        private List<IUIWindowBinder> windowBinders = new List<IUIWindowBinder>(4);
        private IUIWindowBinder[] _windowBindersCache = null;

        // Optional navigation service — set via SetNavigationService()
        private IUINavigationService _navigationService;

        // Mutex for coordinated navigation — cancels previous in-flight navigation (DirectJump mode)
        private CancellationTokenSource _coordinatedNavCts;

        // Strategy that governs rapid sequential coordinated navigation behaviour.
        private CoordinatedNavStrategy _coordinatedNavStrategy = CoordinatedNavStrategy.DirectJump;

        // In-flight coordinated navigation tracking for "direct jump" support.
        // When a new navigation arrives mid-animation, we skip the intermediate page
        // and transition directly from the original source to the final destination.
        // E.g. A→B mid-animation + B→C request = skip B, animate A→C.
        private int _coordinatedNavGeneration;
        private string _inflightLeavingName;
        private UIWindow _inflightLeaving;
        private string _inflightEnteringName;
        private UIWindow _inflightEntering;

        /// <summary>
        /// Enables verbose asset lifecycle diagnostics (source mode, shared-handle release decisions).
        /// Default is false to keep runtime log and string-format overhead minimal.
        /// </summary>
        public bool EnableAssetLifecycleDebugLog
        {
            get => enableAssetLifecycleDebugLog;
            set => enableAssetLifecycleDebugLog = value;
        }

        private void LogAssetLifecycleDebug(string message)
        {
            if (!enableAssetLifecycleDebugLog || string.IsNullOrEmpty(message)) return;
            CLogger.LogInfo($"{DEBUG_FLAG} [AssetLifecycle] {message}");
        }


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
            WarmupDefaultAssetLoadContext();
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
            _currentActiveSceneHandle = UnityEngine.SceneManagement.SceneManager.GetActiveScene().handle;
            // It's better to get UIRoot in Initialize if UIManager is created and initialized from code.
            // If UIManager is a scene object and Initialize is called later, Awake can find UIRoot.
            TryGetUIRoot();
        }

        private void OnEnable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded += OnSceneUnloaded;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged += OnActiveSceneChanged;
        }

        private void OnDisable()
        {
            UnityEngine.SceneManagement.SceneManager.sceneUnloaded -= OnSceneUnloaded;
            UnityEngine.SceneManagement.SceneManager.activeSceneChanged -= OnActiveSceneChanged;
        }

        private void OnSceneUnloaded(UnityEngine.SceneManagement.Scene scene)
        {
            // clean up the window handles to prevent leaks.
            if (uiRoot == null || uiRoot.gameObject.scene == scene)
            {
                CleanupAllWindows();
            }
        }

        private void OnActiveSceneChanged(UnityEngine.SceneManagement.Scene previousScene, UnityEngine.SceneManagement.Scene newScene)
        {
            _currentActiveSceneHandle = newScene.handle;
            RequestSceneBoundSweep(newScene.handle);
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

            // Cancel and dispose all in-flight opens
            foreach (var kv in _openCancellations)
            {
                kv.Value?.Cancel();
                kv.Value?.Dispose();
            }
            _openCancellations.Clear();

            _coordinatedNavCts?.Cancel();
            _coordinatedNavCts?.Dispose();
            _coordinatedNavCts = null;

            foreach (var kv in _configHandles) kv.Value?.Dispose();
            _configHandles.Clear();

            foreach (var kv in _prefabHandles) kv.Value?.Dispose();
            _prefabHandles.Clear();
            _windowToPrefabLocation.Clear();
            _sceneBoundWindows.Clear();
            _sceneBoundWindowIndices.Clear();
            _sceneBoundSweepScratch.Clear();
            _hasPendingSceneBoundSweep = false;
            _pendingSceneBoundSweepDelayFrames = 0;
            _pendingSceneSweepTargetHandle = -1;

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

        private void LateUpdate()
        {
            if (!_hasPendingSceneBoundSweep) return;

            if (_pendingSceneBoundSweepDelayFrames > 0)
            {
                _pendingSceneBoundSweepDelayFrames--;
                return;
            }

            ProcessSceneBoundSweep(_pendingSceneSweepTargetHandle);
            _hasPendingSceneBoundSweep = false;
        }

        // Start is not typically used if Initialize sets up dependencies.
        // private void Start() { }

        /// <summary>
        /// Opens a UI window by its name.
        /// </summary>
        /// <param name="windowName">The unique name of the window (often matches configuration file name).</param>
        /// <param name="onUIWindowCreated">Optional callback when the window is instantiated and added.</param>
        public void OpenUI(string windowName, System.Action<UIWindow> onUIWindowCreated = null, bool? isSceneBoundOverride = null, UIAssetLoadContext assetLoadContext = default)
        {
            if (uiRoot == null || assetPathBuilder == null || objectSpawner == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UIManager not properly initialized. Cannot open UI: {windowName}");
                onUIWindowCreated?.Invoke(null); // Notify failure
                return;
            }
            OpenUIAsync(windowName, onUIWindowCreated, default, false, isSceneBoundOverride, assetLoadContext).Forget(); // Fire and forget UniTask
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

        internal async UniTask<UIWindow> OpenUIAsync(string windowName, System.Action<UIWindow> onUIWindowCreated = null, CancellationToken cancellationToken = default, bool silentOpen = false, bool? isSceneBoundOverride = null, UIAssetLoadContext assetLoadContext = default)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} WindowName cannot be null or empty.");
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            if (activeWindows.ContainsKey(windowName))
            {
                UIWindow existingWindow = activeWindows[windowName];

                // Guard: window may have been destroyed externally (e.g. mid-scene-unload) while
                // UIManager still held the dictionary entry. Clean up the stale state and fall
                // through to open a fresh instance rather than returning a dead reference.
                if (existingWindow == null || existingWindow.gameObject == null)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' was externally destroyed. " +
                                       "Cleaning up stale entry and re-opening.");
                    UnregisterSceneBoundWindow(existingWindow);
                    _navigationService?.Unregister(windowName);
                    activeWindows.Remove(windowName);
                    uiOpenTCS.Remove(windowName);
                    ReleaseConfigHandle(windowName);
                    ReleaseWindowAsset(windowName);
                    // fall through — open a fresh instance below
                }
                else
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' is already open or opening.");
                    onUIWindowCreated?.Invoke(existingWindow);
                    return existingWindow;
                }
            }

            // Check if an opening operation is already in progress
            if (uiOpenTCS.TryGetValue(windowName, out var existingTcs))
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Window '{windowName}' open operation already in progress. Awaiting existing task.");
                UIWindow window = await existingTcs.Task.AttachExternalCancellation(cancellationToken);
                onUIWindowCreated?.Invoke(window);
                return window;
            }

            var tcs = new UniTaskCompletionSource<UIWindow>();
            uiOpenTCS[windowName] = tcs;

            // Capture the active scene at the moment of this open request.
            // This handle is used to bind scene-bound windows to the scene that requested them,
            // even if the scene changes asynchronously before the prefab finishes loading.
            int openRequestedSceneHandle = _currentActiveSceneHandle;

            // Create a linked CTS so CloseUI can cancel this open mid-flight
            var openCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _openCancellations[windowName] = openCts;
            var ct = openCts.Token;
            UIAssetLoadContext resolvedAssetLoadContext = assetLoadContext
                .Merge(await ResolveDefaultAssetLoadContextAsync(ct));

            CLogger.LogInfo($"{DEBUG_FLAG} Attempting to open UI: {windowName}");
            string configPath = assetPathBuilder.GetAssetPath(windowName);
            if (string.IsNullOrEmpty(configPath))
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to get asset path for UI: {windowName}. Check AssetPathBuilder.");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(new System.InvalidOperationException($"Asset path not found for {windowName}"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            UIWindowConfiguration windowConfig = null;
            try
            {
                ct.ThrowIfCancellationRequested();
                if (assetPackage == null)
                {
                    throw new System.InvalidOperationException("IAssetPackage is not available.");
                }

                if (!_configHandles.TryGetValue(windowName, out var configHandle))
                {
                    configHandle = assetPackage.LoadAssetAsync<UIWindowConfiguration>(
                        configPath,
                        resolvedAssetLoadContext.ConfigBucket,
                        resolvedAssetLoadContext.ConfigTag,
                        resolvedAssetLoadContext.ConfigOwner,
                        ct);
                    await WaitForOperationCompletedAsync(configHandle, ct);

                    if (!string.IsNullOrEmpty(configHandle.Error) || configHandle.Asset == null)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} Failed to load UIWindowConfiguration at path: {configPath} for WindowName: {windowName}. Error: {configHandle.Error}");
                        configHandle.Dispose();
                        CleanupOpenState(windowName, openCts);
                        tcs.TrySetException(new System.Exception($"Failed to load UIWindowConfiguration for {windowName}"));
                        onUIWindowCreated?.Invoke(null);
                        return null;
                    }

                    _configHandles[windowName] = configHandle;
                }

                windowConfig = configHandle.Asset;

                if (!windowConfig.IsConfigured)
                {
                    CLogger.LogError($"{DEBUG_FLAG} WindowConfig for '{windowName}' is not configured (Source={windowConfig.Source}).");
                    CleanupOpenState(windowName, openCts);
                    ReleaseConfigHandle(windowName);
                    tcs.TrySetException(new System.NullReferenceException($"WindowConfig not configured for {windowName}"));
                    onUIWindowCreated?.Invoke(null);
                    return null;
                }
            }
            catch (System.OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Open UI operation for '{windowName}' was canceled.");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetCanceled();
                onUIWindowCreated?.Invoke(null);
                return null;
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Exception while loading UIWindowConfiguration for {windowName}: {ex.Message}\n{ex.StackTrace}");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(ex);
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            if (windowConfig.Layer == null || string.IsNullOrEmpty(windowConfig.Layer.LayerName))
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayerConfiguration or LayerName is not set in WindowConfig for: {windowName}");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(new System.NullReferenceException($"LayerConfig null for {windowName}"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            // UIRoot might be created later by foundation bootstrap (e.g. delayed resolver entry),
            // so always try to resolve it again right before layer lookup.
            var currentRoot = TryGetUIRoot();
            if (currentRoot == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UIRoot is null while opening '{windowName}'. Ensure foundation UIRoot entry is instantiated before opening UI.");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(new System.NullReferenceException($"UIRoot is null for {windowName}"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            string layerName = windowConfig.Layer.LayerName;
            UILayer uiLayer = currentRoot.GetUILayer(layerName);

            if (uiLayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayer not found: {layerName} (for window {windowName})");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(new System.Exception($"UILayer '{layerName}' not found"));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            if (uiLayer.HasWindow(windowName))
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' already exists in layer '{uiLayer.LayerName}'. Aborting duplicate open.");
                if (activeWindows.TryGetValue(windowName, out var existingWindowInstance))
                {
                    CleanupOpenState(windowName, openCts);
                    tcs.TrySetResult(existingWindowInstance);
                    onUIWindowCreated?.Invoke(existingWindowInstance);
                    return existingWindowInstance;
                }
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(new System.InvalidOperationException($"Window '{windowName}' exists in layer but not in UIManager's active list."));
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            UIWindow uiWindowInstance = null;
            string managedPrefabLocation = null;
            try
            {
                if (windowConfig.Source == UIWindowConfiguration.PrefabSource.PrefabReference)
                {
                    LogAssetLifecycleDebug($"Opening UIWindow '{windowName}' with source mode: PrefabReference (direct prefab reference).");
                }
                else
                {
                    LogAssetLifecycleDebug($"Opening UIWindow '{windowName}' with source mode: {windowConfig.Source} (location='{windowConfig.EffectiveLocation}').");
                }

                if (windowConfig.Source != UIWindowConfiguration.PrefabSource.PrefabReference)
                {
                    // Handles both AssetReference (AssetRef<GameObject>.Location) and
                    // PathLocation (plain string) — EffectiveLocation unifies both.
                    string effectiveLocation = windowConfig.EffectiveLocation;
                    managedPrefabLocation = effectiveLocation;
                    if (string.IsNullOrEmpty(effectiveLocation) || assetPackage == null)
                    {
                        throw new System.InvalidOperationException($"Prefab source is '{windowConfig.Source}' but EffectiveLocation is empty or AssetPackage is not available.");
                    }
                    if (!_prefabHandles.TryGetValue(effectiveLocation, out var prefabHandle))
                    {
                        prefabHandle = assetPackage.LoadAssetAsync<GameObject>(
                            effectiveLocation,
                            resolvedAssetLoadContext.PrefabBucket,
                            resolvedAssetLoadContext.PrefabTag,
                            resolvedAssetLoadContext.PrefabOwner,
                            ct);
                        await WaitForOperationCompletedAsync(prefabHandle, ct);

                        if (!string.IsNullOrEmpty(prefabHandle.Error) || prefabHandle.Asset == null)
                        {
                            prefabHandle.Dispose();
                            throw new System.Exception($"Failed to load UI prefab at '{effectiveLocation}': {prefabHandle?.Error}");
                        }

                        _prefabHandles[effectiveLocation] = prefabHandle;
                        LogAssetLifecycleDebug($"Loaded prefab handle for location '{effectiveLocation}'.");
                    }
                    else
                    {
                        LogAssetLifecycleDebug($"Reusing cached prefab handle for location '{effectiveLocation}'.");
                    }

                    var go = prefabHandle.Asset;
                    await ThrottleInstantiate(ct);
                    var spawnedGo = objectSpawner.Create(go);
                    uiWindowInstance = spawnedGo != null ? spawnedGo.GetComponent<UIWindow>() : null;

                    if (uiWindowInstance != null)
                    {
                        _windowToPrefabLocation[windowName] = effectiveLocation;
                    }
                }
                else // PrefabReference
                {
                    await ThrottleInstantiate(ct);
                    uiWindowInstance = objectSpawner.Create(windowConfig.WindowPrefab) as UIWindow;
                }

                if (uiWindowInstance == null)
                {
                    throw new System.NullReferenceException($"Spawned GameObject for {windowName} does not have a UIWindow component.");
                }

                // External-destroy safety net: if a window is destroyed outside UIManager flow,
                // UIWindow.OnDestroy can still trigger an idempotent location-based release.
                if (!string.IsNullOrEmpty(managedPrefabLocation))
                {
                    uiWindowInstance.SetSourceAssetPath(managedPrefabLocation);
                    uiWindowInstance.OnReleaseAssetReference = OnWindowReleaseAssetReference;
                }
                else
                {
                    uiWindowInstance.SetSourceAssetPath(null);
                    uiWindowInstance.OnReleaseAssetReference = null;
                }

                if (transitionDriver != null)
                {
                    uiWindowInstance.SetTransitionDriver(transitionDriver);
                }
            }
            catch (System.OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Open UI operation for '{windowName}' was canceled during instantiation.");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetCanceled();
                onUIWindowCreated?.Invoke(null);
                return null;
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to instantiate UIWindow prefab for {windowName}: {ex.Message}\n{ex.StackTrace}");
                CleanupOpenState(windowName, openCts);
                tcs.TrySetException(ex);
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            uiWindowInstance.SetWindowName(windowName);
            uiWindowInstance.SetConfiguration(windowConfig);
            bool isSceneBound = isSceneBoundOverride ?? windowConfig.IsSceneBound;
            // Bind to the scene that was active when OpenUI was called, NOT the current scene.
            // This ensures windows that finish loading after a scene change are still correctly
            // identified as belonging to the old scene and will be swept by the next scene-change.
            uiWindowInstance.ConfigureSceneBinding(isSceneBound, isSceneBound ? openRequestedSceneHandle : -1);
            ApplyWindowCanvasIsolation(uiWindowInstance, windowConfig, uiLayer);
            uiWindowInstance._binders = _windowBindersCache;
            uiLayer.AddWindow(uiWindowInstance);
            activeWindows[windowName] = uiWindowInstance;
            RegisterSceneBoundWindow(uiWindowInstance);
            _navigationService?.Register(windowName);

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

            try
            {
                await UniTask.Yield(ct);

                if (silentOpen)
                    await uiWindowInstance.OpenSilentAsync(ct);
                else
                    await uiWindowInstance.OpenAsync(ct);
            }
            catch (System.OperationCanceledException)
            {
                CLogger.LogInfo($"{DEBUG_FLAG} Open UI operation for '{windowName}' was canceled during open transition.");
                // Window is already in activeWindows/layer — tear it down
                TearDownLeavingWindow(uiWindowInstance, windowName);
                CleanupOpenState(windowName, openCts);
                tcs.TrySetCanceled();
                onUIWindowCreated?.Invoke(null);
                return null;
            }

            onUIWindowCreated?.Invoke(uiWindowInstance);
            tcs.TrySetResult(uiWindowInstance);
            CleanupOpenState(windowName, openCts);

            // Post-open scene-change guard: if the active scene changed between the moment
            // OpenUI was called and now (e.g. async loading took long enough to straddle a
            // scene transition), the pending sweep has already run and missed this window.
            // Close it immediately to honour the scene-bound contract.
            if (isSceneBound && _currentActiveSceneHandle != openRequestedSceneHandle)
            {
                CLogger.LogWarning($"{DEBUG_FLAG} Scene changed while '{windowName}' was opening " +
                                   $"(requested in scene {openRequestedSceneHandle}, " +
                                   $"current scene {_currentActiveSceneHandle}). " +
                                   "Auto-closing scene-bound window.");
                CloseUI(windowName);
            }
            return uiWindowInstance;
        }

        internal UniTask<UIWindow> OpenUIAndWait(string windowName, System.Threading.CancellationToken cancellationToken = default, bool? isSceneBoundOverride = null, UIAssetLoadContext assetLoadContext = default)
        {
            return OpenUIAsync(windowName, null, cancellationToken, false, isSceneBoundOverride, assetLoadContext);
        }

        // Loads, instantiates, and initialises the window (including binders/MVP), but
        // calls OpenSilentAsync instead of Open() so no entry animation plays yet.
        // The window is ready for the coordinator to animate both sides simultaneously.
        internal async UniTask<UIWindow> OpenUIReadyAsync(string windowName, System.Threading.CancellationToken ct = default)
        {
            UIWindow window = await OpenUIAsync(windowName, null, ct, silentOpen: true, assetLoadContext: default);
            return window;
        }

        /// <summary>
        /// Sets the strategy for handling rapid sequential coordinated navigations.
        /// </summary>
        internal void SetCoordinatedNavStrategy(CoordinatedNavStrategy strategy)
        {
            _coordinatedNavStrategy = strategy;
        }

        // Coordinates a simultaneous transition between two windows.
        // Behaviour when a new navigation arrives mid-animation is governed by
        // _coordinatedNavStrategy: DirectJump skips intermediate pages; CardStack
        // allows overlapping transitions for a cascading stacked-card feel.
        internal async UniTask CoordinatedNavigateAsync(
            string fromName, string toName,
            NavigationDirection direction,
            IUITransitionCoordinator coordinator,
            CancellationToken ct = default)
        {
            if (coordinator == null)
            {
                OpenUI(toName);
                return;
            }

            if (_coordinatedNavStrategy == CoordinatedNavStrategy.CardStack)
            {
                await CoordinatedNavigateCardStackAsync(fromName, toName, direction, coordinator, ct);
            }
            else
            {
                await CoordinatedNavigateDirectJumpAsync(fromName, toName, direction, coordinator, ct);
            }
        }

        // ── DirectJump: skip intermediate pages and animate directly from source to final dest ──
        private async UniTask CoordinatedNavigateDirectJumpAsync(
            string fromName, string toName,
            NavigationDirection direction,
            IUITransitionCoordinator coordinator,
            CancellationToken ct)
        {
            // --- Phase 1: Cancel previous nav, claim generation, determine effective leaving ---
            var prevLeavingName = _inflightLeavingName;
            var prevLeaving = _inflightLeaving;
            var prevEnteringName = _inflightEnteringName;
            var prevEntering = _inflightEntering;
            bool hadInFlight = _inflightLeaving != null;

            int myGeneration = ++_coordinatedNavGeneration;

            _coordinatedNavCts?.Cancel();
            _coordinatedNavCts?.Dispose();
            _coordinatedNavCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var navCt = _coordinatedNavCts.Token;

            // Direct-jump: if a previous nav was in-flight, skip the intermediate page.
            // Use the previous nav's leaving window as our leaving (A→C instead of B→C).
            string effectiveFromName;
            UIWindow effectiveLeaving;

            if (hadInFlight)
            {
                effectiveFromName = prevLeavingName;
                effectiveLeaving = prevLeaving;

                // Tear down the intermediate entering window if it was already loaded
                if (prevEntering != null)
                {
                    TearDownLeavingWindow(prevEntering, prevEnteringName);
                }
                else if (!string.IsNullOrEmpty(prevEnteringName)
                         && activeWindows.TryGetValue(prevEnteringName, out var staleEntering))
                {
                    TearDownLeavingWindow(staleEntering, prevEnteringName);
                }
            }
            else
            {
                effectiveFromName = fromName;
                activeWindows.TryGetValue(fromName, out effectiveLeaving);
            }

            // --- Phase 2: Track this nav's in-flight state ---
            _inflightLeavingName = effectiveFromName;
            _inflightLeaving = effectiveLeaving;
            _inflightEnteringName = toName;
            _inflightEntering = null;

            // --- Phase 3: Load entering window ---
            UIWindow entering;
            try
            {
                entering = await OpenUIReadyAsync(toName, navCt);
            }
            catch (System.OperationCanceledException)
            {
                entering = null;
            }

            if (navCt.IsCancellationRequested || entering == null)
            {
                if (myGeneration == _coordinatedNavGeneration)
                {
                    TearDownLeavingWindow(effectiveLeaving, effectiveFromName);
                    if (activeWindows.TryGetValue(toName, out var staleEntering))
                    {
                        TearDownLeavingWindow(staleEntering, toName);
                    }
                    ClearInflightState();
                }
                return;
            }

            _inflightEntering = entering;

            // --- Phase 4: Run coordinator animation ---
            await coordinator.TransitionAsync(effectiveLeaving, entering, direction, navCt);

            if (navCt.IsCancellationRequested)
            {
                if (myGeneration == _coordinatedNavGeneration)
                {
                    TearDownLeavingWindow(effectiveLeaving, effectiveFromName);
                    TearDownLeavingWindow(entering, toName);
                    ClearInflightState();
                }
                return;
            }

            // --- Phase 5: Success ---
            ClearInflightState();
            TearDownLeavingWindow(effectiveLeaving, effectiveFromName);
        }

        // ── CardStack: overlapping transitions for a cascading stacked-card feel ──
        // Each navigation runs independently; a new B→C starts immediately while A→B
        // continues. When A→B finishes, A is torn down. When B→C finishes, B is torn down.
        // This produces a fluid "cards fanning out" visual.
        private async UniTask CoordinatedNavigateCardStackAsync(
            string fromName, string toName,
            NavigationDirection direction,
            IUITransitionCoordinator coordinator,
            CancellationToken ct)
        {
            activeWindows.TryGetValue(fromName, out UIWindow leaving);

            var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var navCt = cts.Token;

            UIWindow entering;
            try
            {
                entering = await OpenUIReadyAsync(toName, navCt);
            }
            catch (System.OperationCanceledException)
            {
                entering = null;
            }

            if (navCt.IsCancellationRequested || entering == null)
            {
                cts.Dispose();
                return;
            }

            // Run the coordinator animation — this pair runs independently,
            // so other CardStack navigations can overlap.
            try
            {
                await coordinator.TransitionAsync(leaving, entering, direction, navCt);
            }
            finally
            {
                cts.Dispose();
            }

            if (navCt.IsCancellationRequested) return;

            // Tear down the leaving window only after THIS transition completes.
            TearDownLeavingWindow(leaving, fromName);
        }

        private void ClearInflightState()
        {
            _inflightLeavingName = null;
            _inflightLeaving = null;
            _inflightEnteringName = null;
            _inflightEntering = null;
        }

        /// <summary>
        /// Tears down a leaving window: close lifecycle (silent), detach from layer,
        /// unregister navigation, release asset handles.
        /// Safe to call with null or already-cleaned-up windows.
        /// </summary>
        private void TearDownLeavingWindow(UIWindow leaving, string windowName)
        {
            if (leaving == null) return;

            UnregisterSceneBoundWindow(leaving);

            UILayer layer = leaving.ParentLayer;
            layer?.DetachWindow(windowName);

            // Run close lifecycle synchronously (no transition animation needed).
            // Close() triggers OnStartClose/OnFinishedClose which deliver
            // OnViewClosing/OnViewClosed to presenters via state-change binder callbacks.
            leaving.Close();

            // Notify binders AFTER close lifecycle so Dispose runs after OnViewClosed.
            var binders = _windowBindersCache;
            if (binders != null)
            {
                for (int i = 0; i < binders.Length; i++)
                {
                    try { binders[i].OnWindowDestroying(leaving); }
                    catch (System.Exception ex)
                    {
                        CLogger.LogError($"{DEBUG_FLAG} WindowBinder {binders[i].GetType().Name} failed during OnWindowDestroying for {windowName}: {ex.Message}");
                    }
                }
            }

            if (activeWindows.ContainsKey(windowName))
            {
                _navigationService?.Unregister(windowName);
                activeWindows.Remove(windowName);
                uiOpenTCS.Remove(windowName);
                ReleaseConfigHandle(windowName);
                ReleaseWindowAsset(windowName);
            }
        }

        internal async UniTask CloseUIAsync(string windowName, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(windowName))
            {
                CLogger.LogError($"{DEBUG_FLAG} WindowName cannot be null or empty for CloseUI.");
                return;
            }

            try
            {
                cancellationToken.ThrowIfCancellationRequested();

                // If an open operation is still in progress, cancel it directly instead of waiting
                if (_openCancellations.TryGetValue(windowName, out var openCancel))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Close requested for '{windowName}' which is still loading. Cancelling open.");
                    openCancel.Cancel();
                    // Wait briefly for the cancellation to propagate and clean up
                    if (uiOpenTCS.TryGetValue(windowName, out var openTcs))
                    {
                        try { await openTcs.Task.SuppressCancellationThrow(); } catch { /* swallow */ }
                    }
                    // If the open was cancelled before instantiation, there's nothing to close
                if (!activeWindows.ContainsKey(windowName))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Open for '{windowName}' was cancelled before it was ready. Nothing to close.");
                    return;
                }
                }

                if (!activeWindows.TryGetValue(windowName, out UIWindow windowToClose))
                {
                    CLogger.LogInfo($"{DEBUG_FLAG} Window '{windowName}' not found in active windows. Skipping close.");
                    return;
                }

                if (windowToClose == null || windowToClose.gameObject == null)
                {
                    CLogger.LogWarning($"{DEBUG_FLAG} Window '{windowName}' is null or destroyed. Cannot close.");
                    if (windowToClose != null) UnregisterSceneBoundWindow(windowToClose);
                    _navigationService?.Unregister(windowName);
                    activeWindows.Remove(windowName);
                    uiOpenTCS.Remove(windowName);
                    ReleaseConfigHandle(windowName);
                    ReleaseWindowAsset(windowName);
                    return;
                }

                CLogger.LogInfo($"{DEBUG_FLAG} Attempting to close UI: {windowName}");

                // Always use async close path to ensure transition animations play
                UILayer layer = windowToClose.ParentLayer;
                if (layer != null)
                {
                    layer.DetachWindow(windowName);
                }

                await windowToClose.CloseAsync(cancellationToken);

                // If the close transition was cancelled mid-animation, the window's
                // OnFinishedClose was never called so the GameObject is still alive.
                // Force-complete the close to prevent an orphaned zombie in the scene.
                if (cancellationToken.IsCancellationRequested && windowToClose != null && windowToClose.gameObject != null)
                {
                    windowToClose.Close();
                }

                // Notify binders AFTER the close lifecycle completes so presenters receive
                // OnViewClosing / OnViewClosed before Dispose().
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

                // Cleanup
                UnregisterSceneBoundWindow(windowToClose);
                _navigationService?.Unregister(windowName);
                activeWindows.Remove(windowName);
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

        public UIPerformanceStats GetPerformanceStats()
        {
            UIRoot root = TryGetUIRoot();
            int layerCount = 0;
            int totalLayerWindowCount = 0;
            if (root != null)
            {
                for (int i = 0; i < root.transform.childCount; i++)
                {
                    Transform child = root.transform.GetChild(i);
                    if (child == null) continue;
                    UILayer layer = child.GetComponent<UILayer>();
                    if (layer == null) continue;

                    layerCount++;
                    totalLayerWindowCount += layer.WindowCount;
                }
            }

            int isolatedWindowCanvasCount = 0;
            foreach (var kv in activeWindows)
            {
                UIWindow window = kv.Value;
                if (window == null || window.gameObject == null) continue;
                if (window.GetComponent<Canvas>() != null)
                {
                    isolatedWindowCanvasCount++;
                }
            }

            return new UIPerformanceStats(
                activeWindows.Count,
                _sceneBoundWindows.Count,
                _openCancellations.Count,
                _configHandles.Count,
                _prefabHandles.Count,
                layerCount,
                totalLayerWindowCount,
                isolatedWindowCanvasCount,
                _hasPendingSceneBoundSweep);
        }

        public void CopyLayerRuntimeStats(List<UILayerRuntimeStats> results)
        {
            if (results == null) return;
            results.Clear();

            UIRoot root = TryGetUIRoot();
            if (root == null) return;

            for (int i = 0; i < root.transform.childCount; i++)
            {
                Transform child = root.transform.GetChild(i);
                if (child == null) continue;

                UILayer layer = child.GetComponent<UILayer>();
                if (layer == null) continue;

                int sortingOrder = layer.UICanvas != null ? layer.UICanvas.sortingOrder : 0;
                results.Add(new UILayerRuntimeStats(layer.LayerName, sortingOrder, layer.WindowCount));
            }
        }

        public Camera GetUICamera()
        {
            return TryGetUIRoot()?.UICamera;
        }

        protected void OnDestroy()
        {
            UnityEngine.Application.onBeforeRender -= ResetPerFrameBudget;

            foreach (var kv in _openCancellations)
            {
                kv.Value?.Cancel();
                kv.Value?.Dispose();
            }
            _openCancellations.Clear();
            _coordinatedNavCts?.Cancel();
            _coordinatedNavCts?.Dispose();
            _coordinatedNavCts = null;
            ClearInflightState();

            foreach (var kv in _configHandles) kv.Value?.Dispose();
            _configHandles.Clear();

            foreach (var kv in _prefabHandles) kv.Value?.Dispose();
            _prefabHandles.Clear();
            _windowToPrefabLocation.Clear();
            _sceneBoundWindows.Clear();
            _sceneBoundWindowIndices.Clear();
            _sceneBoundSweepScratch.Clear();
            _hasPendingSceneBoundSweep = false;
            _pendingSceneBoundSweepDelayFrames = 0;
            _pendingSceneSweepTargetHandle = -1;

            activeWindows.Clear();
            uiOpenTCS.Clear();

            CLogger.LogInfo($"{DEBUG_FLAG} UIManager is being destroyed.");
        }

        // ── Handle release helpers ──────────────────────────────────────────

        public void ReleaseWindowAsset(string windowName)
        {
            if (string.IsNullOrEmpty(windowName)) return;

            if (!_windowToPrefabLocation.TryGetValue(windowName, out var prefabLocation)) return;
            _windowToPrefabLocation.Remove(windowName);

            LogAssetLifecycleDebug($"Release request by window '{windowName}' for location '{prefabLocation}'.");

            ReleaseWindowAssetByLocation(prefabLocation);
        }

        private void OnWindowReleaseAssetReference(string prefabLocation)
        {
            LogAssetLifecycleDebug($"Release request from UIWindow.OnDestroy callback for location '{prefabLocation}'.");
            ReleaseWindowAssetByLocation(prefabLocation);
        }

        private void ReleaseWindowAssetByLocation(string prefabLocation)
        {
            if (string.IsNullOrEmpty(prefabLocation)) return;

            // Prune stale mappings for this location (destroyed externally or already invalid)
            // before checking if the location is still in use.
            _releaseScratchWindowNames.Clear();
            foreach (var kv in _windowToPrefabLocation)
            {
                if (!string.Equals(kv.Value, prefabLocation, System.StringComparison.Ordinal)) continue;

                if (!activeWindows.TryGetValue(kv.Key, out var w) || w == null || w.gameObject == null)
                {
                    _releaseScratchWindowNames.Add(kv.Key);
                }
            }

            for (int i = 0; i < _releaseScratchWindowNames.Count; i++)
            {
                _windowToPrefabLocation.Remove(_releaseScratchWindowNames[i]);
            }
            if (_releaseScratchWindowNames.Count > 0)
            {
                LogAssetLifecycleDebug($"Pruned {_releaseScratchWindowNames.Count} stale window->location mappings for '{prefabLocation}'.");
            }
            _releaseScratchWindowNames.Clear();

            // Check if any other open window still shares this prefab
            bool stillInUse = false;
            foreach (var kv in _windowToPrefabLocation)
            {
                if (string.Equals(kv.Value, prefabLocation, System.StringComparison.Ordinal))
                {
                    stillInUse = true;
                    break;
                }
            }

            if (!stillInUse && _prefabHandles.TryGetValue(prefabLocation, out var handle))
            {
                handle.Dispose();
                _prefabHandles.Remove(prefabLocation);
                LogAssetLifecycleDebug($"Disposed prefab handle for location '{prefabLocation}' (no active users).");
            }
            else if (stillInUse)
            {
                LogAssetLifecycleDebug($"Kept prefab handle for location '{prefabLocation}' (still in use).");
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

        private void CleanupOpenState(string windowName, CancellationTokenSource openCts)
        {
            uiOpenTCS.Remove(windowName);
            _openCancellations.Remove(windowName);
            openCts?.Dispose();
        }

        private void ApplyWindowCanvasIsolation(UIWindow window, UIWindowConfiguration config, UILayer layer)
        {
            if (window == null || config == null || layer == null) return;
            if (!ShouldIsolateWindowCanvas(window, config)) return;

            Canvas parentCanvas = layer.UICanvas;
            if (parentCanvas == null) return;

            Canvas nestedCanvas = window.GetComponent<Canvas>();
            if (nestedCanvas == null)
            {
                nestedCanvas = window.gameObject.AddComponent<Canvas>();
            }

            nestedCanvas.overrideSorting = false;
            nestedCanvas.additionalShaderChannels = parentCanvas.additionalShaderChannels;
            nestedCanvas.pixelPerfect = parentCanvas.pixelPerfect;

            if (window.GetComponent<GraphicRaycaster>() == null)
            {
                window.gameObject.AddComponent<GraphicRaycaster>();
            }
        }

        private async UniTask<UIAssetLoadContext> ResolveDefaultAssetLoadContextAsync(CancellationToken cancellationToken)
        {
            UIRoot root = TryGetUIRoot();
            if (root != null && root.AssetContextProvider != null)
            {
                return await root.AssetContextProvider.ResolveLoadContextAsync(assetPackage, cancellationToken);
            }

            return default;
        }

        private void WarmupDefaultAssetLoadContext()
        {
            UIRoot root = TryGetUIRoot();
            if (root == null || root.AssetContextProvider == null)
            {
                return;
            }

            root.AssetContextProvider.BeginWarmup(assetPackage);
        }

        private static bool ShouldIsolateWindowCanvas(UIWindow window, UIWindowConfiguration config)
        {
            switch (config.CanvasIsolationPolicy)
            {
                case UIWindowConfiguration.SubCanvasPolicy.ForceOwnSubCanvas:
                    return true;
                case UIWindowConfiguration.SubCanvasPolicy.AutoDetect:
                    return HasHighChurnUiMarkers(window);
                default:
                    return false;
            }
        }

        private static bool HasHighChurnUiMarkers(UIWindow window)
        {
            return window.GetComponentInChildren<Animator>(true) != null ||
                   window.GetComponentInChildren<Animation>(true) != null ||
                   window.GetComponentInChildren<ScrollRect>(true) != null ||
                   window.GetComponentInChildren<LayoutGroup>(true) != null ||
                   window.GetComponentInChildren<ContentSizeFitter>(true) != null ||
                   window.GetComponentInChildren<Mask>(true) != null ||
                   window.GetComponentInChildren<RectMask2D>(true) != null;
        }

        private void RequestSceneBoundSweep(int targetSceneHandle)
        {
            _pendingSceneSweepTargetHandle = targetSceneHandle;
            _hasPendingSceneBoundSweep = true;
            _pendingSceneBoundSweepDelayFrames = 1;
        }

        private void RegisterSceneBoundWindow(UIWindow window)
        {
            if (ReferenceEquals(window, null) || !window.IsSceneBound) return;
            if (_sceneBoundWindowIndices.ContainsKey(window)) return;

            int index = _sceneBoundWindows.Count;
            _sceneBoundWindows.Add(window);
            _sceneBoundWindowIndices[window] = index;
        }

        private void UnregisterSceneBoundWindow(UIWindow window)
        {
            if (ReferenceEquals(window, null)) return;
            if (!_sceneBoundWindowIndices.TryGetValue(window, out int index)) return;

            int lastIndex = _sceneBoundWindows.Count - 1;
            UIWindow lastWindow = _sceneBoundWindows[lastIndex];
            _sceneBoundWindows[index] = lastWindow;
            _sceneBoundWindows.RemoveAt(lastIndex);
            _sceneBoundWindowIndices.Remove(window);

            if (index < _sceneBoundWindows.Count && lastWindow != null)
            {
                _sceneBoundWindowIndices[lastWindow] = index;
            }
        }

        private void ProcessSceneBoundSweep(int targetSceneHandle)
        {
            if (_sceneBoundWindows.Count == 0) return;

            _sceneBoundSweepScratch.Clear();

            for (int i = 0; i < _sceneBoundWindows.Count; i++)
            {
                UIWindow window = _sceneBoundWindows[i];
                if (ReferenceEquals(window, null) || window == null || window.gameObject == null)
                {
                    _sceneBoundSweepScratch.Add(window);
                    continue;
                }

                if (!window.IsSceneBound) continue;
                if (window.BoundSceneHandle == targetSceneHandle) continue;

                _sceneBoundSweepScratch.Add(window);
            }

            for (int i = 0; i < _sceneBoundSweepScratch.Count; i++)
            {
                UIWindow window = _sceneBoundSweepScratch[i];
                if (ReferenceEquals(window, null))
                {
                    continue;
                }

                if (window == null || window.gameObject == null)
                {
                    // Already destroyed externally — just clean the tracking list.
                    UnregisterSceneBoundWindow(window);
                    continue;
                }

                string windowName = window.WindowName;
                if (string.IsNullOrEmpty(windowName)) continue;

                // Guard: a previous async sweep may have already issued CloseUI() for this
                // window and it hasn't finished (UnregisterSceneBoundWindow runs at the end
                // of CloseUIAsync). Skip to avoid double-close / double-teardown.
                if (!activeWindows.ContainsKey(windowName))
                {
                    UnregisterSceneBoundWindow(window);
                    continue;
                }

                CloseUI(windowName);
            }

            _sceneBoundSweepScratch.Clear();
        }

        private async UniTask WaitForOperationCompletedAsync(IOperation operation, CancellationToken cancellationToken)
        {
            if (operation == null)
            {
                throw new System.ArgumentNullException(nameof(operation));
            }

            while (!operation.IsDone)
            {
                cancellationToken.ThrowIfCancellationRequested();
                await UniTask.Yield(PlayerLoopTiming.Update, cancellationToken);
            }
        }

        private async UniTask ThrottleInstantiate(CancellationToken cancellationToken = default)
        {
            while (instantiatesThisFrame >= maxInstantiatesPerFrame)
            {
                await UniTask.Yield(cancellationToken);
            }
            instantiatesThisFrame++;
        }
    }
}
