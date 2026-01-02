using UnityEngine;
using UnityEditor;
using CycloneGames.AIPerception.Runtime;

namespace CycloneGames.AIPerception.Editor
{
    [CustomEditor(typeof(AIPerceptionComponent), true)]  // true = supports derived classes
    [CanEditMultipleObjects]
    public class AIPerceptionComponentEditor : UnityEditor.Editor
    {
        private static readonly Color HeaderColor = new Color(0.2f, 0.6f, 0.9f, 1f);
        private static readonly Color SightColor = new Color(1f, 0.8f, 0.2f, 1f);
        private static readonly Color HearingColor = new Color(0.5f, 0.8f, 1f, 1f);
        private static readonly Color DebugColor = new Color(0.6f, 0.9f, 0.6f, 1f);

        private SerializedProperty _enableSight;
        private SerializedProperty _sightConfig;
        private SerializedProperty _enableHearing;
        private SerializedProperty _hearingConfig;
        private SerializedProperty _showDebugOverlay;

        private bool _showSightFoldout = true;
        private bool _showHearingFoldout = true;
        private bool _showDebugFoldout = true;
        private bool _showDerivedFieldsFoldout = true;

        protected virtual void OnEnable()
        {
            _enableSight = serializedObject.FindProperty("_enableSight");
            _sightConfig = serializedObject.FindProperty("_sightConfig");
            _enableHearing = serializedObject.FindProperty("_enableHearing");
            _hearingConfig = serializedObject.FindProperty("_hearingConfig");
            _showDebugOverlay = serializedObject.FindProperty("_showDebugOverlay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCustomHeader();

            DrawSightSection();
            DrawHearingSection();
            DrawDebugSection();

            // Draw derived class fields
            DrawDerivedClassFields();

            if (Application.isPlaying)
            {
                EditorGUILayout.Space(4);
                DrawRuntimeStats();
            }

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

            EditorGUI.LabelField(rect, "AI Perception", style);
            EditorGUILayout.Space(4);
        }

        /// <summary>
        /// Draws any serialized fields from derived classes.
        /// Override this to customize derived field drawing.
        /// </summary>
        protected virtual void DrawDerivedClassFields()
        {
            // Check if this is a derived class
            if (target.GetType() == typeof(AIPerceptionComponent)) return;

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
                        EditorGUILayout.Space(4);
                        var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
                        EditorGUI.DrawRect(bgRect, new Color(0.6f, 0.4f, 0.8f, 0.25f));

                        var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
                        _showDerivedFieldsFoldout = EditorGUI.Foldout(foldoutRect, _showDerivedFieldsFoldout, GUIContent.none, true);

                        var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
                        var style = new GUIStyle(EditorStyles.boldLabel);
                        style.normal.textColor = new Color(0.8f, 0.6f, 1f);
                        EditorGUI.LabelField(labelRect, "📦 Custom Fields", style);
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
            return fieldName == "_enableSight" || fieldName == "_sightConfig" ||
                   fieldName == "_enableHearing" || fieldName == "_hearingConfig" ||
                   fieldName == "_showDebugOverlay" || fieldName == "_debugToggleKey";
        }

        protected virtual void DrawSightSection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, new Color(SightColor.r, SightColor.g, SightColor.b, 0.25f));

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showSightFoldout = EditorGUI.Foldout(foldoutRect, _showSightFoldout, GUIContent.none, true);

            var toggleRect = new Rect(bgRect.x + 18, bgRect.y + 2, 16, 16);
            _enableSight.boolValue = EditorGUI.Toggle(toggleRect, _enableSight.boolValue);

            var labelRect = new Rect(bgRect.x + 38, bgRect.y + 1, bgRect.width - 38, 18);
            var sightStyle = new GUIStyle(EditorStyles.boldLabel);
            sightStyle.normal.textColor = SightColor;
            EditorGUI.LabelField(labelRect, "👁 Sight Sensor", sightStyle);

