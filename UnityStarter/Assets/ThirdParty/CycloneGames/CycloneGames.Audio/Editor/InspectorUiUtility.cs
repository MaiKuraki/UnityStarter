// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.Audio.Editor
{
    internal static class InspectorUiUtility
    {
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;

        private static GUIStyle foldoutLabelStyle;
        private static GUIStyle subHeaderStyle;
        private static GUIStyle subBadgeStyle;

        public static bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            Color backgroundColor = foldout ? color : new Color(color.r * 0.7f, color.g * 0.7f, color.b * 0.7f);
            EditorGUI.DrawRect(rect, backgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.black * 0.2f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black * 0.2f);

            Rect arrowRect = new Rect(
                rect.x + HeaderHorizontalPadding,
                rect.y + 2f,
                HeaderArrowWidth,
                rect.height - 4f);

            Rect labelRect = new Rect(
                arrowRect.xMax + 1f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - HeaderHorizontalPadding - 1f,
                rect.height);

            DrawFoldoutTriangle(arrowRect, foldout);
            EditorGUI.LabelField(labelRect, title, foldoutLabelStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        /// <summary>
        /// Draws a compact sub-section foldout header inside a platform panel.
        /// When <paramref name="enabledBool"/> is provided, shows a coloured On/Off badge on the right.
        /// </summary>
        public static bool DrawSubFoldoutHeader(string title, bool foldout, bool? enabledBool = null)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 18f);
            EditorGUI.DrawRect(rect, new Color(0f, 0f, 0f, 0.18f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.12f));

            // Arrow
            Rect arrowRect = new Rect(rect.x + 3f, rect.y + 2f, 13f, rect.height - 4f);
            DrawFoldoutTriangle(arrowRect, foldout);

            // On/Off badge (right side)
            float badgeWidth = 0f;
            if (enabledBool.HasValue)
            {
                badgeWidth = 36f;
                Rect badgeRect = new Rect(rect.xMax - badgeWidth - 3f, rect.y, badgeWidth, rect.height);
                Color prevColor = GUI.color;
                GUI.color = enabledBool.Value
                    ? new Color(0.45f, 0.92f, 0.45f)
                    : new Color(0.55f, 0.55f, 0.55f);
                EditorGUI.LabelField(badgeRect, enabledBool.Value ? "● On" : "○ Off", subBadgeStyle);
                GUI.color = prevColor;
            }

            // Title
            Rect labelRect = new Rect(
                arrowRect.xMax + 3f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - badgeWidth - 8f,
                rect.height);
            EditorGUI.LabelField(labelRect, title, subHeaderStyle);

            if (Event.current.type == EventType.MouseDown && rect.Contains(Event.current.mousePosition))
            {
                foldout = !foldout;
                Event.current.Use();
            }

            return foldout;
        }

        public static void DrawSectionHeader(string title, string subtitle, Color titleColor)
        {
            Color previousColor = GUI.color;
            GUI.color = titleColor;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.color = previousColor;
            EditorGUILayout.HelpBox(subtitle, MessageType.None);
        }

        private static void EnsureStyles()
        {
            if (foldoutLabelStyle != null)
                return;

            foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            subHeaderStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 11,
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = new Color(0.82f, 0.82f, 0.82f) }
            };

            subBadgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold,
                fontSize = 10
            };
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;
            Vector3[] points;

            if (expanded)
            {
                points = new[]
                {
                    new Vector3(center.x - 4f, center.y - 2f),
                    new Vector3(center.x + 4f, center.y - 2f),
                    new Vector3(center.x, center.y + 3f)
                };
            }
            else
            {
                points = new[]
                {
                    new Vector3(center.x - 2f, center.y - 4f),
                    new Vector3(center.x - 2f, center.y + 4f),
                    new Vector3(center.x + 3f, center.y)
                };
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.90f, 0.90f, 0.90f, 0.95f);
            Handles.DrawAAConvexPolygon(points);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
