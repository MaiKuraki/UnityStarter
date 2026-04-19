// Copyright (c) CycloneGames
// Licensed under the MIT License.

using CycloneGames.Audio.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.Audio.Editor
{
    [CustomEditor(typeof(AudioVoicePolicyProfile))]
    public sealed class AudioVoicePolicyProfileEditor : UnityEditor.Editor
    {
        private static string[] allConfigGuids;
        private static bool hasCheckedForDuplicates;

        private bool showCriticalUI = true;
        private bool showGameplaySFX = true;
        private bool showVoice = true;
        private bool showAmbient = true;
        private bool showMusic = true;

        private SerializedProperty criticalUI;
        private SerializedProperty gameplaySFX;
        private SerializedProperty voice;
        private SerializedProperty ambient;
        private SerializedProperty music;

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private bool stylesInitialized;

        private static readonly Color accentColor = new Color(0.25f, 0.6f, 0.9f);
        private static readonly Color criticalColor = new Color(0.84f, 0.35f, 0.30f);
        private static readonly Color gameplayColor = new Color(0.29f, 0.62f, 0.38f);
        private static readonly Color voiceColor = new Color(0.33f, 0.52f, 0.82f);
        private static readonly Color ambientColor = new Color(0.28f, 0.62f, 0.70f);
        private static readonly Color musicColor = new Color(0.68f, 0.46f, 0.82f);
        private static readonly Color warningColor = new Color(0.9f, 0.7f, 0.3f);

        private void OnEnable()
        {
            criticalUI = serializedObject.FindProperty("criticalUI");
            gameplaySFX = serializedObject.FindProperty("gameplaySFX");
            voice = serializedObject.FindProperty("voice");
            ambient = serializedObject.FindProperty("ambient");
            music = serializedObject.FindProperty("music");
            stylesInitialized = false;
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            InitializeStyles();
            CheckForDuplicates();

            DrawTitle();
            EditorGUILayout.Space(5);
            DrawDuplicateWarning();
            InspectorUiUtility.DrawSectionHeader(
                "Project-Level Category Defaults",
                "AudioEvents with 'Use Category Defaults' enabled will resolve their runtime steal policy from this asset.",
                accentColor);

            EditorGUILayout.Space(5);
            DrawCategorySection("Critical UI", criticalUI, ref showCriticalUI, criticalColor);
            DrawCategorySection("Gameplay SFX", gameplaySFX, ref showGameplaySFX, gameplayColor);
            DrawCategorySection("Voice", voice, ref showVoice, voiceColor);
            DrawCategorySection("Ambient", ambient, ref showAmbient, ambientColor);
            DrawCategorySection("Music", music, ref showMusic, musicColor);

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
            EditorGUILayout.LabelField("Audio Voice Policy Profile", titleStyle, GUILayout.Height(24));
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            EditorGUILayout.LabelField("Category-driven runtime steal policy defaults", subtitleStyle);
        }

        private void CheckForDuplicates()
        {
            if (!hasCheckedForDuplicates || Event.current.type == EventType.Layout)
            {
                allConfigGuids = AssetDatabase.FindAssets("t:AudioVoicePolicyProfile");
                hasCheckedForDuplicates = true;
            }
        }

        private void DrawDuplicateWarning()
        {
            if (allConfigGuids == null || allConfigGuids.Length <= 1)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            GUI.color = warningColor;
            EditorGUILayout.LabelField("Multiple Voice Policy Profiles Detected", EditorStyles.boldLabel);
            GUI.color = Color.white;
            EditorGUILayout.LabelField($"Found {allConfigGuids.Length} AudioVoicePolicyProfile assets. Only one should exist.", EditorStyles.wordWrappedLabel);
            if (GUILayout.Button("Show All in Console"))
            {
                foreach (string guid in allConfigGuids)
                {
                    string path = AssetDatabase.GUIDToAssetPath(guid);
                    Debug.Log($"AudioVoicePolicyProfile found at: {path}");
                }
            }

            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(5);
        }

        private void DrawCategorySection(string title, SerializedProperty property, ref bool expanded, Color color)
        {
            expanded = InspectorUiUtility.DrawFoldoutHeader(title, expanded, color);
            if (!expanded || property == null)
                return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(BuildSummary(property), EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.Space(2f);
            EditorGUILayout.PropertyField(property.FindPropertyRelative("stealResistance"));
            EditorGUILayout.PropertyField(property.FindPropertyRelative("voiceBudgetWeight"));
            EditorGUILayout.PropertyField(property.FindPropertyRelative("allowVoiceSteal"));
            EditorGUILayout.PropertyField(property.FindPropertyRelative("allowDistanceBasedSteal"));
            EditorGUILayout.PropertyField(property.FindPropertyRelative("protectScheduledPlayback"));
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        private static string BuildSummary(SerializedProperty property)
        {
            SerializedProperty stealResistance = property.FindPropertyRelative("stealResistance");
            SerializedProperty voiceBudgetWeight = property.FindPropertyRelative("voiceBudgetWeight");
            SerializedProperty allowVoiceSteal = property.FindPropertyRelative("allowVoiceSteal");
            SerializedProperty allowDistanceBasedSteal = property.FindPropertyRelative("allowDistanceBasedSteal");
            SerializedProperty protectScheduledPlayback = property.FindPropertyRelative("protectScheduledPlayback");

            return $"Resistance {stealResistance.floatValue:0.##}  |  Budget {voiceBudgetWeight.floatValue:0.##}  |  " +
                   $"Steal {(allowVoiceSteal.boolValue ? "On" : "Off")}  |  " +
                   $"Distance {(allowDistanceBasedSteal.boolValue ? "On" : "Off")}  |  " +
                   $"Scheduled {(protectScheduledPlayback.boolValue ? "On" : "Off")}";
        }
    }
}
