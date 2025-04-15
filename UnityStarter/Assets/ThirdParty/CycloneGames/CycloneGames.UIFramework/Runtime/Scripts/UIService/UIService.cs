using System;
using CycloneGames.Factory;
using CycloneGames.Service;
using UnityEngine;

namespace CycloneGames.UIFramework
{
    public interface IUIService
    {
        void OpenUI(string PageName, System.Action<UIPage> OnPageCreated = null);
        void CloseUI(string PageName);
        bool IsUIPageValid(string PageName);
        UIPage GetUIPage(string PageName);
    }
    public class UIService : IDisposable, IUIService
    {
        private const string DEBUG_FLAG = "[UIService]";
        private UIManager uiManager;

        private readonly IAssetPathBuilderFactory assetPathBuilderFactory;
        private readonly IFactory<MonoBehaviour, MonoBehaviour> objectSpawner;
        private readonly IMainCameraService mainCamera;

        public UIService() { }

        public UIService(IAssetPathBuilderFactory assetPathBuilderFactory, IFactory<MonoBehaviour, MonoBehaviour> objectSpawner, IMainCameraService mainCamera)
        {
            this.assetPathBuilderFactory = assetPathBuilderFactory;
            this.objectSpawner = objectSpawner;
            this.mainCamera = mainCamera;

            Initialize(assetPathBuilderFactory, objectSpawner, mainCamera);
        }

        public void Initialize(IAssetPathBuilderFactory assetPathBuilderFactory, IFactory<MonoBehaviour, MonoBehaviour> objectSpawner, IMainCameraService mainCamera)
        {
            uiManager = new UnityEngine.GameObject("UIManager").AddComponent<UIManager>(); //   TODO: maybe use objectSpawner to create this object
            UnityEngine.MonoBehaviour.DontDestroyOnLoad(uiManager.gameObject);
            uiManager.Initialize(assetPathBuilderFactory, objectSpawner, mainCamera);
        }

        public bool IsUIPageValid(string PageName)
        {
            return uiManager.IsUIPageValid(PageName);
        }

        public void OpenUI(string PageName, Action<UIPage> OnPageCreated = null)
        {
            if (uiManager == null)
            {
                UnityEngine.Debug.Log($"{DEBUG_FLAG} Invalid UIManager");
            }

            uiManager.OpenUI(PageName, OnPageCreated);
        }
        public void CloseUI(string PageName)
        {
            if (uiManager == null)
            {
                UnityEngine.Debug.Log($"{DEBUG_FLAG} Invalid UIManager");
            }

            uiManager.CloseUI(PageName);
        }

        public UIPage GetUIPage(string PageName)
        {
            return uiManager.GetUIPage(PageName);
        }

        public void AddUICameraToMainCameraStack()
        {
            if (uiManager == null)
            {
                UnityEngine.Debug.Log($"{DEBUG_FLAG} Invalid UIManager");
            }

            uiManager.AddUICameraToMainCameraStack();
        }

        public void RemoveUICameraFromMainCameraStack()
        {
            if (uiManager == null)
            {
                UnityEngine.Debug.Log($"{DEBUG_FLAG} Invalid UIManager");
            }

            uiManager.RemoveUICameraFromMainCameraStack();
        }

        public void Dispose()
        {
            if (uiManager != null)
            {
                UnityEngine.GameObject.Destroy(uiManager.gameObject);
                uiManager = null;
            }
        }
    }
}