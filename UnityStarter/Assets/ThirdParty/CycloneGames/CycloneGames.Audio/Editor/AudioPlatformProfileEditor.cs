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
        // ---- Per-platform outer foldout ----
        private bool showDesktop = true;
        private bool showMobile  = true;
        private bool showWebGL   = true;
        private bool showConsole = true;

        // ---- Per-platform inner section foldouts ----
        private struct PlatformFolds
        {
            public bool policy;
            public bool culling;
            public bool categories;
            public bool lod;
            public bool occlusion;

            // Default: Playback Policy open, everything else collapsed
            public static PlatformFolds Default => new PlatformFolds
            {
                policy = true, culling = false, categories = false, lod = false, occlusion = false
            };
        }

        private PlatformFolds desktopFolds = PlatformFolds.Default;
        private PlatformFolds mobileFolds  = PlatformFolds.Default;
        private PlatformFolds webGLFolds   = PlatformFolds.Default;
        private PlatformFolds consoleFolds = PlatformFolds.Default;

        private SerializedProperty desktop;
        private SerializedProperty mobile;
        private SerializedProperty webGL;
        private SerializedProperty console;

        private GUIStyle titleStyle;
        private GUIStyle subtitleStyle;
        private bool stylesInitialized;

        private static readonly Color accentColor  = new Color(0.28f, 0.62f, 0.9f);
        private static readonly Color desktopColor = new Color(0.40f, 0.72f, 0.92f);
        private static readonly Color mobileColor  = new Color(0.32f, 0.78f, 0.48f);
        private static readonly Color webGLColor   = new Color(0.90f, 0.64f, 0.28f);
        private static readonly Color consoleColor = new Color(0.74f, 0.52f, 0.86f);

        private void OnEnable()
        {
            desktop = serializedObject.FindProperty("desktop");
            mobile  = serializedObject.FindProperty("mobile");
            webGL   = serializedObject.FindProperty("webGL");
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
            DrawPlatformSection("Desktop",         desktop, ref showDesktop, desktopColor, ref desktopFolds);
            DrawPlatformSection("Mobile",          mobile,  ref showMobile,  mobileColor,  ref mobileFolds);
            DrawPlatformSection("WebGL",           webGL,   ref showWebGL,   webGLColor,   ref webGLFolds);
            DrawPlatformSection("Console / Other", console, ref showConsole, consoleColor, ref consoleFolds);

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

        private void DrawPlatformSection(
            string title, SerializedProperty property,
            ref bool expanded, Color color, ref PlatformFolds folds)
        {
            expanded = InspectorUiUtility.DrawFoldoutHeader(title, expanded, color);
            if (!expanded || property == null) return;

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);

            // One-line summary across the top
            EditorGUILayout.LabelField(BuildSummary(property), EditorStyles.wordWrappedMiniLabel);

            // =========================================================
            // Sub-section: Playback Policy
            // Badge: whether repeat-trigger throttling is on
            // =========================================================
            EditorGUILayout.Space(3f);
            bool throttleOn = property.FindPropertyRelative("enableRepeatTriggerThrottling").boolValue;
            folds.policy = InspectorUiUtility.DrawSubFoldoutHeader("Playback Policy", folds.policy, throttleOn);
            if (folds.policy)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(property.FindPropertyRelative("overrideFocusMode"));
                using (new EditorGUI.DisabledScope(!property.FindPropertyRelative("overrideFocusMode").boolValue))
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("focusMode"));

                EditorGUILayout.Space(2f);
                EditorGUILayout.PropertyField(property.FindPropertyRelative("enableRepeatTriggerThrottling"));
                using (new EditorGUI.DisabledScope(!throttleOn))
                {
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("throttlePerEmitter"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("throttleScheduledPlayback"));
                }
                EditorGUI.indentLevel--;
            }

            // =========================================================
            // Sub-section: Audibility Culling
            // Badge: enableAudibilityCulling
            // =========================================================
            EditorGUILayout.Space(3f);
            bool cullingOn = property.FindPropertyRelative("enableAudibilityCulling").boolValue;
            folds.culling = InspectorUiUtility.DrawSubFoldoutHeader("Audibility Culling", folds.culling, cullingOn);
            if (folds.culling)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(property.FindPropertyRelative("enableAudibilityCulling"));
                using (new EditorGUI.DisabledScope(!cullingOn))
                {
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("cullLoopingEvents"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("cull2DEvents"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("cullScheduledPlayback"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("distanceCullPadding"));
                    EditorGUILayout.PropertyField(property.FindPropertyRelative("minEstimatedAudibility"));
                }
                EditorGUI.indentLevel--;
            }

            // =========================================================
            // Sub-section: Category Multipliers  (no enabled toggle)
            // =========================================================
            EditorGUILayout.Space(3f);
            folds.categories = InspectorUiUtility.DrawSubFoldoutHeader("Category Multipliers", folds.categories);
            if (folds.categories)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField(GUIContent.none, GUILayout.Width(114f));
                EditorGUILayout.LabelField("Budget Mult.", EditorStyles.miniLabel);
                EditorGUILayout.LabelField("Repeat Window (s)", EditorStyles.miniLabel);
                EditorGUILayout.EndHorizontal();
                DrawCategoryRow("Critical UI",  property.FindPropertyRelative("criticalUI"));
                DrawCategoryRow("Gameplay SFX", property.FindPropertyRelative("gameplaySFX"));
                DrawCategoryRow("Voice",        property.FindPropertyRelative("voice"));
                DrawCategoryRow("Ambient",      property.FindPropertyRelative("ambient"));
                DrawCategoryRow("Music",        property.FindPropertyRelative("music"));
                EditorGUI.indentLevel--;
            }

            // =========================================================
            // Sub-section: Update LOD
            // Badge: updateLOD.enabled
            // =========================================================
            EditorGUILayout.Space(3f);
            var lodProp = property.FindPropertyRelative("updateLOD");
            bool lodOn = lodProp != null && lodProp.FindPropertyRelative("enabled").boolValue;
            folds.lod = InspectorUiUtility.DrawSubFoldoutHeader("Update LOD", folds.lod, lodOn);
            if (folds.lod) DrawUpdateLODContent(lodProp);

            // =========================================================
            // Sub-section: Occlusion
            // Badge: occlusion.enabled
            // =========================================================
            EditorGUILayout.Space(3f);
            var occProp = property.FindPropertyRelative("occlusion");
            bool occOn = occProp != null && occProp.FindPropertyRelative("enabled").boolValue;
            folds.occlusion = InspectorUiUtility.DrawSubFoldoutHeader("Occlusion", folds.occlusion, occOn);
            if (folds.occlusion) DrawOcclusionContent(occProp);

            EditorGUILayout.Space(2f);
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(3f);
        }

        // -----------------------------------------------------------------
        // Update LOD inner content
        // -----------------------------------------------------------------
        private static void DrawUpdateLODContent(SerializedProperty lod)
        {
            if (lod == null) return;
            EditorGUI.indentLevel++;

            var enabledProp = lod.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enable Distance LOD"));

            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                EditorGUILayout.PropertyField(lod.FindPropertyRelative("recalcFrameInterval"),
                    new GUIContent("Recalc Interval (frames)", "How often each event's LOD tier is recalculated."));

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Distance Thresholds (meters)", EditorStyles.miniLabel);
                EditorGUILayout.PropertyField(lod.FindPropertyRelative("nearDistance"), new GUIContent("Near Distance"));
                EditorGUILayout.PropertyField(lod.FindPropertyRelative("midDistance"),  new GUIContent("Mid Distance"));

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Update Intervals (frames)", EditorStyles.miniLabel);
                DrawLODIntervalRow("Near", lod.FindPropertyRelative("nearUpdateInterval"));
                DrawLODIntervalRow("Mid",  lod.FindPropertyRelative("midUpdateInterval"));
                DrawLODIntervalRow("Far",  lod.FindPropertyRelative("farUpdateInterval"));

                if (enabledProp.boolValue)
                {
                    float near  = lod.FindPropertyRelative("nearDistance").floatValue;
                    float mid   = lod.FindPropertyRelative("midDistance").floatValue;
                    int   nearI = lod.FindPropertyRelative("nearUpdateInterval").intValue;
                    int   midI  = lod.FindPropertyRelative("midUpdateInterval").intValue;
                    int   farI  = lod.FindPropertyRelative("farUpdateInterval").intValue;
                    EditorGUILayout.HelpBox(
                        $"<{near:0}m: every {nearI} frame(s)  |  <{mid:0}m: every {midI}  |  >{mid:0}m: every {farI}",
                        MessageType.Info);
                }
            }
            EditorGUI.indentLevel--;
        }

        // -----------------------------------------------------------------
        // Occlusion inner content
        // -----------------------------------------------------------------
        private static void DrawOcclusionContent(SerializedProperty occ)
        {
            if (occ == null) return;
            EditorGUI.indentLevel++;

            var enabledProp = occ.FindPropertyRelative("enabled");
            EditorGUILayout.PropertyField(enabledProp, new GUIContent("Enable Occlusion"));

            using (new EditorGUI.DisabledScope(!enabledProp.boolValue))
            {
                EditorGUILayout.PropertyField(occ.FindPropertyRelative("occlusionLayers"),
                    new GUIContent("Occlusion Layers", "Physics layers that block sound. Walls, terrain, etc."));
                EditorGUILayout.PropertyField(occ.FindPropertyRelative("maxOcclusionDistance"),
                    new GUIContent("Max Distance (m)", "Events beyond this distance skip occlusion raycasts."));
                EditorGUILayout.PropertyField(occ.FindPropertyRelative("occludedCutoffHz"),
                    new GUIContent("Occluded LP Cutoff (Hz)", "Low-pass cutoff when fully occluded. Lower = more muffled."));
                EditorGUILayout.PropertyField(occ.FindPropertyRelative("occludedVolumeScale"),
                    new GUIContent("Occluded Volume Scale", "Volume multiplier when fully occluded."));
                EditorGUILayout.PropertyField(occ.FindPropertyRelative("interpolationSpeed"),
                    new GUIContent("Interpolation Speed", "How fast occlusion fades in/out. Higher = snappier."));

                if (enabledProp.boolValue)
                {
                    float cutoff   = occ.FindPropertyRelative("occludedCutoffHz").floatValue;
                    float volScale = occ.FindPropertyRelative("occludedVolumeScale").floatValue;
                    float maxDist  = occ.FindPropertyRelative("maxOcclusionDistance").floatValue;
                    EditorGUILayout.HelpBox(
                        $"Occluded: LP {cutoff:0}Hz  |  Volume x{volScale:0.##}  |  Raycasts up to {maxDist:0}m",
                        MessageType.Info);
                }
            }
            EditorGUI.indentLevel--;
        }

        // -----------------------------------------------------------------
        // Helpers
        // -----------------------------------------------------------------
        private static void DrawLODIntervalRow(string label, SerializedProperty prop)
        {
            if (prop == null) return;
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.LabelField(label, GUILayout.Width(110f));
            EditorGUILayout.PropertyField(prop, GUIContent.none);
            EditorGUILayout.EndHorizontal();
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
                ? property.FindPropertyRelative("focusMode").enumDisplayNames[
                    property.FindPropertyRelative("focusMode").enumValueIndex]
                : "All";
            bool  throttle       = property.FindPropertyRelative("enableRepeatTriggerThrottling").boolValue;
            bool  culling        = property.FindPropertyRelative("enableAudibilityCulling").boolValue;
            float gameplayWindow = property.FindPropertyRelative("gameplaySFX")
                .FindPropertyRelative("repeatTriggerWindowSeconds").floatValue;
            float gameplayBudget = property.FindPropertyRelative("gameplaySFX")
                .FindPropertyRelative("voiceBudgetMultiplier").floatValue;
            var  lodProp    = property.FindPropertyRelative("updateLOD");
            bool lodEnabled = lodProp != null && lodProp.FindPropertyRelative("enabled").boolValue;
            var  occProp    = property.FindPropertyRelative("occlusion");
            bool occEnabled = occProp != null && occProp.FindPropertyRelative("enabled").boolValue;

            return $"Focus {focus}  |  Throttle {(throttle ? $"On ({gameplayWindow:0.###}s SFX)" : "Off")}  |  " +
                   $"Culling {(culling ? "On" : "Off")}  |  LOD {(lodEnabled ? "On" : "Off")}  |  " +
                   $"Occlusion {(occEnabled ? "On" : "Off")}  |  Gameplay Budget {gameplayBudget:0.##}x";
        }
    }
}