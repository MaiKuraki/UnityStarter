using System;
using AdvancedSceneManager.DependencyInjection;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Layouts;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.Popups;
using UnityEditor;
using UnityEditor.Search;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views
{

    class HeaderView : ExtendableViewModel, IView
    {

        public override bool IsExtendableButtonsEnabled => true;
        public override ElementLocation Location => ElementLocation.Header;
        public override VisualElement ExtendableButtonContainer => view.Q("extendable-button-container");
        public override bool IsOverflow => false;

        public override void OnAdded()
        {
            base.OnAdded();
            view.Q<Button>("button-menu").clicked += OpenPopup<MenuPopup>;
            SetupPlayButton(view);
            SetupSettingsButton(view);
        }

        void SetupPlayButton(VisualElement element)
        {

            var button = element.Q<Button>("button-play");
            button.clickable.activators.Add(new() { button = MouseButton.LeftMouse, modifiers = UnityEngine.EventModifiers.Shift });
            element.Q<Button>("button-play").clickable.clickedWithEventInfo += (e) =>
                SceneManager.app.Restart(new() { forceOpenAllScenesOnCollection = e.IsShiftKeyHeld() || e.IsCommandKeyHeld() });

            profileBindingService.BindEnabledToProfile(button);

        }

        void SetupSettingsButton(VisualElement element)
        {
            var button = element.Q<Button>("button-settings");
            button.clicked += settingsView.Open;
            profileBindingService.BindEnabledToProfile(button);
        }

        #region Extra buttons

        static VisualElement GetButton(string fontAwesomeIcon, string tooltip, Action callback)
        {
            var button = new Button(callback) { text = fontAwesomeIcon, tooltip = tooltip };
            button.UseFontAwesome();
            return button;
        }

        [ASMWindowElement(ElementLocation.Header)]
        static VisualElement DevBuild() =>
            GetButton("", "Dev build", () => DependencyInjectionUtility.GetService<MenuPopup>().DoDevBuild());

        [ASMWindowElement(ElementLocation.Header)]
        static VisualElement BuildProfiles() =>
            GetButton("", "Build profiles", () =>
            {
                try
                {
                    //Ugly... but no other option currently
                    Type.GetType("UnityEditor.Build.Profile.BuildProfileWindow, UnityEditor.BuildProfileModule, Version=0.0.0.0, Culture=neutral, PublicKeyToken=null\r\n").
                        GetMethod("ShowBuildProfileWindow").
                        Invoke(null, Array.Empty<object>());
                }
                catch
                {
                    BuildPlayerWindow.ShowBuildPlayerWindow();
                }
            });

        [ASMWindowElement(ElementLocation.Header)]
        static VisualElement ProjectSettings() =>
            GetButton("", "Project settings", () => SettingsService.OpenProjectSettings());

        [ASMWindowElement(ElementLocation.Header)]
        static VisualElement OpenEditor() =>
           GetButton("", "Open code editor", () => EditorApplication.ExecuteMenuItem("Assets/Open C# Project"));

        [ASMWindowElement(ElementLocation.Header, isVisibleByDefault: true)]
        static VisualElement OpenOverview() =>
           GetButton("", "Overview", () => DependencyInjectionUtility.GetService<PopupView>().Open<OverviewPopup>());

        [ASMWindowElement(ElementLocation.Header)]
        static VisualElement OpenUnitySearch() =>
           GetButton("", "Unity search", () => SearchService.ShowContextual("ASM"));

        #endregion

    }

}
