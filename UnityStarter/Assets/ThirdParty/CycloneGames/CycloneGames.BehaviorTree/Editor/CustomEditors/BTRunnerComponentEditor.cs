using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Core;
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
            public static readonly GUIStyle StatusLabelStyle;
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

                StatusLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleLeft
                };
            }
        }

        private SerializedProperty _startOnAwake;
        private SerializedProperty _tickMode;
        private SerializedProperty _behaviorTree;
        private SerializedProperty _initialObjects;

        private BTRunnerComponent _runner => (BTRunnerComponent)target;
        private bool _showBlackboard = true;
        private readonly List<RuntimeBlackboardDebugEntry> _debugEntries =
            new List<RuntimeBlackboardDebugEntry>(16);
        private readonly List<string> _debugLabels = new List<string>(16);
        private RuntimeBlackboard _debugBlackboard;
        private double _nextDebugRefreshTime;
        private const double DEBUG_REFRESH_INTERVAL = 0.2;

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
                if (targets.Length == 1)
                {
                    DrawRuntimeControlsSection();
                }
                else
                {
                    EditorGUILayout.HelpBox(
                        "Runtime controls are available when one BTRunnerComponent is selected.",
                        MessageType.Info);
                }
            }

            if (targets.Length == 1)
            {
                DrawBlackboardSection();
            }
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

            if (_runner.IsStopped)
            {
                statusColor = isDarkTheme ? new Color(0.6f, 0.6f, 0.6f) : new Color(0.4f, 0.4f, 0.4f);
                statusText = "● STOPPED";
            }
            else if (_runner.IsPaused)
            {
                statusColor = isDarkTheme ? new Color(1f, 0.8f, 0.2f) : new Color(0.8f, 0.6f, 0f);
                statusText = "◐ PAUSED";
            }
            else
            {
                statusColor = isDarkTheme ? new Color(0.3f, 0.9f, 0.3f) : new Color(0.1f, 0.7f, 0.1f);
                statusText = "◉ RUNNING";
            }

            var rect = EditorGUILayout.GetControlRect(false, 22);

            // Draw background
            Color bgColor = isDarkTheme ? new Color(0.2f, 0.2f, 0.2f) : new Color(0.85f, 0.85f, 0.85f);
            EditorGUI.DrawRect(rect, bgColor);

            // Draw status indicator circle
            var indicatorRect = new Rect(rect.x + 8, rect.y + 5, 12, 12);
            EditorGUI.DrawRect(indicatorRect, statusColor);

            // Draw status text
            Styles.StatusLabelStyle.normal.textColor = statusColor;
            var labelRect = new Rect(rect.x + 28, rect.y, rect.width - 28, rect.height);
            EditorGUI.LabelField(labelRect, statusText, Styles.StatusLabelStyle);
        }

        protected virtual void DrawBlackboardSection()
        {
            _showBlackboard = EditorGUILayout.Foldout(_showBlackboard, "BlackBoard Data", true);
            if (!_showBlackboard) return;

            EditorGUILayout.BeginVertical(Styles.BoxStyle);

            var bb = _runner.RuntimeTree?.Blackboard;
            if (bb == null)
            {
                EditorGUILayout.LabelField("No data", EditorStyles.centeredGreyMiniLabel);
            }
            else
            {
                RefreshDebugSnapshot(bb);
                EditorGUI.BeginDisabledGroup(true);
                for (int i = 0; i < _debugEntries.Count; i++)
                {
                    RuntimeBlackboardDebugEntry entry = _debugEntries[i];
                    string label = _debugLabels[i];
                    switch (entry.ValueType)
                    {
                        case RuntimeBlackboardValueType.Int:
                            EditorGUILayout.IntField(label, entry.IntValue);
                            break;
                        case RuntimeBlackboardValueType.Float:
                            EditorGUILayout.FloatField(label, entry.FloatValue);
                            break;
                        case RuntimeBlackboardValueType.Bool:
                            EditorGUILayout.Toggle(label, entry.BoolValue);
                            break;
                        case RuntimeBlackboardValueType.Vector3:
                            EditorGUILayout.Vector3Field(label, entry.VectorValue);
                            break;
                        case RuntimeBlackboardValueType.Object:
                            EditorGUILayout.TextField(label, entry.ObjectValue?.ToString() ?? "NULL");
                            break;
                        case RuntimeBlackboardValueType.Long:
                            EditorGUILayout.LongField(label, entry.LongValue);
                            break;
                        case RuntimeBlackboardValueType.Long2:
                            EditorGUILayout.TextField(label, $"({entry.Long2Value.X}, {entry.Long2Value.Y})");
                            break;
                        case RuntimeBlackboardValueType.Long3:
                            EditorGUILayout.TextField(label, $"({entry.Long3Value.X}, {entry.Long3Value.Y}, {entry.Long3Value.Z})");
                            break;
                    }
                }
                EditorGUI.EndDisabledGroup();
                if (_debugEntries.Count == 0)
                {
                    EditorGUILayout.LabelField("No data", EditorStyles.centeredGreyMiniLabel);
                }
            }

            EditorGUILayout.EndVertical();
        }

        private void RefreshDebugSnapshot(RuntimeBlackboard blackboard)
        {
            double now = EditorApplication.timeSinceStartup;
            bool blackboardChanged = !ReferenceEquals(_debugBlackboard, blackboard);
            if (!blackboardChanged &&
                (Event.current == null || Event.current.type != EventType.Layout || now < _nextDebugRefreshTime))
            {
                return;
            }

            _debugBlackboard = blackboard;
            _nextDebugRefreshTime = now + DEBUG_REFRESH_INTERVAL;
            _debugEntries.Clear();
            _debugLabels.Clear();
            try
            {
                blackboard.CopyDebugEntries(_debugEntries);
            }
            catch (ObjectDisposedException)
            {
                _debugBlackboard = null;
                return;
            }

            RuntimeBlackboardSchema schema = blackboard.Schema;
            for (int i = 0; i < _debugEntries.Count; i++)
            {
                RuntimeBlackboardDebugEntry entry = _debugEntries[i];
                string keyLabel = entry.Key.ToString();
                if (schema != null &&
                    schema.TryGetDefinition(entry.Key, out RuntimeBlackboardKeyDefinition definition))
                {
                    keyLabel = definition.Name;
                }

                _debugLabels.Add($"[{entry.ValueType}] {keyLabel}");
            }
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
                if (IsPropertyDrawnExplicitly(iterator.name))
                {
                    continue;
                }

                EditorGUILayout.PropertyField(iterator, true);
            }
        }

        protected virtual bool IsPropertyDrawnExplicitly(string propertyName)
        {
            return propertyName == "m_Script" ||
                   propertyName == "_startOnAwake" ||
                   propertyName == "_tickMode" ||
                   propertyName == "behaviorTree" ||
                   propertyName == "_initialObjects" ||
                   propertyName == "_blackBoard";
        }
    }
}
