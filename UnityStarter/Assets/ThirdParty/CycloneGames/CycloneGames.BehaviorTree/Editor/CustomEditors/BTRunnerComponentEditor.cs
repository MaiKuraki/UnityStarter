using System;
using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors
{
    [CustomEditor(typeof(BTRunnerComponent), true)]
    [CanEditMultipleObjects]
    public class BTRunnerComponentEditor : UnityEditor.Editor
    {
        private static class Styles
        {
            public static readonly GUIStyle HeaderStyle;
            public static readonly GUIStyle BoxStyle;
            public static readonly Color SeparatorColor = new Color(0.3f, 0.3f, 0.3f, 1f);
            
            static Styles()
            {
                HeaderStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    fontSize = 12,
                    margin = new RectOffset(0, 0, 8, 4)
                };
                
                BoxStyle = new GUIStyle("HelpBox")
                {
                    padding = new RectOffset(10, 10, 8, 8),
                    margin = new RectOffset(0, 0, 4, 4)
                };
            }
        }
        
        private SerializedProperty _startOnAwake;
        private SerializedProperty _tickMode;
        private SerializedProperty _behaviorTree;
        private SerializedProperty _initialObjects;
        
        private BTRunnerComponent _runner => (BTRunnerComponent)target;
        private bool _showBlackboard = true;
        
        protected virtual void OnEnable()
        {
            _startOnAwake = serializedObject.FindProperty("_startOnAwake");
            _tickMode = serializedObject.FindProperty("_tickMode");
            _behaviorTree = serializedObject.FindProperty("behaviorTree");
            _initialObjects = serializedObject.FindProperty("_initialObjects");
        }
        
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            
            DrawConfigurationSection();
            DrawBehaviorTreeSection();
            DrawInitialObjectsSection();
            
            if (Application.isPlaying)
            {
                DrawRuntimeControlsSection();
            }
            
            DrawBlackboardSection();
            DrawChildClassFields();
            
            serializedObject.ApplyModifiedProperties();
        }
        
        protected virtual void DrawConfigurationSection()
        {
            EditorGUILayout.LabelField("Configuration", Styles.HeaderStyle);
            EditorGUILayout.BeginVertical(Styles.BoxStyle);
            
            EditorGUILayout.PropertyField(_startOnAwake, new GUIContent("Start On Awake"));
            EditorGUILayout.PropertyField(_tickMode, new GUIContent("Tick Mode"));
            
            if (_tickMode.enumValueIndex == (int)TickMode.Managed)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Managed mode: BTTickManager handles tick for large-scale AI.", MessageType.Info);
                EditorGUI.indentLevel--;
            }
            else if (_tickMode.enumValueIndex == (int)TickMode.Manual)
            {
                EditorGUI.indentLevel++;
                EditorGUILayout.HelpBox("Manual mode: Call ManualTick() to update tree.", MessageType.Info);
                EditorGUI.indentLevel--;
            }
            
            EditorGUILayout.EndVertical();
        }
        
        protected virtual void DrawBehaviorTreeSection()
        {
            EditorGUILayout.LabelField("Behavior Tree", Styles.HeaderStyle);
            EditorGUILayout.BeginVertical(Styles.BoxStyle);
            
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(_behaviorTree, GUIContent.none);
            if (GUILayout.Button("Open Editor", GUILayout.Width(90)))
            {
                BehaviorTreeEditor.OpenWindow();
            }
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        protected virtual void DrawInitialObjectsSection()
        {
            EditorGUILayout.PropertyField(_initialObjects, new GUIContent("Initial Objects"));
        }
        
        protected virtual void DrawRuntimeControlsSection()
        {
            EditorGUILayout.LabelField("Runtime Controls", Styles.HeaderStyle);
            EditorGUILayout.BeginVertical(Styles.BoxStyle);
            
            // Status bar with color
            DrawStatusBar();
            
            EditorGUILayout.Space(4);
            
            EditorGUILayout.BeginHorizontal();
            
            GUI.enabled = _runner.IsStopped;
            if (GUILayout.Button("▶ Play", GUILayout.Height(24)))
            {
                foreach (var t in targets) ((BTRunnerComponent)t)?.Play();
            }
            
            GUI.enabled = !_runner.IsStopped;
            if (GUILayout.Button("■ Stop", GUILayout.Height(24)))
            {
                foreach (var t in targets) ((BTRunnerComponent)t)?.Stop();
            }
            
            GUI.enabled = !_runner.IsStopped && !_runner.IsPaused;
            if (GUILayout.Button("⏸ Pause", GUILayout.Height(24)))
            {
                foreach (var t in targets) ((BTRunnerComponent)t)?.Pause();
            }
            
            GUI.enabled = _runner.IsPaused;
            if (GUILayout.Button("⏵ Resume", GUILayout.Height(24)))
            {
                foreach (var t in targets) ((BTRunnerComponent)t)?.Resume();
            }
            
            GUI.enabled = true;
            EditorGUILayout.EndHorizontal();
            
            EditorGUILayout.EndVertical();
        }
        
        private void DrawStatusBar()
        {
            bool isDarkTheme = EditorGUIUtility.isProSkin;
            
            Color statusColor;
            string statusText;
            string statusIcon;
            
            if (_runner.IsStopped)
            {
                statusColor = isDarkTheme ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f);
                statusText = "STOPPED";
                statusIcon = "●";
            }
            else if (_runner.IsPaused)
            {
                statusColor = isDarkTheme ? new Color(1f, 0.8f, 0.2f) : new Color(0.8f, 0.6f, 0f);
                statusText = "PAUSED";
                statusIcon = "◐";
            }
            else
            {
                statusColor = isDarkTheme ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.1f, 0.7f, 0.1f);
                statusText = "RUNNING";
                statusIcon = "◉";
            }
            
            var rect = EditorGUILayout.GetControlRect(false, 22);
            
            // Draw background
            Color bgColor = isDarkTheme ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.85f, 0.85f, 0.85f);
            EditorGUI.DrawRect(rect, bgColor);
            
            // Draw status indicator circle
            var indicatorRect = new Rect(rect.x + 8, rect.y + 5, 12, 12);
            EditorGUI.DrawRect(indicatorRect, statusColor);
            
            // Draw status text
            var labelStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleLeft,
                normal = { textColor = statusColor }
            };
            var labelRect = new Rect(rect.x + 28, rect.y, rect.width - 28, rect.height);
            EditorGUI.LabelField(labelRect, $"{statusIcon} {statusText}", labelStyle);
        }
        
        protected virtual void DrawBlackboardSection()
        {
            _showBlackboard = EditorGUILayout.Foldout(_showBlackboard, "BlackBoard Data", true);
            if (!_showBlackboard) return;
            
            EditorGUILayout.BeginVertical(Styles.BoxStyle);
            
            var allData = _runner.BlackBoard?.GetAllData();
            if (allData == null || allData.Count == 0)
            {
                EditorGUILayout.LabelField("No data", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                EditorGUI.BeginDisabledGroup(true);
                foreach (var data in allData)
                {
                    EditorGUILayout.TextField(data.Key, data.Value?.ToString() ?? "NULL");
                }
                EditorGUI.EndDisabledGroup();
            }
            
            EditorGUILayout.EndVertical();
        }
        
        /// <summary>
        /// Draw additional fields from child classes. Override to customize.
        /// </summary>
        protected virtual void DrawChildClassFields()
        {
            // Auto-draw remaining serialized fields from derived classes
            var iterator = serializedObject.GetIterator();
            bool enterChildren = true;
            
            while (iterator.NextVisible(enterChildren))
            {
                enterChildren = false;
                
                // Skip already drawn fields
                if (iterator.name == "m_Script" ||
                    iterator.name == "_startOnAwake" ||
                    iterator.name == "_tickMode" ||
                    iterator.name == "behaviorTree" ||
                    iterator.name == "_initialObjects" ||
                    iterator.name == "_blackBoard")
                {
                    continue;
                }
                
                EditorGUILayout.PropertyField(iterator, true);
            }
        }
    }
}