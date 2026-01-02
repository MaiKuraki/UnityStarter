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

        /// <summary>
        /// Override this to customize the header drawing.
        /// </summary>
        protected virtual void DrawCustomHeader()
        {
            EditorGUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(0, 28, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                fontSize = 14,
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = Color.white;

            EditorGUI.LabelField(rect, "Perceptible", style);
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws any serialized fields from derived classes.
        /// Override this to customize derived field drawing.
        /// </summary>
        protected virtual void DrawDerivedClassFields()
        {
            // Check if this is a derived class
            if (target.GetType() == typeof(PerceptibleComponent)) return;

            // Find and draw fields from derived classes
            var iterator = serializedObject.GetIterator();
            bool hasFields = false;

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
                        EditorGUI.DrawRect(bgRect, new Color(0.6f, 0.4f, 0.8f, 0.25f));

                        var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
                        _showDerivedFieldsFoldout = EditorGUI.Foldout(foldoutRect, _showDerivedFieldsFoldout, GUIContent.none, true);

                        var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
                        var labelStyle = new GUIStyle(EditorStyles.boldLabel);
                        labelStyle.normal.textColor = new Color(0.8f, 0.6f, 1f);
                        EditorGUI.LabelField(labelRect, "📦 Custom Fields", labelStyle);
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
            EditorGUILayout.PropertyField(_typeId, new GUIContent("Type ID"));
            EditorGUILayout.PropertyField(_tag, new GUIContent("Tag"));

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Detection", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_detectionRadius, new GUIContent("Detection Radius"));
            EditorGUILayout.PropertyField(_isDetectable, new GUIContent("Is Detectable"));
            EditorGUILayout.PropertyField(_losPoint, new GUIContent("LOS Point (Optional)"));

            if (_losPoint.objectReferenceValue == null)
            {
                EditorGUILayout.HelpBox("No LOS Point set. Using transform position for line-of-sight checks.", MessageType.Info);
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Sound", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_isSoundSource, new GUIContent("Is Sound Source"));
            if (_isSoundSource.boolValue)
            {
                EditorGUILayout.PropertyField(_loudness, new GUIContent("Loudness"));
            }

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("Debug", EditorStyles.miniBoldLabel);
            EditorGUILayout.PropertyField(_showDebugOverlay, new GUIContent("Show Debug Overlay"));

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
            EditorGUI.DrawRect(statsRect, new Color(0.15f, 0.15f, 0.15f, 0.8f));

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            EditorGUI.LabelField(statsRect, "Runtime Info", style);

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
