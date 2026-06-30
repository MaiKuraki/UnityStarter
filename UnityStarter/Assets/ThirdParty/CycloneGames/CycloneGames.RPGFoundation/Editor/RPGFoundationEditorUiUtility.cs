using System;
using UnityEditor;
using UnityEngine;
using Handles = UnityEditor.Handles;

namespace CycloneGames.RPGFoundation.Editor
{
    public static class RPGFoundationEditorUiUtility
    {
        private const float HEADER_HEIGHT = 22f;
        private const float HEADER_HORIZONTAL_PADDING = 4f;
        private const float HEADER_ARROW_WIDTH = 13f;
        private const float BADGE_HEIGHT = 18f;

        public static readonly Color ColorCore = new Color(0.22f, 0.36f, 0.54f, 1f);
        public static readonly Color ColorRuntime = new Color(0.35f, 0.31f, 0.48f, 1f);
        public static readonly Color ColorBehavior = new Color(0.28f, 0.44f, 0.33f, 1f);
        public static readonly Color ColorWarning = new Color(0.62f, 0.42f, 0.22f, 1f);
        public static readonly Color ColorDebug = new Color(0.30f, 0.45f, 0.48f, 1f);
        public static readonly Color ColorError = new Color(0.56f, 0.25f, 0.25f, 1f);

        private static readonly Vector3[] TrianglePoints = new Vector3[3];

        private static GUIStyle s_foldoutLabelStyle;
        private static GUIStyle s_badgeStyle;
        private static GUIStyle s_statusLabelStyle;

        public static bool DrawFoldoutHeader(string title, bool foldout, Color color)
        {
            EnsureStyles();

            Rect rect = EditorGUILayout.GetControlRect(false, HEADER_HEIGHT);
            Color backgroundColor = foldout ? color : Darken(color, 0.72f);
            EditorGUI.DrawRect(rect, backgroundColor);
            EditorGUI.DrawRect(new Rect(rect.x, rect.y, rect.width, 1f), new Color(0f, 0f, 0f, 0.2f));
            EditorGUI.DrawRect(new Rect(rect.x, rect.yMax - 1f, rect.width, 1f), new Color(0f, 0f, 0f, 0.2f));

            Rect arrowRect = new Rect(
                rect.x + HEADER_HORIZONTAL_PADDING,
                rect.y + 2f,
                HEADER_ARROW_WIDTH,
                rect.height - 4f);

            Rect labelRect = new Rect(
                arrowRect.xMax + 2f,
                rect.y,
                rect.width - (arrowRect.xMax - rect.x) - HEADER_HORIZONTAL_PADDING - 2f,
                rect.height);

            DrawFoldoutTriangle(arrowRect, foldout);
            EditorGUI.LabelField(labelRect, title, s_foldoutLabelStyle);

            Event current = Event.current;
            if (current.type == EventType.MouseDown && rect.Contains(current.mousePosition))
            {
                foldout = !foldout;
                current.Use();
            }

            return foldout;
        }

        public static void DrawSection(string title, Color color, ref bool foldout, Action drawContent)
        {
            foldout = DrawFoldoutHeader(title, foldout, color);
            if (!foldout)
            {
                return;
            }

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                drawContent?.Invoke();
            }
        }

        public static void DrawHelpBox(string message, MessageType messageType)
        {
            EditorGUILayout.HelpBox(message, messageType);
        }

        public static void DrawStatusBadge(Rect rect, string label, Color color)
        {
            EnsureStyles();

            Color previousColor = GUI.color;
            GUI.color = color;
            GUI.Label(rect, GUIContent.none, EditorStyles.helpBox);
            GUI.color = previousColor;

            EditorGUI.LabelField(rect, label, s_badgeStyle);
        }

        public static void DrawStatusRow(string label, string value, Color color)
        {
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(128f));
            Rect badgeRect = EditorGUILayout.GetControlRect(false, BADGE_HEIGHT, GUILayout.Width(160f));
            DrawStatusBadge(badgeRect, value, color);
            EditorGUILayout.EndHorizontal();
        }

        public static void DrawReadOnlyText(string label, string value)
        {
            EnsureStyles();
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(128f));
            EditorGUILayout.LabelField(value, s_statusLabelStyle);
            EditorGUILayout.EndHorizontal();
        }

        public static void DrawPropertyIfPresent(SerializedProperty property, GUIContent label = null, bool includeChildren = true)
        {
            if (property == null)
            {
                return;
            }

            EditorGUILayout.PropertyField(property, label ?? new GUIContent(property.displayName), includeChildren);
        }

        public static void DrawDerivedProperties(
            SerializedObject serializedObject,
            string title,
            string[] excludedProperties)
        {
            if (!HasVisibleDerivedProperties(serializedObject, excludedProperties))
            {
                return;
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                SerializedProperty iterator = serializedObject.GetIterator();
                bool enterChildren = true;
                while (iterator.NextVisible(enterChildren))
                {
                    enterChildren = false;
                    if (IsExcluded(iterator.name, excludedProperties))
                    {
                        continue;
                    }

                    EditorGUILayout.PropertyField(iterator, true);
                }
            }
        }

        public static bool IsLayerMaskEmpty(SerializedProperty layerMaskProperty)
        {
            return layerMaskProperty != null
                && layerMaskProperty.propertyType == SerializedPropertyType.LayerMask
                && layerMaskProperty.intValue == 0;
        }

        private static bool HasVisibleDerivedProperties(
            SerializedObject serializedObject,
            string[] excludedProperties)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                if (!IsExcluded(iterator.name, excludedProperties))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool IsExcluded(string propertyName, string[] excludedProperties)
        {
            return Array.IndexOf(excludedProperties, propertyName) >= 0;
        }

        private static Color Darken(Color color, float factor)
        {
            return new Color(color.r * factor, color.g * factor, color.b * factor, color.a);
        }

        private static void EnsureStyles()
        {
            if (s_foldoutLabelStyle != null)
            {
                return;
            }

            s_foldoutLabelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleLeft
            };

            s_badgeStyle = new GUIStyle(EditorStyles.miniLabel)
            {
                normal = { textColor = Color.white },
                fontStyle = FontStyle.Bold,
                alignment = TextAnchor.MiddleCenter
            };

            s_statusLabelStyle = new GUIStyle(EditorStyles.label)
            {
                wordWrap = false
            };
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;

            if (expanded)
            {
                TrianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                TrianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                TrianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                TrianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                TrianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                TrianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.9f, 0.9f, 0.9f, 0.95f);
            Handles.DrawAAConvexPolygon(TrianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
