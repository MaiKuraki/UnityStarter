using System.Linq;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using AdvancedSceneManager.Models.Internal;
using AdvancedSceneManager.Utility;
using UnityEditor.UIElements;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class StartupPage : ViewModel, ISettingsPage
    {

        public string Header => "Startup";

        public override void OnAdded()
        {

            view.Bind(Profile.serializedObject);

            view.Q<DropdownField>("dropdown-splash-scene").
                SetupSceneDropdown(
                getScenes: () => Assets.scenes.Where(s => s.isSplashScreen),
                getValue: () => SceneManager.settings.profile.splashScene,
                setValue: (s) =>
                {
                    if (Profile.current)
                    {
                        Profile.current.splashScene = s;
                        Profile.current.Save();
                    }
                },
                onRefreshButton: LoadingScreenUtility.RefreshSpecialScenes);

        }

    }

}
