using System;
using Cysharp.Threading.Tasks;
using CycloneGames.Factory.Runtime;             // For IUnityObjectSpawner
using CycloneGames.Service.Runtime;             // For IMainCameraService
using CycloneGames.AssetManagement.Runtime;     // For IAssetPathBuilderFactory

namespace CycloneGames.UIFramework.Runtime
{
    public interface IUIService
    {
        /// <summary>
        /// Opens a UI by its registered name.
        /// </summary>
        /// <param name="windowName">The name of the UI window to open.</param>
        /// <param name="onWindowCreated">Optional callback invoked when the window is created.</param>
        void OpenUI(string windowName, System.Action<UIWindow> onWindowCreated = null);
        UniTask<UIWindow> OpenUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Closes a UI by its registered name.
        /// </summary>
        /// <param name="windowName">The name of the UI window to close.</param>
        void CloseUI(string windowName);
        UniTask CloseUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default);

        /// <summary>
        /// Checks if a UI window is currently considered valid (e.g., open and active).
        /// </summary>
        /// <param name="windowName">The name of the UI window.</param>
        /// <returns>True if the window is valid, false otherwise.</returns>
        bool IsUIWindowValid(string windowName);

        /// <summary>
        /// Gets a reference to an open UI window by its name.
        /// </summary>
        /// <param name="windowName">The name of the UI window.</param>
        /// <returns>The UIWindow instance if found and active, otherwise null.</returns>
        UIWindow GetUIWindow(string windowName); // Renamed from GetUIPage for consistency

        // Optional: Methods to manage UI camera stacking if not handled internally by UIManager
        // void AddUICameraToMainCameraStack();
        // void RemoveUICameraFromMainCameraStack();

        (float, float) GetRootCanvasSize();

