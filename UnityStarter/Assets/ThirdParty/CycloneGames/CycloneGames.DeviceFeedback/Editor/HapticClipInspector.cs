using CycloneGames.DeviceFeedback.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.DeviceFeedback.Editor
{
    /// <summary>
    /// Inspector for HapticClip. Edits go through SerializedObject so Undo,
    /// Prefab overrides and multi-object editing work.
    /// </summary>
    [CustomEditor(typeof(HapticClip), true)]
    [CanEditMultipleObjects]
    public sealed class HapticClipInspector : UnityEditor.Editor
    {
        private SerializedProperty _duration;
        private SerializedProperty _intensityCurve;
        private SerializedProperty _sharpnessCurve;
        private SerializedProperty _events;

        private static readonly GUIContent EventModeHelp = new GUIContent(
            "Event mode is active: the curves below are ignored at runtime.");
        private static readonly GUIContent CurveModeHelp = new GUIContent(
            "Curve mode is active: add at least one event to switch to event mode.");
        private static readonly GUIContent DurationLabel = new GUIContent(
            "Duration (s)", "Total clip duration used in curve mode.");

        private void OnEnable()
        {
            _duration = serializedObject.FindProperty("duration");
            _intensityCurve = serializedObject.FindProperty("intensityCurve");
            _sharpnessCurve = serializedObject.FindProperty("sharpnessCurve");
            _events = serializedObject.FindProperty("events");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            bool eventMode = _events != null && _events.arraySize > 0;

            EditorGUILayout.HelpBox(
                eventMode ? EventModeHelp.text : CurveModeHelp.text,
                MessageType.Info);

            EditorGUILayout.PropertyField(_duration, DurationLabel);
            if (!eventMode)
            {
                EditorGUILayout.PropertyField(_intensityCurve, true);
                EditorGUILayout.PropertyField(_sharpnessCurve, true);
            }

            EditorGUILayout.PropertyField(_events, true);

            int invalid = CountInvalidEvents();
            if (invalid > 0)
            {
                EditorGUILayout.HelpBox(
                    invalid + " event(s) contain negative or non-finite values. " +
                    "These events are rejected at runtime admission.",
                    MessageType.Warning);
            }

            if (_events != null && _events.arraySize > DeviceFeedbackLimits.DefaultMaximumHapticEventCount)
            {
                EditorGUILayout.HelpBox(
                    "Event count exceeds the default admission limit (" +
                    DeviceFeedbackLimits.DefaultMaximumHapticEventCount + ").",
                    MessageType.Warning);
            }

            serializedObject.ApplyModifiedProperties();
        }

        private int CountInvalidEvents()
        {
            if (_events == null) return 0;
            int invalid = 0;
            for (int i = 0; i < _events.arraySize; i++)
            {
                SerializedProperty element = _events.GetArrayElementAtIndex(i);
                SerializedProperty time = element.FindPropertyRelative("time");
                SerializedProperty duration = element.FindPropertyRelative("duration");
                SerializedProperty intensity = element.FindPropertyRelative("intensity");
                SerializedProperty sharpness = element.FindPropertyRelative("sharpness");
                if (IsInvalid(time) || IsInvalid(duration) || IsInvalid(intensity) || IsInvalid(sharpness))
                {
                    invalid++;
                }
            }
            return invalid;
        }

        private static bool IsInvalid(SerializedProperty property)
        {
            if (property == null) return true;
            float value = property.floatValue;
            return float.IsNaN(value) || float.IsInfinity(value) || value < 0f;
        }
    }
}
