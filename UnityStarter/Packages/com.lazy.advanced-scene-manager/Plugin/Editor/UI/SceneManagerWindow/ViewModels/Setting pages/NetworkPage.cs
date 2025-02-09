using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class NetworkPage : ViewModel, ISettingsPage
    {

        public string Header => "Network";

        public override void OnAdded()
        {
            view.BindToSettings();
            view.Q("toggle-sync-indicator").BindToUserSettings();
        }

    }

}
