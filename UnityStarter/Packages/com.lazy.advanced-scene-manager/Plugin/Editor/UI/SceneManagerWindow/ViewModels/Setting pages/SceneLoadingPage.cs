using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using AdvancedSceneManager.Utility.CrossSceneReferences;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class SceneLoadingPage : ViewModel, ISettingsPage
    {

        public string Header => "Scene loading";

        public override void OnAdded()
        {

            view.Q("section-profile").BindToProfile();
            view.Q("section-project-settings").BindToSettings();
            view.Q("group-references").BindToSettings();
            view.Q("group-event-methods").BindToSettings();

            SetupScenePickers();
            SetupReferencesToggles();

        }

        void SetupScenePickers()
        {

            view.Q<DropdownField>("dropdown-loading-scene").
                SetupSceneDropdown(
                getScenes: () => Assets.scenes.Where(s => s.isLoadingScreen),
                getValue: () => SceneManager.settings.profile.loadingScene,
                setValue: (s) =>
                {
                    SceneManager.settings.profile.loadingScene = s;
                    SceneManager.settings.profile.Save();
                },
                onRefreshButton: LoadingScreenUtility.RefreshSpecialScenes);

            view.Q<DropdownField>("dropdown-fade-scene").
                SetupSceneDropdown(
                getScenes: () => Assets.scenes.Where(s => s.isLoadingScreen),
                getValue: () => SceneManager.settings.project.fadeScene,
                setValue: (s) => SceneManager.settings.project.fadeScene = s,
                onRefreshButton: LoadingScreenUtility.RefreshSpecialScenes);

        }

        void SetupReferencesToggles()
        {

            var crossRefToggle = view.Q<Toggle>("toggle-cross-scene-references");
            var guidReferenceToggle = view.Q("toggle-guid-references");

            crossRefToggle.RegisterValueChangedCallback(e =>
            {
                UpdateGUIDToggle();
                if (e.newValue)
                    SceneManager.settings.project.enableGUIDReferences = true;
                CrossSceneReferenceUtility.Initialize();
            });

            UpdateGUIDToggle();
            void UpdateGUIDToggle() =>
                guidReferenceToggle.SetEnabled(!SceneManager.settings.project.enableCrossSceneReferences);

            //UI Builder does not support newline in tooltips for some reason, and since this works, we're doing it here
            crossRefToggle.tooltip =
                "[Experimental]\n" +
                "Enables cross-scene references.\n\n" +
                "Note that Unity does not fully support this, and you will receive warnings. It's not unlikely you'll experience jankiness and wrong behavior as well.\n\n" +
                "This is due to Unity unintentionally blocking third-party implementations when warning people and making sure they don't do anything that is not supported by default, even if it sometimes might seem like it.";

        }

    }

}
