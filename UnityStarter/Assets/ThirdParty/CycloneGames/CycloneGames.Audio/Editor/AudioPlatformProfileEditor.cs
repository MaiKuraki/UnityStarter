// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Audio.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Editor
{
    [CustomEditor(typeof(AudioPlatformProfile))]
    public sealed class AudioPlatformProfileEditor : UnityEditor.Editor
    {
        private SerializedProperty desktop;
        private SerializedProperty mobile;
        private SerializedProperty webGL;
        private SerializedProperty console;

        private bool showDesktop = true;
        private bool showMobile = true;
        private bool showWebGL = true;
        private bool showConsole = true;

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private bool stylesInitialized;

        private static readonly Color accentColor = new Color(0.28f, 0.62f, 0.9f);
        private static readonly Color desktopColor = new Color(0.40f, 0.72f, 0.92f);
        private static readonly Color mobileColor = new Color(0.32f, 0.78f, 0.48f);
        private static readonly Color webGLColor = new Color(0.90f, 0.64f, 0.28f);
        private static readonly Color consoleColor = new Color(0.74f, 0.52f, 0.86f);

        private void OnEnable()
        {
            desktop = serializedObject.FindProperty("desktop");
            mobile = serializedObject.FindProperty("mobile");
            webGL = serializedObject.FindProperty("webGL");
            console = serializedObject.FindProperty("console");
            stylesInitialized = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InitializeStyles();

            DrawTitle();
            EditorGUILayout.Space(5f);
            InspectorUiUtility.DrawSectionHeader(
                "Platform Runtime Policy",
                "Defines per-platform focus handling, repeat-trigger throttling, audibility culling, and category budget multipliers.",
                accentColor);

            EditorGUILayout.Space(5f);
            DrawPlatformSection("Desktop", desktop, ref showDesktop, desktopColor);
            DrawPlatformSection("Mobile", mobile, ref showMobile, mobileColor);
            DrawPlatformSection("WebGL", webGL, ref showWebGL, webGLColor);
            DrawPlatformSection("Console / Other", console, ref showConsole, consoleColor);

            serializedObject.ApplyModifiedProperties();
        }

        private void InitializeStyles()
        {
            if (stylesInitialized) return;
            stylesInitialized = true;

            titleStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 16,
                alignment = TextAnchor.MiddleCenter
            };

            subtitleStyle = new GUIStyle(EditorStyles.centeredGreyMiniLabel)
            {
                fontSize = 10
            };
        }

        private void DrawTitle()
        {
            EditorGUILayout.BeginHorizontal();
            GUILayout.FlexibleSpace();
            EditorGUILayout.LabelField("Audio Platform Profile", titleStyle, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Per-platform playback throttling and audibility culling defaults", subtitleStyle);
        }

        private void DrawPlatformSection(string title, SerializedProperty property, ref bool expanded, Color color)
        {
            expanded = InspectorUiUtility.DrawFoldoutHeader(title, expanded, color);
            if (!expanded || property == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(BuildSummary(property), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(3f);

            EditorGUILayout.PropertyField(property.FindPropertyRelative("overrideFocusMode"));
            using (new EditorGUI.DisabledScope(!property.FindPropertyRelative("overrideFocusMode").boolValue))
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative("focusMode"));
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.PropertyField(property.FindPropertyRelative("enableRepeatTriggerThrottling"));
            using (new EditorGUI.DisabledScope(!property.FindPropertyRelative("enableRepeatTriggerThrottling").boolValue))
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative("throttlePerEmitter"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("throttleScheduledPlayback"));
            }

            EditorGUILayout.Space(3f);
            EditorGUILayout.PropertyField(property.FindPropertyRelative("enableAudibilityCulling"));
            using (new EditorGUI.DisabledScope(!property.FindPropertyRelative("enableAudibilityCulling").boolValue))
            {
                EditorGUILayout.PropertyField(property.FindPropertyRelative("cullLoopingEvents"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("cull2DEvents"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("cullScheduledPlayback"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("distanceCullPadding"));
                EditorGUILayout.PropertyField(property.FindPropertyRelative("minEstimatedAudibility"));
            }

            EditorGUILayout.Space(4f);
            EditorGUILayout.LabelField("Category Multipliers", EditorStyles.boldLabel);
            DrawCategoryRow("Critical UI", property.FindPropertyRelative("criticalUI"));
            DrawCategoryRow("Gameplay SFX", property.FindPropertyRelative("gameplaySFX"));
            DrawCategoryRow("Voice", property.FindPropertyRelative("voice"));
            DrawCategoryRow("Ambient", property.FindPropertyRelative("ambient"));
            DrawCategoryRow("Music", property.FindPropertyRelative("music"));

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private static void DrawCategoryRow(string label, SerializedProperty property)
        {
            if (property == null) return;

            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUILayout.PropertyField(property.FindPropertyRelative("voiceBudgetMultiplier"), GUIContent.none);
            EditorGUILayout.PropertyField(property.FindPropertyRelative("repeatTriggerWindowSeconds"), GUIContent.none);
            EditorGUILayout.EndHorizontal();
        }

        private static string BuildSummary(SerializedProperty property)
        {
            bool overrideFocus = property.FindPropertyRelative("overrideFocusMode").boolValue;
            string focus = overrideFocus
                ? property.FindPropertyRelative("focusMode").enumDisplayNames[property.FindPropertyRelative("focusMode").enumValueIndex]
                : "Use AudioManager";
            bool throttle = property.FindPropertyRelative("enableRepeatTriggerThrottling").boolValue;
            bool culling = property.FindPropertyRelative("enableAudibilityCulling").boolValue;
            float gameplayWindow = property.FindPropertyRelative("gameplaySFX").FindPropertyRelative("repeatTriggerWindowSeconds").floatValue;
            float gameplayBudget = property.FindPropertyRelative("gameplaySFX").FindPropertyRelative("voiceBudgetMultiplier").floatValue;

            return $"Focus {focus}  |  Throttle {(throttle ? $"On ({gameplayWindow:0.###}s SFX)" : "Off")}  |  " +
                   $"Culling {(culling ? "On" : "Off")}  |  Gameplay Budget {gameplayBudget:0.##}x";
        }
    }
}
