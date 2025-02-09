using System;
using System.IO;
using AdvancedSceneManager.Editor.UI.Interfaces;
using UnityEngine;
using UnityEngine.UIElements;

namespace AdvancedSceneManager.Editor.UI.Views.Popups
{

    class PickNamePopup : ViewModel, IPopup
    {

        string value;

        bool isOpen => popupView.currentPopup == this;
        Action<string> onContinue;
        Action onCancel;

        public override void OnReopen() =>
            ClosePopup();

        public void Prompt(Action<string> onContinue, string value = null, Action onCancel = null)
        {

            if (isOpen)
                throw new InvalidOperationException("Cannot display multiple prompts at a time.");

            this.onContinue = onContinue;
            this.onCancel = onCancel;
            this.value = value;

            OpenPopup<PickNamePopup>();

        }

        public override void OnAdded()
        {
            SetupButtons();
            SetupTextField();
        }

        public override void OnRemoved()
        {
            value = null;
            onCancel = null;
            onContinue = null;
        }

        Button buttonContinue;
        void SetupButtons()
        {

            buttonContinue = view.Q<Button>("button-continue");
            buttonContinue.clickable = new(Continue);
            buttonContinue.SetEnabled(false);

            view.Q<Button>("button-cancel").clickable = new(Cancel);

        }

        void SetupTextField()
        {

            var textBox = view.Q<TextField>("text-name");

#if UNITY_2022_1_OR_NEWER
            textBox.selectAllOnMouseUp = false;
            textBox.selectAllOnFocus = false;
#endif

            textBox.RegisterValueChangedCallback(e => value = e.newValue);
            textBox.RegisterValueChangedCallback(e => buttonContinue.SetEnabled(Validate()));

            textBox.value = value;
            textBox.Focus();

            if (!string.IsNullOrEmpty(textBox.value))
                textBox.SelectRange(textBox.value.Length, textBox.value.Length);

            //Register enter callback, it must only run onContinue once
            view.RegisterCallback<KeyDownEvent>(e =>
            {
                if (e.keyCode is KeyCode.KeypadEnter or KeyCode.Return)
                    if (Validate())
                    {

                        e.StopPropagation();
                        e.StopImmediatePropagation();

                        Continue();

                    }
            }, TrickleDown.TrickleDown);

        }

        bool Validate() =>
            !string.IsNullOrWhiteSpace(value) &&
            !value.StartsWith(' ') &&
            !value.EndsWith(' ') &&
            value.IndexOfAny(Path.GetInvalidFileNameChars()) < 0;

        void Cancel() =>
            onCancel?.Invoke();

        void Continue()
        {
            onContinue?.Invoke(value);
            onContinue = null; //Make sure it is not run twice due to enter key
        }

    }

}
