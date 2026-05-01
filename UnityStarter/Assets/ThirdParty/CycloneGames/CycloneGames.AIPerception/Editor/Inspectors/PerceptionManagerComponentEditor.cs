using UnityEngine;
using UnityEditor;
using CycloneGames.AIPerception.Runtime;

namespace CycloneGames.AIPerception.Editor
{
    [CustomEditor(typeof(PerceptionManagerComponent))]
    public class PerceptionManagerComponentEditor : UnityEditor.Editor
    {
        private static readonly Color HeaderColor = new Color(0.4f, 0.5f, 0.9f, 1f);
        private static readonly Color PerformanceColor = new Color(0.8f, 0.6f, 0.2f, 1f);
        private static readonly Color LODColor = new Color(0.3f, 0.8f, 0.5f, 1f);
        private static readonly Color PerformanceBgColor = new Color(PerformanceColor.r, PerformanceColor.g, PerformanceColor.b, 0.2f);
        private static readonly Color LODBgColor = new Color(LODColor.r, LODColor.g, LODColor.b, 0.2f);
        private static readonly Color LODLevel1Color = new Color(0.2f, 0.8f, 0.3f, 1f);
        private static readonly Color LODLevel2Color = new Color(0.8f, 0.7f, 0.2f, 1f);
        private static readonly Color LODLevel3Color = new Color(0.8f, 0.3f, 0.2f, 1f);

        private static GUIStyle _headerStyle;
        private static GUIStyle _sectionLabelStyle;
        private static GUIStyle _lodMarkerStyle;
        private static GUIStyle _runtimeStatsStyle;
        private static readonly Color RuntimeStatsBgColor = new Color(0.15f, 0.15f, 0.15f, 0.8f);
        private static readonly Color RuntimeStatsTextColor = new Color(0.8f, 0.8f, 0.8f);
        private static readonly GUIContent LabelLODRef = new GUIContent("LOD Reference", "Reference transform for distance calculation. Typically the main camera or player.");
        private static readonly GUIContent LabelLODLevels = new GUIContent("LOD Levels", "Distance thresholds and frequency multipliers.");
        private static readonly GUIContent LabelDeferredJob = new GUIContent("Deferred Job Completion",
            "Batch jobs and complete in LateUpdate for better CPU utilization with many sensors.");

        private SerializedProperty _useDeferredJobCompletion;
        private SerializedProperty _lodReference;
        private SerializedProperty _lodLevels;

        private bool _showPerformance = true;
        private bool _showLOD = true;

        protected virtual void OnEnable()
        {
            _useDeferredJobCompletion = serializedObject.FindProperty("_useDeferredJobCompletion");
            _lodReference = serializedObject.FindProperty("_lodReference");
            _lodLevels = serializedObject.FindProperty("_lodLevels");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawCustomHeader();
            DrawPerformanceSection();
            DrawLODSection();

            if (Application.isPlaying)
            {
                DrawRuntimeStats();
            }

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawCustomHeader()
        {
            EditorGUILayout.Space(2);
            var rect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, HeaderColor);

            if (_headerStyle == null)
            {
                _headerStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 13,
                    alignment = TextAnchor.MiddleCenter
                };
                _headerStyle.normal.textColor = Color.white;
            }

            EditorGUI.LabelField(rect, "Perception Manager", _headerStyle);
            EditorGUILayout.Space(4);

            EditorGUILayout.HelpBox(
                "Global perception system driver. Auto-created as [PerceptionManager] in DontDestroyOnLoad.",
                MessageType.Info);
        }

        private void DrawPerformanceSection()
        {
            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, PerformanceBgColor);

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showPerformance = EditorGUI.Foldout(foldoutRect, _showPerformance, GUIContent.none, true);

            if (_sectionLabelStyle == null)
            {
                _sectionLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
                _sectionLabelStyle.normal.textColor = PerformanceColor;
            }

            var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
            EditorGUI.LabelField(labelRect, "Performance", _sectionLabelStyle);

