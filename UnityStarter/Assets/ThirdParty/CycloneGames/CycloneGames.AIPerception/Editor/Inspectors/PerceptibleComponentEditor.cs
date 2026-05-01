using UnityEngine;
using UnityEditor;
using CycloneGames.AIPerception.Runtime;

namespace CycloneGames.AIPerception.Editor
{
    [CustomEditor(typeof(PerceptibleComponent), true)]  // true = supports derived classes
    [CanEditMultipleObjects]
    public class PerceptibleComponentEditor : UnityEditor.Editor
    {
        private static readonly Color HeaderColor = new Color(0.2f, 0.8f, 0.6f, 1f);
        private static readonly Color DerivedSectionBgColor = new Color(0.6f, 0.4f, 0.8f, 0.25f);
        private static readonly Color DerivedLabelColor = new Color(0.8f, 0.6f, 1f);
        private static readonly Color RuntimeStatsBgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        private static readonly Color RuntimeStatsTextColor = new Color(0.8f, 0.8f, 0.8f);

        // Cached GUIStyles (0-allocation in OnInspectorGUI)
        private static GUIStyle _headerStyle;
        private static GUIStyle _derivedLabelStyle;
        private static GUIStyle _runtimeStatsStyle;

        // Cached GUIContent (0-allocation in OnInspectorGUI)
        private static readonly GUIContent LabelTypeId = new GUIContent("Type ID");
        private static readonly GUIContent LabelTag = new GUIContent("Tag");
        private static readonly GUIContent LabelDetectionRadius = new GUIContent("Detection Radius");
        private static readonly GUIContent LabelIsDetectable = new GUIContent("Is Detectable");
        private static readonly GUIContent LabelLosPoint = new GUIContent("LOS Point (Optional)");
        private static readonly GUIContent LabelIsSoundSource = new GUIContent("Is Sound Source");
        private static readonly GUIContent LabelLoudness = new GUIContent("Loudness");
        private static readonly GUIContent LabelShowDebugOverlay = new GUIContent("Show Debug Overlay");
        private static readonly GUIContent LabelDerivedFields = new GUIContent("Custom Fields");

        private SerializedProperty _typeId;
        private SerializedProperty _tag;
        private SerializedProperty _detectionRadius;
        private SerializedProperty _isDetectable;
        private SerializedProperty _losPoint;
        private SerializedProperty _loudness;
        private SerializedProperty _isSoundSource;
        private SerializedProperty _showDebugOverlay;

        private bool _showDerivedFieldsFoldout = true;

        protected virtual void OnEnable()
        {
            _typeId = serializedObject.FindProperty("_typeId");
            _tag = serializedObject.FindProperty("_tag");
            _detectionRadius = serializedObject.FindProperty("_detectionRadius");
            _isDetectable = serializedObject.FindProperty("_isDetectable");
            _losPoint = serializedObject.FindProperty("_losPoint");
            _loudness = serializedObject.FindProperty("_loudness");
            _isSoundSource = serializedObject.FindProperty("_isSoundSource");
            _showDebugOverlay = serializedObject.FindProperty("_showDebugOverlay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCustomHeader();
            DrawMainSection();

            // Draw derived class fields
            DrawDerivedClassFields();

            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void DrawCustomHeader()
        {
            EditorGUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 14,
                    alignment = TextAnchor.MiddleCenter
                };
                _headerStyle.normal.textColor = Color.white;
            }

            EditorGUI.LabelField(rect, "Perceptible", _headerStyle);
            EditorGUILayout.Space(4);
        }

        protected virtual void DrawDerivedClassFields()
        {
            // Check if this is a derived class
            if (target.GetType() == typeof(PerceptibleComponent)) return;

            // Find and draw fields from derived classes
            var iterator = serializedObject.GetIterator();
            bool hasFields = false;

            if (_derivedLabelStyle == null)
            {
                _derivedLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                _derivedLabelStyle.normal.textColor = DerivedLabelColor;
            }

            // Collect derived class fields
            if (iterator.NextVisible(true)) // Skip script field
            {
                while (iterator.NextVisible(false))
                {
                    // Skip base class fields
                    if (IsBaseClassField(iterator.name)) continue;

                    if (!hasFields)
                    {
                        hasFields = true;
                        EditorGUILayout.Space(8);
                        var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(bgRect, DerivedSectionBgColor);

                        var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
                        _showDerivedFieldsFoldout = EditorGUI.Foldout(foldoutRect, _showDerivedFieldsFoldout, GUIContent.none, true);

                        var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
                        EditorGUI.LabelField(labelRect, LabelDerivedFields, _derivedLabelStyle);
                    }

                    if (_showDerivedFieldsFoldout)
                    {
                        EditorGUI.indentLevel++;
                        EditorGUILayout.PropertyField(iterator, true);
                        EditorGUI.indentLevel--;
                    }
                }
            }
        }

        private bool IsBaseClassField(string fieldName)
        {
            return fieldName == "_typeId" || fieldName == "_tag" ||
                   fieldName == "_detectionRadius" || fieldName == "_isDetectable" ||
                   fieldName == "_losPoint" || fieldName == "_loudness" ||
                   fieldName == "_isSoundSource" || fieldName == "_showDebugOverlay" ||
                   fieldName == "_debugToggleKey";
        }

        protected virtual void DrawMainSection()
        {
            EditorGUILayout.LabelField("Type", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_typeId, LabelTypeId);
            EditorGUILayout.PropertyField(_tag, LabelTag);

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Detection", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_detectionRadius, LabelDetectionRadius);
            EditorGUILayout.PropertyField(_isDetectable, LabelIsDetectable);
            EditorGUILayout.PropertyField(_losPoint, LabelLosPoint);

            if (_losPoint.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No LOS Point set. Using transform position for line-of-sight checks.", MessageType.Info);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Sound", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_isSoundSource, LabelIsSoundSource);
            if (_isSoundSource.boolValue)
            {
                EditorGUILayout.PropertyField(_loudness, LabelLoudness);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Debug", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_showDebugOverlay, LabelShowDebugOverlay);

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(8);
                DrawRuntimeInfo();
            }
        }

        protected virtual void DrawRuntimeInfo()
        {
            var perceptible = (PerceptibleComponent)target;

            var statsRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(statsRect, RuntimeStatsBgColor);

            if (_runtimeStatsStyle == null)
            {
                _runtimeStatsStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
                _runtimeStatsStyle.normal.textColor = RuntimeStatsTextColor;
            }

            EditorGUI.LabelField(statsRect, "Runtime Info", _runtimeStatsStyle);

            GUI.enabled = false;
            EditorGUILayout.IntField("ID", perceptible.PerceptibleId);
            EditorGUILayout.Toggle("Handle Valid", perceptible.Handle.IsValid);
            EditorGUILayout.Vector3Field("Position", perceptible.Position);
            EditorGUILayout.LabelField("Type Name", PerceptibleTypes.GetTypeName(perceptible.PerceptibleTypeId));
            GUI.enabled = true;

            var detectors = perceptible.GetDetectors();
            if (detectors.Count > 0)
            {
                EditorGUILayout.HelpBox($"Detected by {detectors.Count} AI(s)!", MessageType.Warning);
            }

            if (GUILayout.Button(perceptible.ShowDebugOverlay ? "Hide Debug Overlay" : "Show Debug Overlay"))
            {
                perceptible.ShowDebugOverlay = !perceptible.ShowDebugOverlay;
            }

            Repaint();
        }
    }
}
