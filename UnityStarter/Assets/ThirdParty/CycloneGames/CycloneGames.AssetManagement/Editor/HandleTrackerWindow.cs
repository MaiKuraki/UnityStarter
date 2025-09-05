using UnityEditor;
using UnityEngine;
using CycloneGames.AssetManagement.Runtime;
using System.Collections.Generic;

namespace CycloneGames.AssetManagement.Editor
{
    public class HandleTrackerWindow : EditorWindow
    {
        private List<HandleTracker.HandleInfo> activeHandles;
        private Vector2 scrollPosition;

        [MenuItem("Window/CycloneGames/Asset Management/Handle Tracker")]
        public static void ShowWindow()
        {
            GetWindow<HandleTrackerWindow>("Handle Tracker");
        }

        private void OnEnable()
        {
            RefreshHandles();
        }

        private void Update()
        {
            if (HandleTracker.Enabled && (activeHandles == null || activeHandles.Count != HandleTracker.GetActiveHandles().Count))
            {
                RefreshHandles();
                Repaint();
            }
        }

        private void OnGUI()
        {
            EditorGUILayout.LabelField("Asset Handle Tracker", EditorStyles.boldLabel);
            
            bool wasEnabled = HandleTracker.Enabled;
            HandleTracker.Enabled = EditorGUILayout.Toggle("Enable Tracking", wasEnabled);

            if (wasEnabled != HandleTracker.Enabled)
            {
                RefreshHandles();
            }

            if (GUILayout.Button("Refresh"))
            {
                RefreshHandles();
            }

            EditorGUILayout.Space();

            if (!HandleTracker.Enabled)
            {
                EditorGUILayout.HelpBox("Tracking is disabled. Enable to see active handles.", MessageType.Info);
                return;
            }

            if (activeHandles == null || activeHandles.Count == 0)
            {
                EditorGUILayout.LabelField("No active handles.");
                return;
            }

            EditorGUILayout.LabelField($"Active Handles: {activeHandles.Count}");

            scrollPosition = EditorGUILayout.BeginScrollView(scrollPosition);

            foreach (var handle in activeHandles)
            {
                EditorGUILayout.BeginVertical(EditorStyles.helpBox);
                EditorGUILayout.LabelField($"ID: {handle.Id}", EditorStyles.boldLabel);
                EditorGUILayout.LabelField("Package:", handle.PackageName);
                EditorGUILayout.LabelField("Description:", handle.Description);
                EditorGUILayout.LabelField("Registered At:", handle.RegistrationTime.ToLocalTime().ToString("HH:mm:ss.fff"));
                EditorGUILayout.EndVertical();
                EditorGUILayout.Space(2);
            }

            EditorGUILayout.EndScrollView();
        }

        private void RefreshHandles()
        {
            activeHandles = HandleTracker.GetActiveHandles();
        }
    }
}
