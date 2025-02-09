#if UNITY_EDITOR

using UnityEditor;
using UnityEngine;

namespace AdvancedSceneManager.Editor.Utility
{

    /// <summary>Provides utility functions for prompting the user.</summary>
    static class PromptUtility
    {

        /// <summary>Prompts the user.</summary>
        public static bool Prompt(string title, string message, string ok = "OK", string cancel = "Cancel", DialogOptOutDecisionType rememberType = DialogOptOutDecisionType.ForThisSession, string rememberKey = null)
        {

            var result = string.IsNullOrWhiteSpace(rememberKey)
                ? EditorUtility.DisplayDialog(title, message, ok, cancel)
                : EditorUtility.DisplayDialog(title, message, ok, cancel, rememberType, rememberKey);

            return result;

        }

        /// <summary>Prompts the user.</summary>
        public static bool Prompt(string title, string message, string option1Text, string option2Text, out bool option1, out bool option2, string cancelText = "Cancel")
        {

            option1 = false;
            option2 = false;

            var result = EditorUtility.DisplayDialogComplex(title, message, cancelText, option2Text, option1Text);
            if (result == 1)
                option2 = true;
            else if (result == 2)
                option1 = true;

            return option1 || option2;

        }

        /// <summary>Prompts deletion with a preset message.</summary>
        /// <param name="itemType">The friendly type name of the object being deleted, i.e. 'collection', 'template'.</param>
        public static bool PromptDelete(string itemType) =>
            Prompt($"Removing {itemType}...", $"This is irreversible! Are you sure you wish to remove the {itemType}?");

        /// <summary>Prompts the user to input a string.</summary>
        public static bool PromptString(string title, string message, out string result, string initialText = null) =>
            EditorInputDialog.Show(title, message, out result, initialText);

        class EditorInputDialog : EditorWindow
        {

            string message;
            string text;
            bool ok;
            bool hasSetPosition;
            public static bool Show(string title, string message, out string result, string initialText = null)
            {

                var window = CreateInstance<EditorInputDialog>();
                window.titleContent = new GUIContent(title);
                window.message = message;
                window.text = initialText;

                window.ShowModal();
                result = window.text;

                return window.ok;

            }

            void OnGUI()
            {

                SetPosition();
                CheckInput();

                // Draw our control
                var rect = EditorGUILayout.BeginVertical(new GUIStyle() { padding = new(12, 12, 0, 0) });
                if (Event.current.type == EventType.Repaint)
                    rect.width = 400;

                EditorGUILayout.Space(12);
                EditorGUILayout.LabelField(message);

                EditorGUILayout.Space(8);

                GUI.SetNextControlName("text");
                text = EditorGUILayout.TextField("", text);
                GUI.FocusControl("text");   // Focus text field

                EditorGUILayout.Space(12);

                EditorGUILayout.BeginHorizontal();

                if (GUILayout.Button("Cancel"))
                    Close();

                if (GUILayout.Button("OK"))
                {
                    ok = true;
                    Close();
                }

                EditorGUILayout.EndHorizontal();

                EditorGUILayout.Space(8);
                EditorGUILayout.EndVertical();

                // Force change size of the window
                if (rect.width != 0 && minSize != rect.size)
                    minSize = maxSize = rect.size;

            }

            void CheckInput()
            {

                var e = Event.current;
                if (e.type == EventType.KeyDown)
                    if (e.keyCode == KeyCode.Escape)
                        Close();
                    else if (e.keyCode is KeyCode.Return or KeyCode.KeypadEnter)
                    {
                        ok = true;
                        Close();
                    }

            }

            void SetPosition()
            {

                if (hasSetPosition)
                    return;
                hasSetPosition = true;

                position = new Rect((Screen.mainWindowDisplayInfo.width / 2) - (position.width / 2), (Screen.mainWindowDisplayInfo.height / 2) - (position.height / 2), position.width, position.height);

            }

        }

    }

}
#endif
