using CycloneGames.GameplayFramework.Runtime;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(CameraOutputBehaviour), true)]
    [CanEditMultipleObjects]
    internal class CameraOutputBehaviourEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            serializedObject.Update();
            DrawDefaultInspector();

            if (targets.Length == 1)
            {
                var output = (CameraOutputBehaviour)target;
                EditorGUILayout.Space(6f);
                InspectorUiUtility.DrawSectionHeader(
                    "Output Status",
                    "CameraManager owns activation and release through the active World.",
                    new Color(0.42f, 0.62f, 0.82f, 1f));
                using (new EditorGUI.DisabledScope(true))
                {
                    EditorGUILayout.Toggle("Active", output.IsActive);
                    EditorGUILayout.ObjectField(
                        "Output Object",
                        output.OutputObject,
                        typeof(Object),
                        allowSceneObjects: true);
                    EditorGUILayout.ObjectField(
                        "Owner",
                        output.Owner,
                        typeof(CameraManager),
                        allowSceneObjects: true);
                }
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
