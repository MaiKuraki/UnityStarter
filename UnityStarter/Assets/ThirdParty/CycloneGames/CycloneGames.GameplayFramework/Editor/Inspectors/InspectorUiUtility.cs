using System;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    internal static class InspectorUiUtility
    {
        private const float SectionContentPadding = 14f;
        private const float HeaderHorizontalPadding = 4f;
        private const float HeaderArrowWidth = 13f;

        private static GUIStyle foldoutLabelStyle;
        private static readonly Vector3[] foldoutTrianglePoints = new Vector3[3];
        private static readonly string[] noPropertyNames = Array.Empty<string>();

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

        public static void DrawSectionHeader(string title, string subtitle, Color titleColor)
        {
            Color previousColor = GUI.color;
            GUI.color = titleColor;
            EditorGUILayout.LabelField(title, EditorStyles.boldLabel);
            GUI.color = previousColor;
            EditorGUILayout.HelpBox(subtitle, MessageType.None);
        }

        public static void DrawSerializedProperties(SerializedObject serializedObject, params string[] paddedPropertyNames)
        {
            DrawSerializedPropertiesExcluding(serializedObject, paddedPropertyNames, noPropertyNames);
        }

        public static void DrawSerializedPropertiesExcluding(
            SerializedObject serializedObject,
            string[] paddedPropertyNames,
            string[] excludedPropertyNames)
        {
            SerializedProperty iterator = serializedObject.GetIterator();
            bool enterChildren = true;

            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (iterator.name == "m_Script")
                {
                    continue;
                }

                if (Array.IndexOf(excludedPropertyNames, iterator.name) >= 0)
                {
                    continue;
                }

                if (Array.IndexOf(paddedPropertyNames, iterator.name) >= 0)
                {
                    DrawPaddedPropertyField(iterator, SectionContentPadding);
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        public static void DrawActorTickConfiguration(
            SerializedObject serializedObject,
            ActorTickPhase? codeOwnedPhase = null)
        {
            DrawSectionHeader(
                "Primary Actor Tick",
                "World dispatches opt-in Actor Tick through one selected PlayerLoop phase. None removes the Actor from Tick dispatch.",
                new Color(0.46f, 0.84f, 1f, 1f));

            if (codeOwnedPhase.HasValue)
            {
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.EnumPopup("Tick Phase", codeOwnedPhase.Value);
                    EditorGUILayout.Toggle("Start With Tick Enabled", false);
                }

                EditorGUILayout.HelpBox(
                    "This Actor type owns its Tick phase in code and enables Tick only while its runtime service is active.",
                    MessageType.Info);
                return;
            }

            SerializedProperty phaseProperty = serializedObject.FindProperty("PrimaryTickPhase");
            SerializedProperty enabledProperty = serializedObject.FindProperty("StartWithTickEnabled");
            if (phaseProperty == null || enabledProperty == null)
            {
                EditorGUILayout.HelpBox("Actor Tick configuration properties are unavailable.", MessageType.Error);
                return;
            }

            using (new EditorGUI.DisabledScope(Application.isPlaying))
            {
                EditorGUILayout.PropertyField(phaseProperty, new GUIContent("Tick Phase"));
                bool phaseIsNone = !phaseProperty.hasMultipleDifferentValues &&
                                   phaseProperty.enumValueIndex == (int)ActorTickPhase.None;
                using (new EditorGUI.DisabledScope(phaseIsNone))
                {
                    EditorGUILayout.PropertyField(
                        enabledProperty,
                        new GUIContent("Start With Tick Enabled"));
                }
            }

            if (Application.isPlaying)
            {
                EditorGUILayout.HelpBox(
                    "Use SetActorTickPhase and SetActorTickEnabled for runtime changes.",
                    MessageType.None);
            }
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
        }

        private static void DrawPaddedPropertyField(SerializedProperty property, float leftPadding)
        {
            float propertyHeight = EditorGUI.GetPropertyHeight(property, true);
            Rect rect = EditorGUILayout.GetControlRect(true, propertyHeight);
            rect.xMin += leftPadding;
            EditorGUI.PropertyField(rect, property, true);
        }

        private static void DrawFoldoutTriangle(Rect rect, bool expanded)
        {
            Vector2 center = rect.center;

            if (expanded)
            {
                foldoutTrianglePoints[0] = new Vector3(center.x - 4f, center.y - 2f);
                foldoutTrianglePoints[1] = new Vector3(center.x + 4f, center.y - 2f);
                foldoutTrianglePoints[2] = new Vector3(center.x, center.y + 3f);
            }
            else
            {
                foldoutTrianglePoints[0] = new Vector3(center.x - 2f, center.y - 4f);
                foldoutTrianglePoints[1] = new Vector3(center.x - 2f, center.y + 4f);
                foldoutTrianglePoints[2] = new Vector3(center.x + 3f, center.y);
            }

            Handles.BeginGUI();
            Color previousColor = Handles.color;
            Handles.color = new Color(0.90f, 0.90f, 0.90f, 0.95f);
            Handles.DrawAAConvexPolygon(foldoutTrianglePoints);
            Handles.color = previousColor;
            Handles.EndGUI();
        }
    }
}
