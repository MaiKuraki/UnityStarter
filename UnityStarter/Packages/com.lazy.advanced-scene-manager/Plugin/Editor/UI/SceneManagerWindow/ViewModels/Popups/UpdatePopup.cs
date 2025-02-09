using System.Threading;
using System.Threading.Tasks;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using AdvancedSceneManager.Editor.UI.Views.Settings;
using AdvancedSceneManager.Editor.Utility;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class UpdatePopup : ViewModel, IPopup
    {

        Button downloadButton;
        public override void OnAdded()
        {

            if (!UpdateUtility.isUpdateAvailable)
            {
                ClosePopup();
                return;
            }

            view.Q<Button>("button-close").clicked += ClosePopup;
            view.Q<Button>("button-cancel").clicked += ClosePopup;
            view.Q<Button>("button-github").clicked += ViewOnGithub;
            view.Q<Button>("button-settings").clicked += ViewSettings;

            downloadButton = view.Q<Button>("button-download");
            downloadButton.clicked += Download;

            view.Q<Label>("text-status").text = UpdateUtility.isAssetStoreUpdateRequired ? "Asset store update is available!" : "Patch is available!"; ;
            view.Q<Label>("text-patch-notes").text = UpdateUtility.availablePatchNotes.Trim();

            view.Q("text-warning-patch-notes").SetVisible(!UpdateUtility.isAssetStoreUpdateRequired);
            view.Q("text-warning-asset-store").SetVisible(UpdateUtility.isAssetStoreUpdateRequired);

            view.Q<ScrollView>().PersistScrollPosition();

        }

        public override void OnRemoved()
        {
            token?.Cancel();
            view.Q<ScrollView>().ClearScrollPosition();
        }

        void ViewOnGithub()
        {
            Application.OpenURL("https://github.com/Lazy-Solutions/AdvancedSceneManager/releases");
        }

        CancellationTokenSource token;

        async void Download()
        {

            try
            {

                token?.Cancel();
                token = new();

                downloadButton.SetEnabled(false);
                await UpdateUtility.CheckUpdate(true, token.Token);
                if (token?.IsCancellationRequested ?? false)
                    throw new TaskCanceledException();

                if (UpdateUtility.isUpdateAvailable)
                    UpdateUtility.Update();
                else
                    ClosePopup();

                token = null;

            }
            catch (TaskCanceledException)
            { }
            catch (System.Exception ex)
            {
                Debug.LogException(ex);
            }
            finally
            {
                downloadButton?.SetEnabled(true);
            }

        }

        void ViewSettings()
        {
            OpenSettings<UpdatesPage>();
        }

    }

}
