using CycloneGames.GameplayFramework.Runtime.Integrations.Cinemachine;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor.Integrations.Cinemachine
{
    [CustomEditor(typeof(CinemachineCameraOutput))]
    [CanEditMultipleObjects]
    public sealed class CinemachineCameraOutputEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();
            SerializedProperty discoveryProperty =
                serializedObject.FindProperty("allowSceneDiscovery");
            if (discoveryProperty != null && discoveryProperty.hasMultipleDifferentValues)
            {
                EditorGUILayout.HelpBox(
                    "The selected outputs use different scene-discovery settings.",
                    MessageType.Info);
            }
            else if (discoveryProperty != null && discoveryProperty.boolValue)
            {
                EditorGUILayout.HelpBox(
                    "Scene discovery is an opt-in cold-path scan. It succeeds only when the output Scene contains an unambiguous CinemachineCamera and CinemachineBrain. Assign both references explicitly for deterministic composition.",
                    MessageType.Warning);
            }
            else
            {
                EditorGUILayout.HelpBox(
                    "Assign the CinemachineCamera and CinemachineBrain explicitly. Scene discovery is disabled by default.",
                    MessageType.Info);
            }

            if (targets.Length == 1 && Application.isPlaying)
            {
                var output = (CinemachineCameraOutput)target;
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Runtime Output", EditorStyles.boldLabel);
                if (!output.gameObject.activeInHierarchy)
                {
                    EditorGUILayout.HelpBox(
                        "Runtime output state is available after this component enters the Play Mode lifecycle.",
                        MessageType.Info);
                    serializedObject.ApplyModifiedProperties();
                    return;
                }

                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle("Active", output.IsActive);
                    EditorGUILayout.ObjectField(
                        "Virtual Camera",
                        output.ActiveVirtualCamera,
                        typeof(Unity.Cinemachine.CinemachineCamera),
                        allowSceneObjects: true);
                    EditorGUILayout.ObjectField(
                        "Brain",
                        output.ActiveBrain,
                        typeof(Unity.Cinemachine.CinemachineBrain),
                        allowSceneObjects: true);
                }
            }
            else if (targets.Length == 1)
            {
                EditorGUILayout.HelpBox(
                    "Runtime Cinemachine ownership is available in Play Mode.",
                    MessageType.Info);
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
