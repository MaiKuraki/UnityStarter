using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Settings
{

    class UpdatesPage : ViewModel, ISettingsPage
    {

        public string Header => "Updates";

        Label statusLabel;
        Label currentVersionLabel;
        Label availableVersionLabel;
        Label assetStoreLabel;

        Button checkButton;
        Button downloadButton;
        public override void OnAdded()
        {

            view.Q("dropdown-interval").BindToUserSettings();
            view.Q("toggle-default").BindToSettings();

            checkButton = view.Q<Button>("button-check");
            downloadButton = view.Q<Button>("button-download");
            statusLabel = view.Q<Label>("text-status");
            currentVersionLabel = view.Q<Label>("text-version-current");
            availableVersionLabel = view.Q<Label>("text-version-available");
            assetStoreLabel = view.Q<Label>("text-asset-store");

            checkButton.clicked += Check;
            downloadButton.clicked += Download;
            view.Q<Button>("button-view-patches").clicked += ViewPatches;

            Reload();

            UpdateUtility.updateChecked += UpdateUtility_updateChecked;

        }

        public override void OnRemoved()
        {
            UpdateUtility.updateChecked -= UpdateUtility_updateChecked;
        }

        private void UpdateUtility_updateChecked()
        {
            Reload();
        }

        void Reload()
        {

            currentVersionLabel.text = "Installed version: <b>" + UpdateUtility.installedVersion + "</b>";
            availableVersionLabel.text = "Available version: <b>" + UpdateUtility.availableVersion + "</b>";

            availableVersionLabel.SetVisible(UpdateUtility.isUpdateAvailable);
            assetStoreLabel.SetVisible(UpdateUtility.isAssetStoreUpdateRequired);

            checkButton.SetVisible(!UpdateUtility.isUpdateAvailable);
            downloadButton.SetVisible(UpdateUtility.isUpdateAvailable);

            if (UpdateUtility.isUpdateAvailable)
                statusLabel.text = UpdateUtility.isAssetStoreUpdateRequired ? "Asset store update is available!" : "Patch is available!";
            else
                statusLabel.text = "You are up to date!";

        }

        async void Check()
        {
            await UpdateUtility.CheckUpdate(true);
        }

        async void Download()
        {
            await UpdateUtility.CheckUpdate(true);
            if (UpdateUtility.isUpdateAvailable)
                UpdateUtility.Update();
        }

        void ViewPatches() =>
            Application.OpenURL(UpdateUtility.GithubReleases);

    }

}
