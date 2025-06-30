using UnityEditor;
using UnityEngine;
using Unity.Cinemachine;
using CycloneGames.GameplayFramework;

namespace CycloneGames.GameplayFramework.Editor
{
    /// <summary>
    /// Custom editor for the CameraManager class and its children.
    /// It provides a clear, runtime-aware inspector view.
    /// The 'true' in the attribute means this editor will also be used for any class that inherits from CameraManager.
    /// </summary>
    [CustomEditor(typeof(CameraManager), true)]
    public class CameraManagerEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            // Get the instance of the CameraManager we are inspecting.
            var cameraManager = (CameraManager)target;

            // Draw a title for our custom inspector section.
            EditorGUILayout.LabelField("Camera Manager Status", EditorStyles.boldLabel);

            // Begin a styled group box for our status display.
            EditorGUILayout.BeginVertical(GUI.skin.box);

            // Check if the application is currently in Play Mode.
            if (Application.isPlaying)
            {
                var activeCamera = cameraManager.ActiveVirtualCamera;

                if (activeCamera != null)
                {
                    // If a camera is active, use a green color tint.
                    GUI.color = new Color(0.7f, 1.0f, 0.7f); // A pleasant light green

                    EditorGUILayout.LabelField("Active Camera:", EditorStyles.miniBoldLabel);

                    // Create a horizontal layout for the object field and the ping button.
                    EditorGUILayout.BeginHorizontal();

                    // Display the active camera in a read-only object field.
                    // We disable the GUI temporarily to make the field not editable.
                    GUI.enabled = false;
                    EditorGUILayout.ObjectField(activeCamera, typeof(Unity.Cinemachine.CinemachineCamera), false);
                    GUI.enabled = true;

                    // Add a small "Ping" button to quickly find the object in the hierarchy.
                    if (GUILayout.Button("Ping", GUILayout.Width(50)))
                    {
                        EditorGUIUtility.PingObject(activeCamera);
                    }
                    EditorGUILayout.EndHorizontal();
                }
                else
                {
                    // If no camera is active, use a yellow/gray tint for warning.
                    GUI.color = Color.yellow;
                    EditorGUILayout.LabelField("Active Camera: None", EditorStyles.boldLabel);
                }
            }
            else
            {
                // If not in play mode, show an informative help box.
                GUI.color = Color.gray;
                EditorGUILayout.HelpBox("Active camera information will be displayed here during Play Mode.", MessageType.Info);
            }

            // IMPORTANT: Reset the GUI color back to default.
            GUI.color = Color.white;

            EditorGUILayout.EndVertical();

            // Add some space before drawing the rest of the inspector.
            EditorGUILayout.Space(10);

            // Draw the default inspector for all other public/serialized fields.
            // This ensures that fields from the base class (like DefaultFOV) and
            // fields from subclasses (like your 'virtualCameras' list) are still drawn.
            DrawDefaultInspector();
        }
    }
}
