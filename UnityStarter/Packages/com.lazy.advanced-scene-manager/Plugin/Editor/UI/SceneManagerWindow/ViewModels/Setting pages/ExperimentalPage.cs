using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class ExperimentalPage : ViewModel, ISettingsPage
    {

        public string Header => "Experimental";

        public override void OnAdded()
        {
            view.BindToSettings();
        }

    }

}
