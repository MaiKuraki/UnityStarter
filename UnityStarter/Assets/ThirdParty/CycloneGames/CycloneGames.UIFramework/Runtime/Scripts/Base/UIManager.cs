using System.Collections.Generic;
using Cysharp.Threading.Tasks;
using UnityEngine;
using CycloneGames.Logger;
using CycloneGames.Service;
using UnityEngine.AddressableAssets;
using Addler.Runtime.Core.LifetimeBinding;
using CycloneGames.Factory;

namespace CycloneGames.UIFramework
{
    public class UIManager : MonoBehaviour
    {
        private const string DEBUG_FLAG = "[UIManager]";
        private IAssetPathBuilder assetPathBuilder;
        private IUnityObjectSpawner objectSpawner;
        private IMainCameraService mainCamera;
        private UIRoot uiRoot;
        private Dictionary<string, UniTaskCompletionSource<bool>> uiOpenTasks = new Dictionary<string, UniTaskCompletionSource<bool>>();

        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IUnityObjectSpawner objectSpawner, IMainCameraService mainCamera)
        {
            this.assetPathBuilder = assetPathBuilderFactory.Create("UI");
            if (this.assetPathBuilder == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Invalid AssetPathBuilder, Check your [AssetPathBuilderFactory], make sure it contains 'UI' key.");
                return;
            }
            this.objectSpawner = objectSpawner;
            this.mainCamera = mainCamera;
        }

        private void Awake()
        {
            uiRoot = GameObject.FindFirstObjectByType<UIRoot>();
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
            var pageHandle = Addressables.LoadAssetAsync<UIPageConfiguration>(configPath);
            try
            {
                await pageHandle.Task;
                pageConfig = pageHandle.Result;

                if (pageConfig == null || pageConfig.PagePrefab == null)
                {
                    CLogger.LogError($"{DEBUG_FLAG} Invalid UI Prefab in PageConfig, PageName: {PageName}");
                    uiOpenTasks.Remove(PageName);
                    pageHandle.Release();
                    return;
                }
            }
            catch (System.Exception ex)
            {
                CLogger.LogError($"{DEBUG_FLAG} An exception occurred while loading the UI: {PageName}: {ex.Message}");
                uiOpenTasks.Remove(PageName);
                pageHandle.Release();
                return;
            }

            string layerName = pageConfig.Layer.LayerName;
            UILayer uiLayer = uiRoot.GetUILayer(layerName);
            if (uiLayer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} UILayer not found: {layerName}");
                uiOpenTasks.Remove(PageName);
                pageHandle.Release();
                return;
            }

            if (uiLayer.HasPage(PageName))
            {
                // Please note that within this framework, the opening of a UIPage must be unique;
                // that is, UI pages similar to Notifications should be managed within the page itself and should not be opened repeatedly for the same UI page.
                CLogger.LogError($"{DEBUG_FLAG} Page already exists: {PageName}, layer: {uiLayer.LayerName}");
                uiOpenTasks.Remove(PageName);
                pageHandle.Release();
                return;
            }

            UIPage uiPage = objectSpawner.Create(pageConfig.PagePrefab) as UIPage;
            if (uiPage == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Failed to instantiate UIPage prefab: {PageName}");
                uiOpenTasks.Remove(PageName);
                pageHandle.Release();
                return;
            }
            await pageHandle.BindTo(uiPage.gameObject);
            uiPage.SetPageName(PageName);
            uiLayer.AddPage(uiPage);
            OnPageCreated?.Invoke(uiPage);

            tcs.TrySetResult(true);
        }

        async UniTask CloseUIAsync(string PageName)
        {
            if (uiOpenTasks.TryGetValue(PageName, out var openTask))
            {
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
                }
                return;
            }

            layer.RemovePage(PageName);
        }

        internal bool IsUIPageValid(string PageName)
        {
            UILayer layer = uiRoot.TryGetUILayerFromPageName(PageName);
            if (layer == null)
            {
                CLogger.LogError($"{DEBUG_FLAG} Can not find layer from PageName: {PageName}");
                return false;
            }

            // If the page doesn't exist or isn't active, it's not valid.
            return layer.HasPage(PageName);
        }

        internal UIPage GetUIPage(string PageName)
        {
            UILayer layer = uiRoot.TryGetUILayerFromPageName(PageName);
            if (layer == null)
            {
                return null;
            }
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