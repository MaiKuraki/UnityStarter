using System;
using CycloneGames.BehaviorTree.Runtime.Components;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors
{
    [CustomEditor(typeof(BTRunnerComponent))]
    [CanEditMultipleObjects]
    public class BTRunnerComponentEditor : UnityEditor.Editor
    {
        private Color _lastColor;
        private BTRunnerComponent _runner => (BTRunnerComponent)target;
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            _lastColor = GUI.backgroundColor;
            SingleBehaviorTreePanel();
            if (Application.isPlaying)
            {
                GUILayout.Label("Controller");
                GUILayout.Box("", new GUIStyle() { fixedHeight = 1, stretchWidth = true, normal = { background = Texture2D.grayTexture } });
                EditorGUILayout.BeginHorizontal();
                if (_runner.IsStopped)
                {
                    GUI.backgroundColor = Color.green;
                    if (GUILayout.Button("Play")) Array.ForEach(targets, t => ((BTRunnerComponent)t)?.Play());
                }
                else
                {
                    GUI.backgroundColor = Color.red;
                    if (GUILayout.Button("Stop")) Array.ForEach(targets, t => ((BTRunnerComponent)t)?.Stop());
                    if (_runner.IsPaused)
                    {
                        GUI.backgroundColor = Color.blue;
                        if (GUILayout.Button("Resume")) Array.ForEach(targets, t => ((BTRunnerComponent)t)?.Resume());
                    }
                    else
                    {
                        GUI.backgroundColor = Color.yellow;
                        if (GUILayout.Button("Pause")) Array.ForEach(targets, t => ((BTRunnerComponent)t)?.Pause());
                    }
                }
                EditorGUILayout.EndHorizontal();
            }
            GUI.backgroundColor = _lastColor;
            SingleBlackBoardPanel();
            serializedObject.ApplyModifiedProperties();
        }

        protected virtual void SingleBehaviorTreePanel()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_startOnAwake"));
            EditorGUILayout.BeginHorizontal();
            EditorGUILayout.PropertyField(serializedObject.FindProperty("behaviorTree"));
            if (GUILayout.Button("Open Editor", GUILayout.Width(100)))
            {
                BehaviorTreeEditor.OpenWindow();
            }
            EditorGUILayout.EndHorizontal();
        }

        private void SingleBlackBoardPanel()
        {
            EditorGUILayout.PropertyField(serializedObject.FindProperty("_initialObjects"));
            GUILayout.Box("", new GUIStyle() { fixedHeight = 1, stretchWidth = true, normal = { background = Texture2D.grayTexture } });
            GUILayout.Label("BlackBoard");
            GUILayout.Box("", new GUIStyle() { fixedHeight = 1, stretchWidth = true, normal = { background = Texture2D.whiteTexture } });

            var allData = _runner.BlackBoard.GetAllData();
            if (allData.Count == 0)
            {
                EditorGUI.BeginDisabledGroup(true);
                EditorGUILayout.LabelField("No data");
                EditorGUI.EndDisabledGroup();
                return;
            }
            EditorGUI.BeginDisabledGroup(true);
            foreach (var data in allData)
            {
                var key = data.Key;
                var value = data.Value;
                EditorGUILayout.TextField(key, value == null ? "NULL" : value.ToString());
            }
            EditorGUI.EndDisabledGroup();
        }
    }
}