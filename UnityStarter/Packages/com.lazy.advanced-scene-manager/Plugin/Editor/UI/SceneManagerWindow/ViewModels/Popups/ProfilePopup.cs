using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using AdvancedSceneManager.Editor.Utility;
using AdvancedSceneManager.Models;
using UnityEngine;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class ProfilePopup : ListPopup<Profile>
    {

        public override string noItemsText { get; } = "No profiles, you can create one using + button.";
        public override string headerText { get; } = "Profiles";
        public override IEnumerable<Profile> items => SceneManager.assets.profiles;

        public override bool displayRenameButton => true;
        public override bool displayRemoveButton => true;
        public override bool displayDuplicateButton => true;

        public override void OnAdded()
        {
            base.OnAdded();
            SceneImportUtility.scenesChanged += Reload;
        }

        public override void OnRemoved()
        {
            base.OnRemoved();
            SceneImportUtility.scenesChanged -= Reload;
        }

        public override void OnAdd()
        {

            pickNamePopup.Prompt(value =>
            {

                try
                {
                    Profile.SetProfile(Profile.Create(value));
                    ClosePopup();
                }
                catch (Exception e)
                {
                    Debug.LogError(e);
                }

            },
            onCancel: OpenPopup<ProfilePopup>);
        }

        public override async void OnSelected(Profile profile)
        {

            progressSpinnerView.Start();
            ClosePopup();
            await Task.Delay(250);
            Profile.SetProfile(profile);

            progressSpinnerView.Stop();

        }

        public override void OnRename(Profile profile)
        {

            pickNamePopup.Prompt(
                value: profile.name,
                onContinue: (value) =>
                {
                    profile.Rename(value);
                    OpenPopup<ProfilePopup>();
                },
                onCancel: OpenPopup<ProfilePopup>);

        }

        public override void OnRemove(Profile profile)
        {

            confirmPopup.Prompt(() =>
            {
                Profile.Delete(profile);
                OpenPopup<ProfilePopup>();
            },
            onCancel: OpenPopup<ProfilePopup>,
            message: $"Are you sure you wish to remove '{profile.name}'?");

        }

        public override void OnDuplicate(Profile profile)
        {
            Profile.Duplicate(profile);
            Reload();
        }

    }

}
