using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    /// <summary>
    /// Custom editor for the CameraManager class and its subclasses.
    /// Displays runtime camera state during Play Mode.
    /// </summary>
    [CustomEditor(typeof(CameraManager), true)]
    public class CameraManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var cameraManager = (CameraManager)target;

            EditorGUILayout.LabelField("Camera Manager Status", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(GUI.skin.box);

            if (Application.isPlaying)
            {
                var activeCamera = cameraManager.ActiveVirtualCamera;

                if (activeCamera != null)
                {
                    GUI.color = new Color(0.7f, 1.0f, 0.7f);
                    EditorGUILayout.LabelField("Active Camera:", EditorStyles.miniBoldLabel);
                    EditorGUILayout.BeginHorizontal();
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(activeCamera, typeof(CinemachineCamera), false);
                    GUI.enabled = true;
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(activeCamera);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField("Active Camera: None", EditorStyles.boldLabel);
                }
            }
            else
            {
                GUI.color = Color.gray;
                EditorGUILayout.HelpBox("Active camera information will be displayed here during Play Mode.", MessageType.Info);
            }

            GUI.color = Color.white;
            EditorGUILayout.EndVertical();
            EditorGUILayout.Space(10);
            DrawDefaultInspector();
        }
    }
}
