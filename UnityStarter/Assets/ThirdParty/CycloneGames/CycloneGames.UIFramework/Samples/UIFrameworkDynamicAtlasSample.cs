using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using Cysharp.Threading.Tasks;
using CycloneGames.AssetManagement.Runtime;
using CycloneGames.UIFramework.DynamicAtlas;

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
        public string testIconPath2 = "line-md--github"; // line-md--github.png
        public Transform uiRoot;

        private IAssetModule _assetModule;
        private IAssetPackage _assetPackage;
        private readonly Dictionary<string, IAssetHandle<Texture2D>> _loadedTextureHandles =
            new Dictionary<string, IAssetHandle<Texture2D>>(8);

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
                    if (_loadedTextureHandles.TryGetValue(path, out var cachedHandle))
                    {
                        return cachedHandle.Asset;
                    }

                    // Synchronous load requirement for DynamicAtlas
                    var handle = _assetPackage.LoadAssetSync<Texture2D>(path);
                    if (handle.Asset == null)
                    {
                        handle.Dispose();
                        Debug.LogError($"[AtlasSample] Failed to load texture: {path}");
                        return null;
                    }

                    _loadedTextureHandles[path] = handle;
                    return handle.Asset;
                },
                unload: (path, tex) =>
                {
                    if (_loadedTextureHandles.Remove(path, out var handle))
                    {
                        handle.Dispose();
                    }

                    if (tex != null)
                    {
                        Resources.UnloadAsset(tex);
                    }
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
            imgObj.transform.localPosition = new Vector3(-100, -300, 0);
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

            var imgObj2 = new GameObject("AtlasSprite2");
            imgObj2.transform.SetParent(uiRoot, false);
            imgObj2.transform.localPosition = new Vector3(100, -300, 0);
            var img2 = imgObj2.AddComponent<Image>();
            img2.rectTransform.sizeDelta = new Vector2(128, 128);

            Sprite sprite2 = DynamicAtlasManager.Instance.GetSprite(testIconPath2);

            if (sprite2 != null)
            {
                img2.sprite = sprite2;
                Debug.Log($"[AtlasSample] Sprite assigned from Atlas: {sprite2.name}");
            }
            else
            {
                Debug.LogWarning($"[AtlasSample] Sprite not found at path: {testIconPath2}. Check if file exists in Resources folder.");
            }
        }

        private void OnDestroy()
        {
            ReleaseLoadedTextureHandles();

            if (_assetModule != null)
            {
                _assetModule.DestroyAsync().Forget();
                _assetModule = null;
            }

            _assetPackage = null;
        }

        private void ReleaseLoadedTextureHandles()
        {
            foreach (var handle in _loadedTextureHandles.Values)
            {
                handle.Dispose();
            }

            _loadedTextureHandles.Clear();
        }
    }
}