        void Initialize(IAssetPathBuilderFactory factory, IUnityObjectSpawner spawner, IMainCameraService cameraService);
        void Initialize(IAssetPathBuilderFactory factory, IUnityObjectSpawner spawner, IMainCameraService cameraService, IAssetPackage package);
    }

    public class UIService : IDisposable, IUIService
    {
        private const string DEBUG_FLAG = "[UIService]";
        private UIManager uiManagerInstance;

        // Dependencies are injected via constructor
        private IAssetPathBuilderFactory assetPathBuilderFactory;
        private IUnityObjectSpawner objectSpawner;
        private IMainCameraService mainCameraService;

        private bool isInitialized = false;

        // Default constructor might be used if service locator pattern is used elsewhere to provide dependencies later.
        // However, constructor injection is generally preferred for clarity of dependencies.
        public UIService()
        {
            //  You must Initialize UIService
            isInitialized = false;
        }

        /// <summary>
        /// Initializes the UIService for projects using Unity's built-in Resources.Load for asset management.
        /// This method does not require an explicit IAssetPackage parameter, as it will use the default package
        /// from AssetManagementLocator (typically a ResourcesAssetPackage implementation).
        /// 
        /// Use this method when:
        /// - Your project uses Unity's Resources.Load to manage UI assets
        /// - You don't need hot-update capabilities for UI assets
        /// - All UI resources are bundled with the application at build time
        /// 
        /// Note: If your project uses AssetBundle-based systems (Addressables, YooAsset, etc.) for hot-update
        /// capabilities, use the overload that accepts an IAssetPackage parameter instead.
        /// </summary>
        /// <param name="factory">Factory for creating asset path builders to resolve UI asset paths.</param>
        /// <param name="spawner">Service for instantiating UI prefabs with optional pooling support.</param>
        /// <param name="cameraService">Service for managing main camera and UI camera stacking (can be null).</param>
        /// <exception cref="ArgumentNullException">Thrown when factory or spawner is null.</exception>
        public virtual void Initialize(IAssetPathBuilderFactory factory, IUnityObjectSpawner spawner, IMainCameraService cameraService)
        {
            if (isInitialized)
            {
                UnityEngine.Debug.LogWarning($"{DEBUG_FLAG} UIService already initialized. Operation aborted.");
                return;
            }
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (spawner == null) throw new ArgumentNullException(nameof(spawner));
            // cameraService can be optional depending on requirements
            // if (cameraService == null) throw new ArgumentNullException(nameof(cameraService));

            this.assetPathBuilderFactory = factory;
            this.objectSpawner = spawner;
            this.mainCameraService = cameraService;

            InitializeUIManager(null);
            isInitialized = true;
        }

        /// <summary>
        /// Initializes the UIService for projects using AssetBundle-based asset management systems (Addressables, YooAsset, etc.)
        /// that support hot-update capabilities.
        /// 
        /// Use this method when:
        /// - Your project uses AssetBundle-based systems for asset management
        /// - You need hot-update capabilities for UI assets
        /// - UI resources are loaded from remote servers or downloaded dynamically
        /// 
        /// Important Design Limitation:
        /// Currently, the UIService supports only a single IAssetPackage instance. This means all UI resources
        /// (including UIWindowConfiguration assets and UI prefabs) must be managed within the same package.
        /// If you have multiple packages in your project, ensure all UI-related assets are organized within
        /// the single package passed to this method.
        /// 
        /// The package is used by UIManager to:
        /// - Load UIWindowConfiguration ScriptableObject assets
        /// - Load UI prefab GameObjects when using PrefabSource.Location mode
        /// - Manage asset lifecycle and handle disposal
        /// </summary>
        /// <param name="factory">Factory for creating asset path builders to resolve UI asset paths.</param>
        /// <param name="spawner">Service for instantiating UI prefabs with optional pooling support.</param>
        /// <param name="cameraService">Service for managing main camera and UI camera stacking (can be null).</param>
        /// <param name="package">The asset package that contains all UI resources. Must not be null.</param>
        /// <exception cref="ArgumentNullException">Thrown when factory, spawner, or package is null.</exception>
        public virtual void Initialize(IAssetPathBuilderFactory factory, IUnityObjectSpawner spawner, IMainCameraService cameraService, IAssetPackage package)
        {
            if (isInitialized)
            {
                UnityEngine.Debug.LogWarning($"{DEBUG_FLAG} UIService already initialized. Operation aborted.");
                return;
            }
            if (factory == null) throw new ArgumentNullException(nameof(factory));
            if (spawner == null) throw new ArgumentNullException(nameof(spawner));
            if (package == null) throw new ArgumentNullException(nameof(package));

            this.assetPathBuilderFactory = factory;
            this.objectSpawner = spawner;
            this.mainCameraService = cameraService;

            InitializeUIManager(package);
            isInitialized = true;
        }

        // This method could also be an explicit Init if dependencies aren't constructor-injected.
        private void InitializeUIManager(IAssetPackage package)
        {
            // Try to find an existing UIManager in the scene.
            uiManagerInstance = UnityEngine.GameObject.FindFirstObjectByType<UIManager>();

            if (uiManagerInstance == null)
            {
                // If not found, create one. This UIManager GameObject should persist.
                UnityEngine.GameObject managerObject = new UnityEngine.GameObject("UIManager_RuntimeInstance");
                uiManagerInstance = managerObject.AddComponent<UIManager>();
                UnityEngine.Object.DontDestroyOnLoad(managerObject); // Make it persist across scene loads
                UnityEngine.Debug.Log($"{DEBUG_FLAG} UIManager instance created and marked DontDestroyOnLoad.");
            }
            else
            {
                UnityEngine.Debug.Log($"{DEBUG_FLAG} Found existing UIManager instance in the scene.");
            }

            // Initialize the UIManager instance with the provided dependencies.
            var pkg = package ?? AssetManagementLocator.DefaultPackage;
            uiManagerInstance.Initialize(assetPathBuilderFactory, objectSpawner, mainCameraService, pkg);
        }

        private bool CheckInitialization()
        {
            if (!isInitialized || uiManagerInstance == null)
            {
                UnityEngine.Debug.LogError($"{DEBUG_FLAG} UIService or UIManager is not initialized. Operation aborted.");
                return false;
            }
            return true;
        }

        public bool IsUIWindowValid(string windowName)
        {
            if (!CheckInitialization()) return false;
            return uiManagerInstance.IsUIWindowValid(windowName);
        }

        public void OpenUI(string windowName, Action<UIWindow> onWindowCreated = null)
        {
            if (!CheckInitialization())
            {
                onWindowCreated?.Invoke(null); // Notify failure
                return;
            }
            uiManagerInstance.OpenUI(windowName, onWindowCreated);
        }

        public UniTask<UIWindow> OpenUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default)
        {
            if (!CheckInitialization()) return UniTask.FromResult<UIWindow>(null);
            return uiManagerInstance.OpenUIAndWait(windowName, cancellationToken);
        }

        public void CloseUI(string windowName)
        {
            if (!CheckInitialization()) return;
            uiManagerInstance.CloseUI(windowName);
        }

        public async UniTask CloseUIAsync(string windowName, System.Threading.CancellationToken cancellationToken = default)
        {
            if (!CheckInitialization()) return;
            await uiManagerInstance.CloseUIAsync(windowName, cancellationToken);
        }

        /// <summary>
        /// Optionally open and await until the window reports Opened (strict sequencing use-cases).
        /// </summary>
        public UniTask<UIWindow> OpenUIAndWait(string windowName, System.Threading.CancellationToken cancellationToken = default)
        {
            if (!CheckInitialization()) return UniTask.FromResult<UIWindow>(null);
            // This method is now just a wrapper for OpenUIAsync.
            // The original implementation with UniTaskCompletionSource is redundant if OpenUIAsync is already awaitable.
            return uiManagerInstance.OpenUIAndWait(windowName, cancellationToken);
        }

        public UIWindow GetUIWindow(string windowName)
        {
            if (!CheckInitialization()) return null;
            return uiManagerInstance.GetUIWindow(windowName);
        }

        // These methods are wrappers if UIManager provides them.
        // If UIService should have its own logic, implement here.
        public void AddUICameraToMainCameraStack()
        {
            if (!CheckInitialization()) return;
            uiManagerInstance.AddUICameraToMainCameraStack();
        }

        public void RemoveUICameraFromMainCameraStack()
        {
            if (!CheckInitialization()) return;
            uiManagerInstance.RemoveUICameraFromMainCameraStack();
        }

        public void Dispose()
        {
            UnityEngine.Debug.Log($"{DEBUG_FLAG} Disposing UIService.");
            if (uiManagerInstance != null)
            {
                // Decide if UIService disposing should destroy the UIManager GameObject.
                // If UIManager is a persistent singleton, maybe not.
                // If UIManager is tied to this UIService instance's lifetime, then yes.
                // UnityEngine.Object.Destroy(uiManagerInstance.gameObject);
                uiManagerInstance = null;
            }
            isInitialized = false;
        }

        public (float, float) GetRootCanvasSize()
        {
            if (!CheckInitialization())
            {
                UnityEngine.Debug.LogError($"{DEBUG_FLAG} UIService is not initialized. Operation aborted.");
                return (0, 0);
            }
            return uiManagerInstance.GetRootCanvasSize();
        }
    }
}
