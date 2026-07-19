using UnityEditor;
using UnityEngine;

namespace CycloneGames.Utility.Editor
{
    /// <summary>
    /// Narrow, Editor-only presentation helpers shared by Utility's explicit custom inspectors.
    /// </summary>
    internal static class InspectorUiUtility
    {
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;

        private static readonly Vector3[] FoldoutTrianglePoints = new Vector3[3];
        private static GUIStyle _foldoutLabelStyle;
        private static GUIStyle _statValueStyle;

        public static bool DrawFoldoutHeader(string title, bool expanded, Color color)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, 22f);
            Color background = expanded
                ? color
                : new Color(color.r * 0.72f, color.g * 0.72f, color.b * 0.72f, color.a);
            EditorGUI.DrawRect(rect, background);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0f, 0f, 0f, 0.22f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.22f));

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

            DrawFoldoutTriangle(arrowRect, expanded);
            EditorGUI.LabelField(labelRect, title, _foldoutLabelStyle);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && current.button == 0 && rect.Contains(current.mousePosition))
            {
                expanded = !expanded;
                current.Use();
            }

            return expanded;
        }

        public static void DrawModuleHeader(string title, string description)
        {
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(description, MessageType.None);
            EditorGUILayout.Space(2f);
        }

        public static void DrawScriptProperty(SerializedProperty scriptProperty)
        {
            if (scriptProperty == null)
            {
                return;
            }

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(scriptProperty);
            }
        }

        public static void DrawReadOnlyStat(string label, string value)
        {
            EnsureStyles();
            Rect rect = EditorGUILayout.GetControlRect();
            EditorGUI.LabelField(rect, label);
            EditorGUI.LabelField(rect, value, _statValueStyle);
        }

        private static void EnsureStyles()
        {
            if (_foldoutLabelStyle != null)
            {
                return;
            }

            _foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                fontStyle = FontStyle.Bold,
                normal = { textColor = Color.white }
            };
            _statValueStyle = new GUIStyle(EditorStyles.label)
            {
                alignment = TextAnchor.MiddleRight,
                fontStyle = FontStyle.Bold
            };
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;
            if (expanded)
            {
                FoldoutTrianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                FoldoutTrianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                FoldoutTrianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                FoldoutTrianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                FoldoutTrianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                FoldoutTrianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.94f, 0.94f, 0.94f, 0.96f);
            Handles.DrawAAConvexPolygon(FoldoutTrianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
