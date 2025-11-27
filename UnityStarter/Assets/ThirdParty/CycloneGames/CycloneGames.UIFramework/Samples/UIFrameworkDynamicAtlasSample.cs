using CycloneGames.AssetManagement.Runtime;
using CycloneGames.UIFramework.DynamicAtlas;
using Cysharp.Threading.Tasks;
using UnityEngine;
using UnityEngine.UI;

namespace CycloneGames.UIFramework.Samples
{
    /// <summary>
    /// A standalone sample demonstrating Dynamic Atlas integration.
    /// Does not interfere with the main UIFramework pipeline.
    /// </summary>
    public class UIFrameworkDynamicAtlasSample : MonoBehaviour
    {
        [Header("Configuration")]
        public string testIconPath = "svg-spinners--tadpole"; // Ensure this exists in Resources/svg-spinners--tadpole.png
        public Transform uiRoot;

        private IAssetModule _assetModule;
        private IAssetPackage _assetPackage;

        private async void Start()
        {
            await InitializeAssetSystemAsync();

            ConfigureDynamicAtlas();

            CreateVisualizationUI();
        }

        private async UniTask InitializeAssetSystemAsync()
        {
            _assetModule = new ResourcesModule();
            await _assetModule.InitializeAsync();

            _assetPackage = _assetModule.CreatePackage("AtlasSamplePackage");
            await _assetPackage.InitializeAsync(new AssetPackageInitOptions(AssetPlayMode.EditorSimulate, null));

            Debug.Log("[AtlasSample] Asset System Initialized.");
        }

        private void ConfigureDynamicAtlas()
        {
            // Inject our custom loader into the Atlas System
            DynamicAtlasManager.Instance.Configure(
                load: (path) =>
                {
                    // Synchronous load requirement for DynamicAtlas
                    var handle = _assetPackage.LoadAssetSync<Texture2D>(path);
                    if (handle.Asset == null)
                    {
                        Debug.LogError($"[AtlasSample] Failed to load texture: {path}");
                        return null;
                    }
                    // Note: In a real system, you should cache the handle to release it later.
                    return handle.Asset;
                },
                unload: (path, tex) =>
                {
                    // Simple unload for Resources
                    Resources.UnloadAsset(tex);
                }
            );

            Debug.Log("[AtlasSample] Dynamic Atlas Configured.");
        }

        private void CreateVisualizationUI()
        {
            if (uiRoot == null)
            {
                var canvasGO = new GameObject("AtlasSampleCanvas");
                var canvas = canvasGO.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.ScreenSpaceOverlay;
                canvasGO.AddComponent<CanvasScaler>();
                canvasGO.AddComponent<GraphicRaycaster>();
                uiRoot = canvasGO.transform;
            }

            // Create Image
            var imgObj = new GameObject("AtlasSprite");
            imgObj.transform.SetParent(uiRoot, false);
            imgObj.transform.localPosition = new Vector3(0, -300, 0);
            var img = imgObj.AddComponent<Image>();
            img.rectTransform.sizeDelta = new Vector2(128, 128);

            // LOAD FROM ATLAS
            Sprite sprite = DynamicAtlasManager.Instance.GetSprite(testIconPath);

            if (sprite != null)
            {
                img.sprite = sprite;
                Debug.Log($"[AtlasSample] Sprite assigned from Atlas: {sprite.name}");
            }
            else
            {
                Debug.LogWarning($"[AtlasSample] Sprite not found at path: {testIconPath}. Check if file exists in Resources folder.");
            }
        }

        private void OnDestroy()
        {
            _assetModule?.Destroy();
        }
    }
}