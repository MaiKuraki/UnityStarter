#if UNITY_EDITOR && TOOLBAR_EXTENDER

using AdvancedSceneManager.Models;
using UnityEditor;
using UnityEngine;
using UnityToolbarExtender;

namespace AdvancedSceneManager.Editor.Utility
{

    [InitializeOnLoad]
    static class ToolbarButton
    {

        static ToolbarButton() =>
            SceneManager.OnInitialized(() =>
            {
                ToolbarExtender.LeftToolbarGUI.Add(() => DrawButton(true));
                ToolbarExtender.RightToolbarGUI.Add(() => DrawButton(false));
            });

        static void DrawButton(bool onLeft)
        {

            if (SceneManager.settings.user.toolbarButtonCount < 1)
                return;

            if (onLeft && SceneManager.settings.user.toolbarPlayButtonOffset > 0)
                return;
            else if (!onLeft && SceneManager.settings.user.toolbarPlayButtonOffset <= 0)
                return;

            var offset = ((Screen.width * 0.5f) - (width + 67)) * (SceneManager.settings.user.toolbarPlayButtonOffset / 100f);

            if (onLeft)
                GUILayout.FlexibleSpace();
            else
                GUILayout.Space(offset);

            var c = GUI.contentColor;
            GUI.contentColor = Color.green;

            if (Event.current.type == EventType.Repaint)
                width = 0;
            for (int i = 0; i < SceneManager.settings.user.toolbarButtonCount; i++)
                DrawPlayButton(i);

            if (onLeft)
                GUILayout.Space(-offset);
            else
                GUILayout.FlexibleSpace();

            GUI.contentColor = c;

        }

        static float width;
        static void DrawPlayButton(int i)
        {

            SceneManager.settings.user.ToolbarAction(i, out var collection, out var runStartupProcess);
            var style = EditorStyles.toolbarDropDown;

            EditorGUI.BeginChangeCheck();

            var content = EditorGUIUtility.IconContent("d_PlayButton");
            content = new GUIContent(content)
            {
                text = (collection ? collection.title : "") + " ",
                tooltip = "Enter ASM play mode"
            };

            var r = GUILayoutUtility.GetRect(content, style);

            if (Event.current.type == EventType.Repaint)
                width += r.width;

            GUI.Button(r, content, style);

            var isHoveringOverSplitButton = Event.current.mousePosition.x < r.xMax && Event.current.mousePosition.x > r.xMax - 16;
            if (EditorGUI.EndChangeCheck())
            {

                var isContext = Event.current.button == 1 || isHoveringOverSplitButton;

                if (!isContext)
                {
                    SceneManager.app.Restart(new Core.App.StartupProps(SceneManager.app.startupProps ?? new()) { openCollection = collection, runStartupProcessWhenPlayingCollection = runStartupProcess });
                }
                else
                {

                    var menu = new GenericMenu();
                    menu.AddItem(new GUIContent("None"), !collection, () => SceneManager.settings.user.ToolbarAction(i, null, true));
                    menu.AddSeparator("");
                    foreach (var c in Profile.current.collections)
                        menu.AddItem(new GUIContent(c.title), collection == c, () => SceneManager.settings.user.ToolbarAction(i, c, runStartupProcess));

                    menu.AddSeparator("");
                    if (collection)
                        menu.AddItem(new GUIContent("Run startup process"), runStartupProcess, () => SceneManager.settings.user.ToolbarAction(i, collection, !runStartupProcess));
                    else
                        menu.AddDisabledItem(new GUIContent("Run startup process"), false);

                    menu.ShowAsContext();

                }

            }

        }

        public static void Repaint()
        {
            if (EditorWindow.focusedWindow)
                EditorWindow.focusedWindow.SendEvent(Event.KeyboardEvent("x"));
        }

    }

}
#endif
