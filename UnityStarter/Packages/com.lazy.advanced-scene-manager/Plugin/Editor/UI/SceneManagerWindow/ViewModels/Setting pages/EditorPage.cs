using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using UnityEditor;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class EditorPage : ViewModel, ISettingsPage
    {

        public string Header => "Editor";

        public override void OnAdded()
        {

            view.BindToUserSettings();
            SetupProfiles();
            SetupLocking();
            SetupConfigurablePlayMode();

            view.Q("toggle-autoUpdateBuildScenes").BindToSettings();
        }

        void SetupProfiles()
        {

            var profileForce = view.Q<ObjectField>("profile-force");
            var profileDefault = view.Q<ObjectField>("profile-default");

            profileForce.RegisterValueChangedCallback(e => profileDefault.SetEnabled(!profileForce.value));

            view.Q("group-profiles").BindToSettings();
            view.Q("group-profiles").Query<PropertyField>().ForEach(e => e.SetEnabled(true));

        }

        void SetupLocking()
        {
            view.Q("group-locking").BindToSettings();
            view.Q<Toggle>("toggle-scene-lock").RegisterValueChangedCallback(e => HierarchyGUIUtility.Repaint());
        }

        void SetupConfigurablePlayMode()
        {

            EnterPlayModeOptions[] list =
            {
                    EnterPlayModeOptions.None,
                    EnterPlayModeOptions.DisableDomainReload,
                    EnterPlayModeOptions.DisableSceneReload,
                    EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload,
                };

            var field = view.Q<DropdownField>("enum-play-mode-option");
            field.index = GetOptionIndex(EditorSettings.enterPlayModeOptions);

            field.RegisterValueChangedCallback(e =>
            {
                // Use e.newValue instead of field.index
                var selectedOption = GetEnterPlayModeOption(field.index);
                EditorSettings.enterPlayModeOptions = selectedOption;
                EditorSettings.enterPlayModeOptionsEnabled = selectedOption != EnterPlayModeOptions.None;
            });

        }

        int GetOptionIndex(EnterPlayModeOptions options) =>
            options switch
            {
                EnterPlayModeOptions.None => 0,
                EnterPlayModeOptions.DisableDomainReload => 1,
                EnterPlayModeOptions.DisableSceneReload => 2,
                EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload => 3,
                _ => 0,
            };

        EnterPlayModeOptions GetEnterPlayModeOption(int index) =>
            index switch
            {
                0 => EnterPlayModeOptions.None,
                1 => EnterPlayModeOptions.DisableDomainReload,
                2 => EnterPlayModeOptions.DisableSceneReload,
                3 => EnterPlayModeOptions.DisableDomainReload | EnterPlayModeOptions.DisableSceneReload,
                _ => EnterPlayModeOptions.None,
            };

    }

}
