using UnityEngine;
using CycloneGames.Factory.Runtime;
using CycloneGames.Service;
using CycloneGames.AssetManagement;
using CycloneGames.AssetManagement.Integrations.Common;

namespace CycloneGames.UIFramework.Samples
{
    /// <summary>
    /// Minimal bootstrap demonstrating UIFramework usage with AssetManagement abstraction.
    /// Attach to an empty GameObject in a demo scene that also contains a UIRoot prefab.
    /// </summary>
    public sealed class UIFrameworkSampleBootstrap : MonoBehaviour
    {
        [SerializeField] private string firstWindowName = "SampleWindow";
        [SerializeField] private bool autoSetupAddressablesPackage = true; // Try set DefaultPackage if missing

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
                if (autoSetupAddressablesPackage)
                {
                    await EnsureDefaultPackageAsync();
                }
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
                // Minimal Addressables-based package from Samples. Replace with your adapter as needed.
                var pkg = new AddressablesPackage();
                await pkg.InitializeAsync(default);
                AssetManagementLocator.DefaultPackage = pkg;
                Debug.Log("[UIFrameworkSample] DefaultPackage set to AddressablesPackage (Samples).");
            }
            catch (System.Exception ex)
            {
                Debug.LogWarning($"[UIFrameworkSample] Failed to auto-setup Addressables package: {ex.Message}");
            }
        }
    }
}


