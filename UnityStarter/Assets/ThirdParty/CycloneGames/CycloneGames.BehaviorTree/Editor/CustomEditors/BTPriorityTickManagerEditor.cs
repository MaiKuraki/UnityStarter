using UnityEditor;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Components;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors
{
    [CustomEditor(typeof(BTPriorityTickManagerComponent))]
    public class BTPriorityTickManagerEditor : UnityEditor.Editor
    {
        private static readonly Color[] PriorityColors = new Color[]
        {
            new Color(0.2f, 0.8f, 0.2f),  // P0 - Green (high priority)
            new Color(0.4f, 0.8f, 0.4f),  // P1
            new Color(0.8f, 0.8f, 0.2f),  // P2 - Yellow
            new Color(0.8f, 0.6f, 0.2f),  // P3 - Orange
            new Color(0.8f, 0.4f, 0.2f),  // P4
            new Color(0.6f, 0.3f, 0.2f),  // P5
            new Color(0.5f, 0.5f, 0.5f),  // P6 - Gray
            new Color(0.3f, 0.3f, 0.3f),  // P7
        };
        
        private SerializedProperty _config;
        private SerializedProperty _lodUpdateInterval;
        private SerializedProperty _referencePoint;
        private SerializedProperty _playerTag;
        private SerializedProperty _autoFindPlayer;
        
        private BTPriorityTickManagerComponent _target;
        private bool _showStats = true;
        
        private void OnEnable()
        {
            _config = serializedObject.FindProperty("_config");
            _lodUpdateInterval = serializedObject.FindProperty("_lodUpdateInterval");
            _referencePoint = serializedObject.FindProperty("_referencePoint");
            _playerTag = serializedObject.FindProperty("_playerTag");
            _autoFindPlayer = serializedObject.FindProperty("_autoFindPlayer");
            _target = (BTPriorityTickManagerComponent)target;
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            DrawConfigSection();
            DrawReferencePointSection();
            
            if (Application.isPlaying)
            {
                DrawRuntimeStats();
            }
            
            serializedObject.ApplyModifiedProperties();
        }
        
        private void DrawConfigSection()
        {
            EditorGUILayout.LabelField("Configuration", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("HelpBox");
            
            EditorGUILayout.PropertyField(_config, new GUIContent("LOD Config"));
            EditorGUILayout.PropertyField(_lodUpdateInterval, new GUIContent("LOD Update Interval"));
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawReferencePointSection()
        {
            EditorGUILayout.Space(8);
            EditorGUILayout.LabelField("Reference Point", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical("HelpBox");
            
            EditorGUILayout.PropertyField(_autoFindPlayer, new GUIContent("Auto Find Player"));
            
            if (_autoFindPlayer.boolValue)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.PropertyField(_playerTag, new GUIContent("Player Tag"));
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.PropertyField(_referencePoint, new GUIContent("Reference Point"));
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawRuntimeStats()
        {
            EditorGUILayout.Space(8);
            _showStats = EditorGUILayout.Foldout(_showStats, "Runtime Statistics", true);
            
            if (!_showStats) return;
            
            EditorGUILayout.BeginVertical("HelpBox");
            
            int total = _target.TotalTreeCount;
            EditorGUILayout.LabelField($"Total Managed Trees: {total}", EditorStyles.boldLabel);
            
            EditorGUILayout.Space(4);
            
            // Priority distribution bar
            if (total > 0)
            {
                DrawPriorityDistribution(total);
            }
            else
            {
                EditorGUILayout.HelpBox("No trees registered", MessageType.Info);
            }
            
            EditorGUILayout.EndVertical();
            
            Repaint();
        }
        
        private void DrawPriorityDistribution(int total)
        {
            EditorGUILayout.LabelField("Priority Distribution:");
            
            Rect barRect = EditorGUILayout.GetControlRect(false, 24);
            float xOffset = 0;
            
            for (int i = 0; i < 8; i++)
            {
                int count = _target.GetPriorityTreeCount(i);
                if (count == 0) continue;
                
                float ratio = (float)count / total;
                float width = barRect.width * ratio;
                
                var segmentRect = new Rect(barRect.x + xOffset, barRect.y, width, barRect.height);
                EditorGUI.DrawRect(segmentRect, PriorityColors[i]);
                
                if (width > 30)
                {
                    var style = new GUIStyle(EditorStyles.miniLabel)
                    {
                        alignment = TextAnchor.MiddleCenter,
                        normal = { textColor = Color.white }
                    };
                    EditorGUI.LabelField(segmentRect, $"P{i}:{count}", style);
                }
                
                xOffset += width;
            }
            
            // Legend
            EditorGUILayout.Space(4);
            EditorGUILayout.BeginHorizontal();
            for (int i = 0; i < 4; i++)
            {
                int count = _target.GetPriorityTreeCount(i);
                DrawLegendItem($"P{i}: {count}", PriorityColors[i]);
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.BeginHorizontal();
            for (int i = 4; i < 8; i++)
            {
                int count = _target.GetPriorityTreeCount(i);
                DrawLegendItem($"P{i}: {count}", PriorityColors[i]);
            }
            EditorGUILayout.EndHorizontal();
        }
        
        private void DrawLegendItem(string label, Color color)
        {
            var rect = EditorGUILayout.GetControlRect(false, 16, GUILayout.Width(60));
            var colorRect = new Rect(rect.x, rect.y + 3, 10, 10);
            EditorGUI.DrawRect(colorRect, color);
            
            var labelRect = new Rect(rect.x + 14, rect.y, rect.width - 14, rect.height);
            EditorGUI.LabelField(labelRect, label, EditorStyles.miniLabel);
        }
    }
}
