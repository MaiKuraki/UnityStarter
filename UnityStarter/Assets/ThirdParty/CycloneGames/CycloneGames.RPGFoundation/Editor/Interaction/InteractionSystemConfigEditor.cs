using UnityEditor;
using UnityEngine;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(Runtime.Interaction.InteractionSystemConfig))]
    public sealed class InteractionSystemConfigEditor : UnityEditor.Editor
    {
        private static readonly GUIContent s_spatialLabel = new("Spatial Grid");
        private static readonly GUIContent s_detectionLabel = new("Detection Defaults");
        private static readonly GUIContent s_lodLabel = new("LOD Defaults");
        private static readonly GUIContent s_perfLabel = new("Performance");
        private static readonly GUIContent s_applyLabel = new("Apply to All Detectors in Scene");

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
        private SerializedProperty _maxLosChecksPerFrame;
        private SerializedProperty _useLosSpatialCache;

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
            _maxLosChecksPerFrame = serializedObject.FindProperty("maxLosChecksPerFrame");
            _useLosSpatialCache = serializedObject.FindProperty("useLosSpatialCache");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.LabelField(s_spatialLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_cellSize);
            EditorGUILayout.PropertyField(_is2DMode);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(s_detectionLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_maxInteractables);
            EditorGUILayout.PropertyField(_positionUpdateThreshold);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(s_lodLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_nearDistance);
            EditorGUILayout.PropertyField(_farDistance);
            EditorGUILayout.PropertyField(_disableDistance);
            EditorGUILayout.PropertyField(_nearIntervalMs);
            EditorGUILayout.PropertyField(_farIntervalMs);
            EditorGUILayout.PropertyField(_veryFarIntervalMs);
            EditorGUILayout.PropertyField(_sleepIntervalMs);
            EditorGUILayout.PropertyField(_sleepEnterMs);
            EditorGUILayout.Space();

            EditorGUILayout.LabelField(s_perfLabel, EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_maxLosChecksPerFrame);
            EditorGUILayout.PropertyField(_useLosSpatialCache);
            EditorGUILayout.Space();

            if (GUILayout.Button(s_applyLabel))
            {
                var config = (Runtime.Interaction.InteractionSystemConfig)target;
                var detectors = FindObjectsByType<Runtime.Interaction.InteractionDetector>(FindObjectsSortMode.None);
                foreach (var d in detectors)
                {
                    var so = new SerializedObject(d);
                    so.FindProperty("maxLosChecksPerFrame").intValue = config.MaxLosChecksPerFrame;
                    so.FindProperty("useLosSpatialCache").boolValue = config.UseLosSpatialCache;
                    so.ApplyModifiedProperties();
                }
                Debug.Log($"[InteractionSystemConfig] Applied to {detectors.Length} detector(s) in scene.");
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