            if (_showSightFoldout && _enableSight.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("HalfAngle"), new GUIContent("Half Angle (°)"));
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("MaxDistance"), new GUIContent("Max Distance"));
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("UpdateInterval"), new GUIContent("Update Interval"));
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("ObstacleLayer"), new GUIContent("Obstacle Layer"));
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("UseLineOfSight"), new GUIContent("Use Line of Sight"));
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("FilterByType"), new GUIContent("Filter by Type"));
                if (_sightConfig.FindPropertyRelative("FilterByType").boolValue)
                {
                    EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("TargetTypeId"), new GUIContent("Target Type ID"));
                }
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawHearingSection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, new Color(HearingColor.r, HearingColor.g, HearingColor.b, 0.25f));

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showHearingFoldout = EditorGUI.Foldout(foldoutRect, _showHearingFoldout, GUIContent.none, true);

            var toggleRect = new Rect(bgRect.x + 18, bgRect.y + 2, 16, 16);
            _enableHearing.boolValue = EditorGUI.Toggle(toggleRect, _enableHearing.boolValue);

            var labelRect = new Rect(bgRect.x + 38, bgRect.y + 1, bgRect.width - 38, 18);
            var hearingStyle = new GUIStyle(EditorStyles.boldLabel);
            hearingStyle.normal.textColor = HearingColor;
            EditorGUI.LabelField(labelRect, "👂 Hearing Sensor", hearingStyle);

            if (_showHearingFoldout && _enableHearing.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("Radius"), new GUIContent("Detection Radius"));
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("UpdateInterval"), new GUIContent("Update Interval"));

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Sound Occlusion", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("UseOcclusion"), new GUIContent("Enable Occlusion"));
                if (_hearingConfig.FindPropertyRelative("UseOcclusion").boolValue)
                {
                    EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("OcclusionLayer"), new GUIContent("Occlusion Layer"));
                    EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("OcclusionAttenuation"), new GUIContent("Wall Attenuation"));
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("FilterByType"), new GUIContent("Filter by Type"));
                if (_hearingConfig.FindPropertyRelative("FilterByType").boolValue)
                {
                    EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("TargetTypeId"), new GUIContent("Target Type ID"));
                }
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawDebugSection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, new Color(DebugColor.r, DebugColor.g, DebugColor.b, 0.25f));

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showDebugFoldout = EditorGUI.Foldout(foldoutRect, _showDebugFoldout, GUIContent.none, true);

            var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
            var debugStyle = new GUIStyle(EditorStyles.boldLabel);
            debugStyle.normal.textColor = DebugColor;
            EditorGUI.LabelField(labelRect, "🔧 Debug", debugStyle);

            if (_showDebugFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_showDebugOverlay, new GUIContent("Show Debug Overlay"));
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawRuntimeStats()
        {
            var statsRect = GUILayoutUtility.GetRect(0, 22, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(statsRect, new Color(0.15f, 0.15f, 0.15f, 0.8f));

            var style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter
            };
            style.normal.textColor = new Color(0.8f, 0.8f, 0.8f);
            EditorGUI.LabelField(statsRect, "Runtime Stats", style);

            var perception = (AIPerceptionComponent)target;

            EditorGUILayout.BeginHorizontal();
            GUI.enabled = false;
            EditorGUILayout.IntField("Sight", perception.SightDetectedCount, GUILayout.Width(EditorGUIUtility.currentViewWidth / 2 - 20));
            EditorGUILayout.IntField("Hearing", perception.HearingDetectedCount);
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();

            if (perception.HasAnyDetection)
            {
                EditorGUILayout.HelpBox("⚠ Target Detected!", MessageType.Warning);
            }

            if (GUILayout.Button(perception.ShowDebugOverlay ? "Hide Debug Overlay" : "Show Debug Overlay"))
            {
                perception.ShowDebugOverlay = !perception.ShowDebugOverlay;
            }

            Repaint();
        }
    }
}
