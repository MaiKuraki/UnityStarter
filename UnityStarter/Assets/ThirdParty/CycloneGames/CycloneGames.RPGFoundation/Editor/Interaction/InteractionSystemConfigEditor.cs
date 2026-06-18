using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(InteractionSystemConfig))]
    public sealed class InteractionSystemConfigEditor : UnityEditor.Editor
    {
        private SerializedProperty _cellSize;
        private SerializedProperty _is2DMode;
        private SerializedProperty _maxInteractables;
        private SerializedProperty _positionUpdateThreshold;
        private SerializedProperty _nearDistance;
        private SerializedProperty _farDistance;
        private SerializedProperty _disableDistance;
        private SerializedProperty _nearIntervalMs;
        private SerializedProperty _farIntervalMs;
        private SerializedProperty _veryFarIntervalMs;
        private SerializedProperty _sleepIntervalMs;
        private SerializedProperty _sleepEnterMs;
        private SerializedProperty _autoInteractMinIntervalMs;
        private SerializedProperty _maxLosChecksPerFrame;
        private SerializedProperty _blockWhenLosBudgetExceeded;
        private SerializedProperty _useLosSpatialCache;

        private static bool s_spatialFoldout = true;
        private static bool s_detectionFoldout = true;
        private static bool s_lodFoldout = true;
        private static bool s_perfFoldout = true;

        private void OnEnable()
        {
            _cellSize = serializedObject.FindProperty("cellSize");
            _is2DMode = serializedObject.FindProperty("is2DMode");
            _maxInteractables = serializedObject.FindProperty("maxInteractables");
            _positionUpdateThreshold = serializedObject.FindProperty("positionUpdateThreshold");
            _nearDistance = serializedObject.FindProperty("nearDistance");
            _farDistance = serializedObject.FindProperty("farDistance");
            _disableDistance = serializedObject.FindProperty("disableDistance");
            _nearIntervalMs = serializedObject.FindProperty("nearIntervalMs");
            _farIntervalMs = serializedObject.FindProperty("farIntervalMs");
            _veryFarIntervalMs = serializedObject.FindProperty("veryFarIntervalMs");
            _sleepIntervalMs = serializedObject.FindProperty("sleepIntervalMs");
            _sleepEnterMs = serializedObject.FindProperty("sleepEnterMs");
            _autoInteractMinIntervalMs = serializedObject.FindProperty("autoInteractMinIntervalMs");
            _maxLosChecksPerFrame = serializedObject.FindProperty("maxLosChecksPerFrame");
            _blockWhenLosBudgetExceeded = serializedObject.FindProperty("blockWhenLosBudgetExceeded");
            _useLosSpatialCache = serializedObject.FindProperty("useLosSpatialCache");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawSpatialSettings();
            DrawDetectionSettings();
            DrawLodSettings();
            DrawPerformanceSettings();
            DrawApplyTools();

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSpatialSettings()
        {
            s_spatialFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Spatial Grid",
                s_spatialFoldout,
                InteractionInspectorUiUtility.ColorCore);
            if (!s_spatialFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_cellSize);
                EditorGUILayout.PropertyField(_is2DMode);
                InteractionInspectorUiUtility.DrawHelpBox(
                    "These settings are consumed by InteractionSystem. Runtime systems already initialized in play mode need an explicit reinitialize path if you change these values live.",
                    MessageType.None);
            }
        }

        private void DrawDetectionSettings()
        {
            s_detectionFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Detection Defaults",
                s_detectionFoldout,
                InteractionInspectorUiUtility.ColorBehavior);
            if (!s_detectionFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_maxInteractables);
                EditorGUILayout.PropertyField(_positionUpdateThreshold);
            }
        }

        private void DrawLodSettings()
        {
            s_lodFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "LOD Defaults",
                s_lodFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_lodFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_nearDistance);
                EditorGUILayout.PropertyField(_farDistance);
                EditorGUILayout.PropertyField(_disableDistance);
                EditorGUILayout.PropertyField(_nearIntervalMs);
                EditorGUILayout.PropertyField(_farIntervalMs);
                EditorGUILayout.PropertyField(_veryFarIntervalMs);
                EditorGUILayout.PropertyField(_sleepIntervalMs);
                EditorGUILayout.PropertyField(_sleepEnterMs);
            }
        }

        private void DrawPerformanceSettings()
        {
            s_perfFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Performance",
                s_perfFoldout,
                InteractionInspectorUiUtility.ColorDebug);
            if (!s_perfFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_maxLosChecksPerFrame);
                EditorGUILayout.PropertyField(_blockWhenLosBudgetExceeded);
                EditorGUILayout.PropertyField(_useLosSpatialCache);
                EditorGUILayout.PropertyField(_autoInteractMinIntervalMs);
            }
        }

        private void DrawApplyTools()
        {
            EditorGUILayout.Space(4f);
            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                InteractionInspectorUiUtility.DrawHelpBox(
                    "Applies matching defaults to all InteractionDetector components in the open scene. This records Undo for each changed detector.",
                    MessageType.None);

                if (GUILayout.Button("Apply To All Detectors In Scene"))
                    ApplyToAllDetectors();
            }
        }

        private void ApplyToAllDetectors()
        {
            var config = (InteractionSystemConfig)target;
            var detectors = FindObjectsByType<InteractionDetector>(FindObjectsSortMode.None);

            for (int i = 0; i < detectors.Length; i++)
            {
                InteractionDetector detector = detectors[i];
                Undo.RecordObject(detector, "Apply Interaction System Config");

                var so = new SerializedObject(detector);
                SetInt(so, "maxInteractables", config.MaxInteractables);
                SetFloat(so, "nearDistance", config.NearDistance);
                SetFloat(so, "farDistance", config.FarDistance);
                SetFloat(so, "disableDistance", config.DisableDistance);
                SetFloat(so, "nearIntervalMs", config.NearIntervalMs);
                SetFloat(so, "farIntervalMs", config.FarIntervalMs);
                SetFloat(so, "veryFarIntervalMs", config.VeryFarIntervalMs);
                SetFloat(so, "sleepIntervalMs", config.SleepIntervalMs);
                SetFloat(so, "sleepEnterMs", config.SleepEnterMs);
                SetFloat(so, "autoInteractMinIntervalMs", config.AutoInteractMinIntervalMs);
                SetInt(so, "maxLosChecksPerFrame", config.MaxLosChecksPerFrame);
                SetBool(so, "blockWhenLosBudgetExceeded", config.BlockWhenLosBudgetExceeded);
                SetBool(so, "useLosSpatialCache", config.UseLosSpatialCache);
                so.ApplyModifiedProperties();
                EditorUtility.SetDirty(detector);
            }

            Debug.Log("[InteractionSystemConfig] Applied to " + detectors.Length + " detector(s) in scene.");
        }

        private static void SetInt(SerializedObject so, string propertyName, int value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property != null)
                property.intValue = value;
        }

        private static void SetFloat(SerializedObject so, string propertyName, float value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property != null)
                property.floatValue = value;
        }

        private static void SetBool(SerializedObject so, string propertyName, bool value)
        {
            SerializedProperty property = so.FindProperty(propertyName);
            if (property != null)
                property.boolValue = value;
        }
    }
}
