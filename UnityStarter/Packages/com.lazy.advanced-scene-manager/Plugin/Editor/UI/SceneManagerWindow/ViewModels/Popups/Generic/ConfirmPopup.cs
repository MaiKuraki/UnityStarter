using System;
using AdvancedSceneManager.Editor.UI.Interfaces;
using AdvancedSceneManager.Editor.UI.Utility;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class ConfirmPopup : ViewModel, IPopup
    {

        bool isOpen => popupView.currentPopup == this;

        Action onConfirm;
        Action onCancel;
        Action onSecondary;
        Action onDismiss;
        string confirmText;
        string cancelText;
        string secondaryText;
        string message;

        bool didPressButton;

        public override void OnReopen()
        {
            //It would be good to have the ability to call onCancel here, but this only happens after domain reload, so no shot.
            ClosePopup();
        }

        public void Prompt(Action onConfirm, Action onCancel = null, string confirmText = "OK", string cancelText = "Cancel", string message = "Are you sure?", Action onSecondary = null, string secondaryText = null, Action onDismiss = null)
        {

            if (isOpen)
                throw new InvalidOperationException("Cannot display multiple prompts at a time.");

            this.onConfirm = onConfirm;
            this.onSecondary = onSecondary;
            this.onCancel = onCancel;
            this.confirmText = confirmText;
            this.secondaryText = secondaryText;
            this.cancelText = cancelText;
            this.message = message;
            this.onDismiss = onDismiss;

            OpenPopup<ConfirmPopup>();

        }

        public override void OnAdded()
        {

            var confirmButton = view.Q<Button>("button-confirm");
            var secondaryButton = view.Q<Button>("button-secondary");
            var cancelButton = view.Q<Button>("button-cancel");

            confirmButton.text = confirmText;
            secondaryButton.text = secondaryText;
            cancelButton.text = cancelText;

            cancelButton.clicked += () => { didPressButton = true; onCancel(); };
            secondaryButton.clicked += () => { didPressButton = true; onSecondary(); };
            confirmButton.clicked += () => { didPressButton = true; onConfirm(); };

            if (string.IsNullOrEmpty(secondaryText) || onSecondary is null)
                secondaryButton.Hide();

            view.Q<Label>("label-message").text = message;

        }

        public override void OnRemoved()
        {

            if (didPressButton)
                return;

            onDismiss?.Invoke();

        }

    }

}
