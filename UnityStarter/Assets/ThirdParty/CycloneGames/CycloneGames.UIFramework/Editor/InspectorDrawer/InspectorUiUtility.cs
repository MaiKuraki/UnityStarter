// Copyright (c) CycloneGames
// Licensed under the MIT License.

using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.UIFramework.Editor
{
    internal static class InspectorUiUtility
    {
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;
        private const float BadgeHorizontalPadding = 6f;

        private static readonly Vector3[] trianglePoints = new Vector3[3];

        private static GUIStyle foldoutLabelStyle;
        private static GUIStyle badgeStyle;

        public static bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EnsureStyles();

            EditorGUILayout.Space(2f);

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

        public static void DrawStatusBadge(string label, Color color, float width)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(width));
            DrawStatusBadge(rect, label, color);
        }

        public static void DrawStatusBadge(Rect rect, string label, Color color)
        {
            EnsureStyles();

            Color backgroundColor = new Color(color.r, color.g, color.b, 0.85f);
            EditorGUI.DrawRect(rect, backgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), Color.black * 0.18f);
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), Color.black * 0.18f);

            Rect labelRect = new Rect(
                rect.x + BadgeHorizontalPadding,
                rect.y,
                rect.width - BadgeHorizontalPadding * 2f,
                rect.height);
            EditorGUI.LabelField(labelRect, label, badgeStyle);
        }

        private static void EnsureStyles()
        {
            if (foldoutLabelStyle != null)
            {
                return;
            }

            foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;

            if (expanded)
            {
                trianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                trianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                trianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                trianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                trianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                trianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
            Handles.DrawAAConvexPolygon(trianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
