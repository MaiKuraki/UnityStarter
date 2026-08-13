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

            if (targets.Length == 1)
            {
                var output = (CinemachineCameraOutput)target;
                EditorGUILayout.Space(6f);
                EditorGUILayout.LabelField("Runtime Output", EditorStyles.boldLabel);
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

            serializedObject.ApplyModifiedProperties();
        }
    }
}
