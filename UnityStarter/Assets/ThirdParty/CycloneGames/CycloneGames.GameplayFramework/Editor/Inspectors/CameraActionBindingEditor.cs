using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(CameraActionBinding))]
    [CanEditMultipleObjects]
    internal sealed class CameraActionBindingEditor : UnityEditor.Editor
    {
        private SerializedProperty playerController;
        private SerializedProperty autoResolvePlayerController;
        private SerializedProperty actionMap;
        private SerializedProperty actionEntries;
        private SerializedProperty maxActiveActions;
        private SerializedProperty maxPooledModes;

        private void OnEnable()
        {
            playerController = serializedObject.FindProperty("playerController");
            autoResolvePlayerController = serializedObject.FindProperty("autoResolvePlayerController");
            actionMap = serializedObject.FindProperty("actionMap");
            actionEntries = serializedObject.FindProperty("actionEntries");
            maxActiveActions = serializedObject.FindProperty("maxActiveActions");
            maxPooledModes = serializedObject.FindProperty("maxPooledModes");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            InspectorUiUtility.DrawSectionHeader(
                "Controller",
                "Actions are pushed into this local PlayerController camera stack.",
                new Color(0.44f, 0.60f, 0.80f, 1f));
            EditorGUILayout.PropertyField(autoResolvePlayerController);
            using (new EditorGUI.DisabledScope(autoResolvePlayerController.boolValue))
            {
                EditorGUILayout.PropertyField(playerController);
            }

            InspectorUiUtility.DrawSectionHeader(
                "Action Sources",
                "Inline entries take precedence over entries with the same key in the shared map.",
                new Color(0.48f, 0.68f, 0.48f, 1f));
            EditorGUILayout.PropertyField(actionMap);
            EditorGUILayout.PropertyField(actionEntries, includeChildren: true);

            InspectorUiUtility.DrawSectionHeader(
                "Runtime Budgets",
                "Cap simultaneously active actions and retained preset-mode instances.",
                new Color(0.66f, 0.54f, 0.36f, 1f));
            EditorGUILayout.PropertyField(maxActiveActions);
            EditorGUILayout.PropertyField(maxPooledModes);

            if (targets.Length == 1)
            {
                var binding = (CameraActionBinding)target;
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.IntField("Active Actions", binding.ActiveActionCount);
                    EditorGUILayout.IntField("Pooled Modes", binding.PooledModeCount);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
