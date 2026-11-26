using UnityEditor;
using UnityEngine;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>Shared Inspector drawing primitives for the input module.</summary>
    internal static class EditorUiUtility
    {
        private static GUIStyle _headerStyle;

        private static GUIStyle HeaderStyle
        {
            get
            {
                if (_headerStyle == null)
                {
                    _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                    {
                        alignment = TextAnchor.MiddleLeft,
                        padding = new RectOffset(4, 4, 0, 0),
                        margin = new RectOffset(0, 0, 0, 0)
                    };
                }

                return _headerStyle;
            }
        }

        internal static void SectionHeader(string title)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
        }

        internal static void Hint(string text)
        {
            EditorGUILayout.LabelField(text, EditorStyles.wordWrappedMiniLabel);
        }

        /// <summary>Opens a boxed section whose triangle and title are both drawn inside the box.</summary>
        internal static bool FoldoutSection(bool expanded, string title, int count)
        {
            BeginCard();
            if (GUILayout.Button((expanded ? "\u25BC  " : "\u25B6  ") + title + " (" + count + ")", HeaderStyle, GUILayout.Height(20f)))
            {
                expanded = !expanded;
            }
            return expanded;
        }

        internal static void EndFoldoutSection()
        {
            EndCard();
        }

        internal static void BeginCard()
        {
            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
        }

        internal static void EndCard()
        {
            EditorGUILayout.EndVertical();
        }

        internal static void Separator()
        {
            EditorGUILayout.Space(6f);
        }

        internal static void Info(string text)
        {
            EditorGUILayout.HelpBox(text, MessageType.Info);
        }

        internal static void Warning(string text)
        {
            EditorGUILayout.HelpBox(text, MessageType.Warning);
        }
    }
}
