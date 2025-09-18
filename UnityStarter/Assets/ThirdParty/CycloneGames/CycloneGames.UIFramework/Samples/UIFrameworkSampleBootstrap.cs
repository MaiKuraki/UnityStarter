using UnityEngine;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service.Runtime;
using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.UIFramework.Runtime.Samples
{
    /// <summary>
    /// Minimal bootstrap demonstrating UIFramework usage with AssetManagement abstraction.
    /// Attach to an empty GameObject in a demo scene that also contains a UIRoot prefab.
    /// </summary>
    public sealed class UIFrameworkSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private string firstWindowName = "UIWindow_SampleUI"; //  If you use Addressable, this is the path in Addressable, and you must implement IAssetPackage

        private UIService uiService;
        private MainCameraService _mainCameraService;
        private UIAssetFactory _uiAssetFactory;

        private async void Start()
        {
            var ok = await InitializeUIServicePipelineAsync();
            if (!ok) return;

            var window = await uiService.OpenUIAndWait(firstWindowName);
            if (window == null)
            {
                Debug.LogError($"[UIFrameworkSample] Failed to open window: {firstWindowName}");
                return;
            }

            Debug.Log($"[UIFrameworkSample] Opened window: {firstWindowName}");
        }
        
        private async System.Threading.Tasks.Task<bool> InitializeUIServicePipelineAsync()
        {
            _uiAssetFactory = new UIAssetFactory();
            _mainCameraService = new MainCameraService();

            var factory = _uiAssetFactory as IAssetPathBuilderFactory;
            var spawner = new DefaultUnityObjectSpawner();
            var mainCameraService = _mainCameraService as IMainCameraService;

            if (AssetManagementLocator.DefaultPackage == null)
            {
                await EnsureDefaultPackageAsync();
                if (AssetManagementLocator.DefaultPackage == null)
                {
                    Debug.LogError("[UIFrameworkSample] DefaultPackage is null. Assign a package via AssetManagementLocator before boot.");
                    return false;
                }
            }

            if (factory == null)
            {
                Debug.LogError("[UIFrameworkSample] AssetPathBuilderFactoryProvider must implement IAssetPathBuilderFactory.");
                return false;
            }

            uiService = new UIService();
            uiService.Initialize(factory, spawner, mainCameraService);
            return true;
        }

        private async System.Threading.Tasks.Task EnsureDefaultPackageAsync()
        {
            try
            {
                // Use the new Resources-based package.
                IAssetModule module = new ResourcesModule();
                module.Initialize(new AssetManagementOptions());
                var pkg = module.CreatePackage("DefaultResources");
                await pkg.InitializeAsync(default);
                AssetManagementLocator.DefaultPackage = pkg;
                Debug.Log("[UIFrameworkSample] DefaultPackage set to ResourcesPackage.");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UIFrameworkSample] Failed to auto-setup Resources package: {ex.Message}");
            }
        }
    }
}
