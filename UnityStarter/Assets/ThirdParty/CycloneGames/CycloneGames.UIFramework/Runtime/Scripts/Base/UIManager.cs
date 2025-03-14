using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logger;
using CycloneGames.Core;
using CycloneGames.Service;
using Object = UnityEngine.Object;

namespace CycloneGames.UIFramework
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        private IAssetPathBuilder assetPathBuilder;
        private IAssetLoader assetLoader;
        private IObjectSpawner objectSpawner;
        private IMainCameraService mainCamera;
        private UIRoot uiRoot;
        private Dictionary<string, UniTaskCompletionSource<bool>> uiOpenTasks = new Dictionary<string, UniTaskCompletionSource<bool>>();
        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IAssetLoader assetLoader, IObjectSpawner objectSpawner, IMainCameraService mainCamera)
        {
            this.assetPathBuilder = assetPathBuilderFactory.Create("UI");   // TODO: maybe there is a better way implement this
            if (this.assetPathBuilder == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid AssetPathBuilder, Check your [AssetPathBuilderFactory], make sure it contains 'UI' key.");
                return;
            }
            this.assetLoader = assetLoader;
            this.objectSpawner = objectSpawner;
            this.mainCamera = mainCamera;
        }

        private void Awake()
        {
            uiRoot = GameObject.FindFirstObjectByType<UIRoot>();

            // UnityEngine.Debug.Log($"{DEBUG_FLAG} UIRootValid: {uiRoot != null}");
        }

        private void Start()
        {
            AddUICameraToMainCameraStack();
        }

        internal void OpenUI(string PageName, System.Action<UIPage> OnPageCreated = null)
        {
            OpenUIAsync(PageName, OnPageCreated).Forget();
        }

        internal void CloseUI(string PageName)
        {
            CloseUIAsync(PageName).Forget();
        }

        async UniTask OpenUIAsync(string PageName, System.Action<UIPage> OnPageCreated = null)
        {
            // Avoid duplicated open same UI
            if (uiOpenTasks.ContainsKey(PageName))
            {
                CLogger.LogError($"{DEBUG_FLAG} Duplicated Open! PageName: {PageName}");
                return;
            }
            var tcs = new UniTaskCompletionSource<bool>();
            uiOpenTasks[PageName] = tcs;

            CLogger.LogInfo($"{DEBUG_FLAG} Attempting to open UI: {PageName}");
            string configPath = assetPathBuilder.GetAssetPath(PageName);
            UIPageConfiguration pageConfig = null;
            Object pagePrefab = null;

            try
            {
                // Attempt to load the configuration
                pageConfig = await assetLoader.LoadAssetAsync<UIPageConfiguration>(configPath);

                // If the configuration load fails, log the error and exit
                if (pageConfig == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Failed to load UI Config, PageName: {PageName}");
                    uiOpenTasks.Remove(PageName);
                    return;
                }

                // Attempt to load the Prefab
                pagePrefab = pageConfig.PagePrefab;

                // If the Prefab load fails, log the error and exit
                if (pagePrefab == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Invalid UI Prefab in PageConfig, PageName: {PageName}");
                    uiOpenTasks.Remove(PageName);
                    return;
                }
            }
            catch (System.Exception ex)
            {
                // Catch any exceptions, log the error message
                CLogger.LogError($"{DEBUG_FLAG} An exception occurred while loading the UI: {PageName}: {ex.Message}");
                // Perform any necessary cleanup here
                uiOpenTasks.Remove(PageName);
                return; // Handle the exception here instead of re-throwing it
            }

            // If there are no exceptions and the resources have been successfully loaded, proceed to instantiate and setup the UI page
            string layerName = pageConfig.Layer.LayerName;
            UILayer uiLayer = uiRoot.GetUILayer(layerName);
            if (uiLayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayer not found: {layerName}");
                uiOpenTasks.Remove(PageName);
                return;
            }

            // UnityEngine.Debug.Log($"{DEBUG_FLAG} uiLayerValid: {uiLayer != null}, layerName: {layerName}");
            if (uiLayer.HasPage(PageName))
            {
                // Please note that within this framework, the opening of a UIPage must be unique;
                // that is, UI pages similar to Notifications should be managed within the page itself and should not be opened repeatedly for the same UI page.
                CLogger.LogError($"{DEBUG_FLAG} Page already exists: {PageName}, layer: {uiLayer.LayerName}");
                uiOpenTasks.Remove(PageName);
                return;
            }

            UIPage uiPage = objectSpawner.SpawnObject(pagePrefab) as UIPage;
            if (uiPage == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to instantiate UIPage prefab: {PageName}");
                uiOpenTasks.Remove(PageName);
                return;
            }

            uiPage.SetPageName(PageName);
            uiLayer.AddPage(uiPage);
            OnPageCreated?.Invoke(uiPage);

            tcs.TrySetResult(true);
        }

        async UniTask CloseUIAsync(string PageName)
        {
            if (uiOpenTasks.TryGetValue(PageName, out var openTask))
            {
                // Waiting Open Task Finished
                await openTask.Task;
                uiOpenTasks.Remove(PageName);
            }

            string preReleaseConfigPath = assetPathBuilder.GetAssetPath(PageName);

            UILayer layer = uiRoot?.TryGetUILayerFromPageName(PageName);
            if (layer == null)
            {
                UIPage preRemovePage = GetUIPage(PageName);
                if (preRemovePage != null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Layer not found, but page exists: {PageName}");
                    preRemovePage.ClosePage();
                    assetLoader.ReleaseAssetHandle(preReleaseConfigPath);
                }
                return;
            }

            layer.RemovePage(PageName);
            assetLoader.ReleaseAssetHandle(preReleaseConfigPath);
        }

        internal bool IsUIPageValid(string PageName)
        {
            // Check if the UI Root has a layer containing the page with the given name.
            UILayer layer = uiRoot.TryGetUILayerFromPageName(PageName);
            if (layer == null)
            {
                // If the layer doesn't exist, the page is not valid.
                CLogger.LogError($"{DEBUG_FLAG} Can not find layer from PageName: {PageName}");
                return false;
            }

            // If the page doesn't exist or isn't active, it's not valid.
            return layer.HasPage(PageName);
        }

        internal UIPage GetUIPage(string PageName)
        {
            // Check if the UI Root has a layer containing the page with the given name.
            UILayer layer = uiRoot.TryGetUILayerFromPageName(PageName);
            if (layer == null)
            {
                // If the layer doesn't exist, the page is not valid.
                // Debug.LogError($"{DEBUG_FLAG} Can not find layer from PageName: {PageName}");
                return null;
            }

            // If the page doesn't exist or isn't active, it's not valid.
            return layer.GetUIPage(PageName);
        }

        public void AddUICameraToMainCameraStack()
        {
            mainCamera?.AddCameraToStack(uiRoot.UICamera);
        }

        public void RemoveUICameraFromMainCameraStack()
        {
            mainCamera?.RemoveCameraFromStack(uiRoot.UICamera);
        }
    }
}