            if (_showPerformance)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_useDeferredJobCompletion, LabelDeferredJob);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawLODSection()
        {
            EditorGUILayout.Space(2);
            var bgRect = GUILayoutUtility.GetRect(0, 20, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(bgRect, LODBgColor);

            var foldoutRect = new Rect(bgRect.x + 2, bgRect.y + 2, 14, 16);
            _showLOD = EditorGUI.Foldout(foldoutRect, _showLOD, GUIContent.none, true);

            var labelRect = new Rect(bgRect.x + 18, bgRect.y + 1, bgRect.width - 18, 18);
            EditorGUI.LabelField(labelRect, "LOD (Level of Detail)", _sectionLabelStyle);

            if (_showLOD)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_lodReference, LabelLODRef);

                if (_lodReference.objectReferenceValue == null)
                {
                    EditorGUILayout.HelpBox(
                        "No LOD reference set. LOD is disabled — all sensors update at full frequency.",
                        MessageType.Info);
                }
                else
                {
                    DrawLODPreview();
                }

                EditorGUILayout.PropertyField(_lodLevels, LabelLODLevels, true);
                EditorGUI.indentLevel--;
            }
        }

        private void DrawLODPreview()
        {
            if (_lodLevels.arraySize == 0) return;

            EditorGUILayout.Space(4);
            EditorGUILayout.LabelField("LOD Preview", EditorStyles.miniBoldLabel);

            var previewRect = GUILayoutUtility.GetRect(0, 48, GUILayout.ExpandWidth(true));
            float maxDist = 0f;

            for (int i = 0; i < _lodLevels.arraySize; i++)
            {
                var level = _lodLevels.GetArrayElementAtIndex(i);
                float dist = level.FindPropertyRelative("Distance").floatValue;
                if (dist > maxDist) maxDist = dist;
            }

            if (maxDist <= 0f) maxDist = 200f;

            // Draw distance bands
            float prevDist = 0f;
            Color[] levelColors = { LODLevel1Color, LODLevel2Color, LODLevel3Color };

            for (int i = 0; i < _lodLevels.arraySize; i++)
            {
                var level = _lodLevels.GetArrayElementAtIndex(i);
                float dist = level.FindPropertyRelative("Distance").floatValue;
                float mult = level.FindPropertyRelative("FrequencyMultiplier").floatValue;

                float x0 = prevDist / maxDist * previewRect.width;
                float x1 = dist / maxDist * previewRect.width;
                var bandRect = new Rect(previewRect.x + x0, previewRect.y + 8, x1 - x0, previewRect.height - 16);

                Color bandColor = i < levelColors.Length ? levelColors[i] : levelColors[levelColors.Length - 1];
                bandColor.a = 0.3f;
                EditorGUI.DrawRect(bandRect, bandColor);

                // Label
                var labelContent = new GUIContent($"×{mult:F2}");
                var labelSize = EditorStyles.miniLabel.CalcSize(labelContent);
                float labelX = previewRect.x + (x0 + x1) / 2f - labelSize.x / 2f;
                if (labelX < previewRect.x) labelX = previewRect.x + 2;

                var labelRect = new Rect(labelX, previewRect.y + previewRect.height - 14, labelSize.x, 14);
                GUI.Label(labelRect, labelContent, EditorStyles.miniLabel);

                prevDist = dist;
            }

            // Border
            var borderRect = new Rect(previewRect.x, previewRect.y + 8, previewRect.width, previewRect.height - 16);
            Handles.color = new Color(0.5f, 0.5f, 0.5f, 0.3f);
            Handles.DrawSolidRectangleWithOutline(borderRect, Color.clear, new Color(0.5f, 0.5f, 0.5f, 0.4f));

            // Distance markers
            if (_lodMarkerStyle == null)
            {
                _lodMarkerStyle = new GUIStyle(EditorStyles.miniLabel)
                {
                    alignment = TextAnchor.LowerCenter,
                    fontSize = 9
                };
            }

            GUI.Label(new Rect(previewRect.x, previewRect.y, 30, 12), "0m", _lodMarkerStyle);
            GUI.Label(new Rect(previewRect.x + previewRect.width - 30, previewRect.y, 40, 12), $"{maxDist:F0}m", _lodMarkerStyle);
        }

        private void DrawRuntimeStats()
        {
            EditorGUILayout.Space(4);
            var statsRect = GUILayoutUtility.GetRect(0, 26, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(statsRect, RuntimeStatsBgColor);

            if (_runtimeStatsStyle == null)
            {
                _runtimeStatsStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter,
                    fontSize = 12
                };
                _runtimeStatsStyle.normal.textColor = RuntimeStatsTextColor;
            }

            EditorGUI.LabelField(statsRect, "▶ Runtime", _runtimeStatsStyle);

            EditorGUI.indentLevel++;
            GUI.enabled = false;
            EditorGUILayout.Toggle("Deferred Mode", SensorManager.Instance?.UseDeferredJobCompletion ?? false);
            EditorGUILayout.IntField("Active Sensors", SensorManager.Instance?.SensorCount ?? 0);
            EditorGUILayout.IntField("Perceptibles", PerceptibleRegistry.Instance?.Count ?? 0);
            GUI.enabled = true;
            EditorGUI.indentLevel--;

            Repaint();
        }
    }
}
