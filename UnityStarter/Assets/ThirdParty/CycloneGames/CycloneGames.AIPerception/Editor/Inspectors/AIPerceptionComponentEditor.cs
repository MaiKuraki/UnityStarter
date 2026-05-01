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
        private static readonly Color ProximityColor = new Color(1f, 0.5f, 0.2f, 1f);
        private static readonly Color DebugColor = new Color(0.6f, 0.9f, 0.6f, 1f);
        private static readonly Color DerivedSectionBgColor = new Color(0.6f, 0.4f, 0.8f, 0.25f);
        private static readonly Color SightSectionBgColor = new Color(SightColor.r, SightColor.g, SightColor.b, 0.25f);
        private static readonly Color HearingSectionBgColor = new Color(HearingColor.r, HearingColor.g, HearingColor.b, 0.25f);
        private static readonly Color ProximitySectionBgColor = new Color(ProximityColor.r, ProximityColor.g, ProximityColor.b, 0.25f);
        private static readonly Color DebugSectionBgColor = new Color(DebugColor.r, DebugColor.g, DebugColor.b, 0.25f);
        private static readonly Color RuntimeStatsBgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        private static readonly Color RuntimeStatsTextColor = new Color(0.8f, 0.8f, 0.8f);
        private static readonly Color DerivedLabelColor = new Color(0.8f, 0.6f, 1f);

        // Cached GUIStyles (0-allocation in OnInspectorGUI)
        private static GUIStyle _headerStyle;
        private static GUIStyle _derivedLabelStyle;
        private static GUIStyle _sightLabelStyle;
        private static GUIStyle _hearingLabelStyle;
        private static GUIStyle _proximityLabelStyle;
        private static GUIStyle _debugLabelStyle;
        private static GUIStyle _runtimeStatsStyle;

        // Cached GUIContent (0-allocation in OnInspectorGUI)
        private static readonly GUIContent LabelHalfAngle = new GUIContent("Half Angle (°)");
        private static readonly GUIContent LabelMaxDistance = new GUIContent("Max Distance");
        private static readonly GUIContent LabelUpdateInterval = new GUIContent("Update Interval");
        private static readonly GUIContent LabelObstacleLayer = new GUIContent("Obstacle Layer");
        private static readonly GUIContent LabelUseLineOfSight = new GUIContent("Use Line of Sight");
        private static readonly GUIContent LabelFilterByType = new GUIContent("Filter by Type");
        private static readonly GUIContent LabelTargetTypeId = new GUIContent("Target Type ID");
        private static readonly GUIContent LabelDetectionRadius = new GUIContent("Detection Radius");
        private static readonly GUIContent LabelProximityRadius = new GUIContent("Detection Radius");
        private static readonly GUIContent LabelEnableOcclusion = new GUIContent("Enable Occlusion");
        private static readonly GUIContent LabelOcclusionLayer = new GUIContent("Occlusion Layer");
        private static readonly GUIContent LabelWallAttenuation = new GUIContent("Wall Attenuation");
        private static readonly GUIContent LabelShowDebugOverlay = new GUIContent("Show Debug Overlay");
        private static readonly GUIContent LabelDerivedFields = new GUIContent("Custom Fields");
        private static readonly GUIContent LabelMemoryDuration = new GUIContent("Memory Duration", "Seconds to remember after leaving sensor range. 0 = disabled.");
        private static readonly GUIContent LabelMemoryDurationHearing = new GUIContent("Memory Duration", "Seconds to remember after sound fades. 0 = disabled.");

        private SerializedProperty _enableSight;
        private SerializedProperty _sightConfig;
        private SerializedProperty _enableHearing;
        private SerializedProperty _hearingConfig;
        private SerializedProperty _enableProximity;
        private SerializedProperty _proximityConfig;
        private SerializedProperty _showDebugOverlay;

        private bool _showSightFoldout = true;
        private bool _showHearingFoldout = true;
        private bool _showProximityFoldout = true;
        private bool _showDebugFoldout = true;
        private bool _showDerivedFieldsFoldout = true;

        protected virtual void OnEnable()
        {
            _enableSight = serializedObject.FindProperty("_enableSight");
            _sightConfig = serializedObject.FindProperty("_sightConfig");
            _enableHearing = serializedObject.FindProperty("_enableHearing");
            _hearingConfig = serializedObject.FindProperty("_hearingConfig");
            _enableProximity = serializedObject.FindProperty("_enableProximity");
            _proximityConfig = serializedObject.FindProperty("_proximityConfig");
            _showDebugOverlay = serializedObject.FindProperty("_showDebugOverlay");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCustomHeader();

            DrawSightSection();
            DrawHearingSection();
            DrawProximitySection();
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

            EditorGUI.LabelField(rect, "AI Perception", _headerStyle);
            EditorGUILayout.Space(4);
        }

        protected virtual void DrawDerivedClassFields()
        {
            // Check if this is a derived class
            if (target.GetType() == typeof(AIPerceptionComponent)) return;

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
                        EditorGUILayout.Space(4);
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
            return fieldName == "_enableSight" || fieldName == "_sightConfig" ||
                   fieldName == "_enableHearing" || fieldName == "_hearingConfig" ||
                   fieldName == "_enableProximity" || fieldName == "_proximityConfig" ||
                   fieldName == "_showDebugOverlay" || fieldName == "_debugToggleKey";
        }

        protected virtual void DrawSightSection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, SightSectionBgColor);

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showSightFoldout = EditorGUI.Foldout(foldoutRect, _showSightFoldout, GUIContent.none, true);

            var toggleRect = new Rect(bgRect.x + 18, bgRect.y + 2, 16, 16);
            _enableSight.boolValue = EditorGUI.Toggle(toggleRect, _enableSight.boolValue);

            if (_sightLabelStyle == null)
            {
                _sightLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                _sightLabelStyle.normal.textColor = SightColor;
            }

            var labelRect = new Rect(bgRect.x + 38, bgRect.y + 1, bgRect.width - 38, 18);
            EditorGUI.LabelField(labelRect, "Sight Sensor", _sightLabelStyle);

            if (_showSightFoldout && _enableSight.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("HalfAngle"), LabelHalfAngle);
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("MaxDistance"), LabelMaxDistance);
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("UpdateInterval"), LabelUpdateInterval);
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("ObstacleLayer"), LabelObstacleLayer);
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("UseLineOfSight"), LabelUseLineOfSight);
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("FilterByType"), LabelFilterByType);
                if (_sightConfig.FindPropertyRelative("FilterByType").boolValue)
                {
                    EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("TargetTypeId"), LabelTargetTypeId);
                }
                EditorGUILayout.PropertyField(_sightConfig.FindPropertyRelative("MemoryDuration"), LabelMemoryDuration);
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawHearingSection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, HearingSectionBgColor);

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showHearingFoldout = EditorGUI.Foldout(foldoutRect, _showHearingFoldout, GUIContent.none, true);

            var toggleRect = new Rect(bgRect.x + 18, bgRect.y + 2, 16, 16);
            _enableHearing.boolValue = EditorGUI.Toggle(toggleRect, _enableHearing.boolValue);

            if (_hearingLabelStyle == null)
            {
                _hearingLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                _hearingLabelStyle.normal.textColor = HearingColor;
            }

            var labelRect = new Rect(bgRect.x + 38, bgRect.y + 1, bgRect.width - 38, 18);
            EditorGUI.LabelField(labelRect, "Hearing Sensor", _hearingLabelStyle);

            if (_showHearingFoldout && _enableHearing.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("Radius"), LabelDetectionRadius);
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("UpdateInterval"), LabelUpdateInterval);

                EditorGUILayout.Space(2);
                EditorGUILayout.LabelField("Sound Occlusion", EditorStyles.miniBoldLabel);
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("UseOcclusion"), LabelEnableOcclusion);
                if (_hearingConfig.FindPropertyRelative("UseOcclusion").boolValue)
                {
                    EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("OcclusionLayer"), LabelOcclusionLayer);
                    EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("OcclusionAttenuation"), LabelWallAttenuation);
                }

                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("FilterByType"), LabelFilterByType);
                if (_hearingConfig.FindPropertyRelative("FilterByType").boolValue)
                {
                    EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("TargetTypeId"), LabelTargetTypeId);
                }
                EditorGUILayout.PropertyField(_hearingConfig.FindPropertyRelative("MemoryDuration"), LabelMemoryDurationHearing);
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawProximitySection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, ProximitySectionBgColor);

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showProximityFoldout = EditorGUI.Foldout(foldoutRect, _showProximityFoldout, GUIContent.none, true);

            var toggleRect = new Rect(bgRect.x + 18, bgRect.y + 2, 16, 16);
            _enableProximity.boolValue = EditorGUI.Toggle(toggleRect, _enableProximity.boolValue);

            if (_proximityLabelStyle == null)
            {
                _proximityLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                _proximityLabelStyle.normal.textColor = ProximityColor;
            }

            var labelRect = new Rect(bgRect.x + 38, bgRect.y + 1, bgRect.width - 38, 18);
            EditorGUI.LabelField(labelRect, "Proximity Sensor", _proximityLabelStyle);

            if (_showProximityFoldout && _enableProximity.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_proximityConfig.FindPropertyRelative("Radius"), LabelProximityRadius);
                EditorGUILayout.PropertyField(_proximityConfig.FindPropertyRelative("UpdateInterval"), LabelUpdateInterval);

                EditorGUILayout.Space(2);
                EditorGUILayout.PropertyField(_proximityConfig.FindPropertyRelative("FilterByType"), LabelFilterByType);
                if (_proximityConfig.FindPropertyRelative("FilterByType").boolValue)
                {
                    EditorGUILayout.PropertyField(_proximityConfig.FindPropertyRelative("TargetTypeId"), LabelTargetTypeId);
                }
                EditorGUILayout.PropertyField(_proximityConfig.FindPropertyRelative("MemoryDuration"), LabelMemoryDuration);
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawDebugSection()
        {
            EditorGUILayout.Space(2);

            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, DebugSectionBgColor);

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showDebugFoldout = EditorGUI.Foldout(foldoutRect, _showDebugFoldout, GUIContent.none, true);

            if (_debugLabelStyle == null)
            {
                _debugLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                _debugLabelStyle.normal.textColor = DebugColor;
            }

            var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
            EditorGUI.LabelField(labelRect, "Debug", _debugLabelStyle);

            if (_showDebugFoldout)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_showDebugOverlay, LabelShowDebugOverlay);
                EditorGUI.indentLevel--;
            }
        }

        protected virtual void DrawRuntimeStats()
        {
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

            EditorGUI.LabelField(statsRect, "Runtime Stats", _runtimeStatsStyle);

            var perception = (AIPerceptionComponent)target;

            EditorGUILayout.BeginHorizontal();
            GUILayout.Space(2);
            EditorGUILayout.LabelField("S:", GUILayout.Width(18));
            GUI.enabled = false;
            EditorGUILayout.IntField(perception.SightDetectedCount, GUILayout.MinWidth(30));
            GUI.enabled = true;
            GUILayout.Space(4);
            EditorGUILayout.LabelField("H:", GUILayout.Width(18));
            GUI.enabled = false;
            EditorGUILayout.IntField(perception.HearingDetectedCount, GUILayout.MinWidth(30));
            GUI.enabled = true;
            GUILayout.Space(4);
            EditorGUILayout.LabelField("P:", GUILayout.Width(18));
            GUI.enabled = false;
            EditorGUILayout.IntField(perception.ProximityDetectedCount, GUILayout.MinWidth(30));
            GUI.enabled = true;
            GUILayout.FlexibleSpace();
            EditorGUILayout.EndHorizontal();

            if (perception.HasAnyDetection)
            {
                EditorGUILayout.HelpBox("Target Detected!", MessageType.Warning);
            }

            if (GUILayout.Button(perception.ShowDebugOverlay ? "Hide Debug Overlay" : "Show Debug Overlay"))
            {
                perception.ShowDebugOverlay = !perception.ShowDebugOverlay;
            }

            Repaint();
        }
    }
}
