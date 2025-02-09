using System;
using System.Collections;
using System.IO;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.Settings;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Utility;
using UnityEditor;
using UnityEngine;
using UnityEngine.UIElements;
using Environment = System.Environment;
using Object = UnityEngine.Object;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    static class Icons
    {

        public const string UpdateNotNeeded = "";
        public const string UpdateAvailable = "";
        public const string UpdateIsChecking = "";
        public const string UpdateIsTooOutOfDate = "X";

        public const string DebugEnabled = "";
        public const string DebugDisabled = "";

    }

    static class Urls
    {
        public const string AssetStoreReview = "https://assetstore.unity.com/packages/tools/utilities/advanced-scene-manager-174152#reviews";
        public const string GithubIssue = "https://github.com/Lazy-Solutions/AdvancedSceneManager/issues/new";
        public const string Discord = "https://discord.gg/pnRn6zeFEJ";
        public const string GithubDocs = "https://github.com/Lazy-Solutions/AdvancedSceneManager/tree/main/docs";
        public const string Examples = "https://github.com/Lazy-Solutions/AdvancedSceneManager/blob/main/examples.md";
    }

    class MenuPopup : ExtendableViewModel, IPopup
    {

        public override bool IsExtendableButtonsEnabled => true;
        public override VisualElement ExtendableButtonContainer => view?.Q("extendable-button-container");
        public override ElementLocation Location => ElementLocation.Header;
        public override bool IsOverflow => true;

        public override void OnAdded()
        {

            base.OnAdded();

            SetupHeader();
            InitializeBuild();
            InitializeDocs();
            InitializeReview();

            view.Q<ScrollView>().PersistScrollPosition();

        }

        public override void OnRemoved()
        {
            view.Q<ScrollView>().ClearScrollPosition();
        }

        ASMUserSettings settings => SceneManager.settings.user;

        #region Header

        void SetupHeader()
        {
            view.Q<Label>("text-version").text = SceneManager.package.version;
            view.Q<Button>("button-update").clicked += OpenSettings<UpdatesPage>;
            view.Q<Button>("button-close").clicked += ClosePopup;
        }

        #endregion
        #region Parameters

        public static object flashDocsSection { get; } = new();

        public override void PassParameter(object parameter)
        {
            if (parameter == flashDocsSection)
                FlashDocs();
        }

        void FlashDocs() =>
            FlashBorderColor(view.Q("section-docs"), Color.clear, Color.white, 500).StartCoroutine();

        private IEnumerator FlashBorderColor(VisualElement element, Color color1, Color color2, float duration)
        {

            SceneManagerWindow.window.wantsConstantRepaint = true;
            yield return new WaitForSeconds(0.25f);

            yield return AnimateBorderColor(element, color1, color2, duration);
            yield return new WaitForSeconds(0.25f);
            yield return AnimateBorderColor(element, color2, color1, duration);
            yield return new WaitForSeconds(0.25f);

            SceneManagerWindow.window.wantsConstantRepaint = false;

        }

        IEnumerator AnimateBorderColor(VisualElement element, Color color1, Color color2, float duration)
        {

            var elapsedTime = 0f;

            while (elapsedTime < duration)
            {

                elapsedTime += Time.deltaTime;
                var t = elapsedTime / duration;
                var easedT = EaseOutExpo(t);
                var newColor = Color.Lerp(color1, color2, easedT);

                SetBorderColor(element, newColor);

                yield return null;

            }

            SetBorderColor(element, color2);

        }

        void SetBorderColor(VisualElement element, Color color)
        {
            element.style.borderTopColor = color;
            element.style.borderBottomColor = color;
            element.style.borderLeftColor = color;
            element.style.borderRightColor = color;
        }

        private float EaseOutExpo(float t)
        {
            return t == 1 ? 1 : 1 - Mathf.Pow(2, -10 * t);
        }

        #endregion
        #region Build

        void InitializeBuild()
        {

            InitializeFolderButton();
            InitializeProfilerButton();

            view.Q<Button>("button-build").clicked += DoDevBuild;

        }

        public void DoDevBuild()
        {
            var path = GetBuildPath().Replace("%temp%", Path.GetTempPath());
            BuildUtility.DoBuild(path + "/app.exe", attachProfiler: settings.quickBuildUseProfiler, true);
        }

        string GetBuildPath() =>
            string.IsNullOrEmpty(settings.quickBuildPath)
            ? Path.Combine("%temp%", "ASM", "builds", Application.companyName, Application.productName)
            : settings.quickBuildPath;

        void InitializeFolderButton()
        {

            var folderButton = view.Q<Button>("button-build-folder");
            folderButton.clicked += () =>
            {

                var path = EditorUtility.OpenFolderPanel("Select folder to put builds in...", Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory), Application.productName);
                settings.quickBuildPath = Directory.Exists(path) ? path : null;

                UpdateFolderButton();

            };

            UpdateFolderButton();
            void UpdateFolderButton() =>
                folderButton.tooltip = GetBuildPath();
        }

        void InitializeProfilerButton()
        {

            var profilerButton = view.Q<Button>("button-build-profiler");

            profilerButton.clicked += () =>
            {
                settings.quickBuildUseProfiler = !settings.quickBuildUseProfiler;
                UpdateProfilerButton();
            };

            UpdateProfilerButton();
            void UpdateProfilerButton()
            {
                profilerButton.text = settings.quickBuildUseProfiler ? Icons.DebugEnabled : Icons.DebugDisabled;
                profilerButton.tooltip = settings.quickBuildUseProfiler ? "Profiler enabled. Press to disable." : "Profiler disabled. Press to enable.";
            }

        }

        #endregion
        #region Docs

        void InitializeDocs()
        {

            SetupLink(view.Q<Button>("button-docs-online"), Urls.GithubDocs, HideNotification);

            var obj = FindLocalDocs();
            var folder = $"{ProjectWindowUtil.GetContainingFolder(AssetDatabase.GetAssetPath(obj))}/";

            var button = view.Q<Button>("button-docs-local");
            SetupTooltip(button, folder);

            button.clicked += () =>
            {
                HideNotification();
                EditorGUIUtility.PingObject(obj);
            };

            void HideNotification()
            {

                SceneManager.settings.user.m_hideDocsNotification = true;
                SceneManager.settings.user.Save();

                notifications.ReloadPersistentNotifications();

            }

        }

        Object FindLocalDocs()
        {
            var path = $"{SceneManager.package.folder}/Documentation/_api-reference.pdf";
            return AssetDatabase.LoadAssetAtPath<Object>(path);
        }

        #endregion
        #region Review

        void InitializeReview()
        {
            SetupLink(view.Q<Button>("button-asset-store"), Urls.AssetStoreReview);
            SetupLink(view.Q<Button>("button-github"), Urls.GithubIssue);
            SetupLink(view.Q<Button>("button-discord"), Urls.Discord);
        }

        #endregion

        void SetupLink(Button button, string url, Action onClicked = null)
        {

            button.clicked += () =>
            {
                Application.OpenURL(url);
                onClicked?.Invoke();
            };

            SetupTooltip(button, url);

        }

        void SetupTooltip(Button button, string url)
        {
            if (EditorGUIUtility.isProSkin)
                button.tooltip += "\n\n" + $"<color=#00ffffff>{url}</color>";
            else
                button.tooltip += "\n\n" + $"<color=#00008B>{url}</color>";
        }
    }

}
