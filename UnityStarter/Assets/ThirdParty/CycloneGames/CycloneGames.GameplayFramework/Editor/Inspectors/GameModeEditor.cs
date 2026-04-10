using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(GameMode), true)]
    public class GameModeEditor : UnityEditor.Editor
    {
        private static readonly Color editableHeaderColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color readOnlyHeaderColor = new Color(0.30f, 0.50f, 0.70f);

        private bool showEditableConfiguration = true;
        private bool showReadOnlyRuntime = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var gm = (GameMode)target;

            showEditableConfiguration = InspectorUiUtility.DrawFoldoutHeader("Editable Configuration", showEditableConfiguration, editableHeaderColor);
            if (showEditableConfiguration)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Editable Fields", "These fields are writable and persist on the component.", new Color(1f, 0.76f, 0.38f, 1f));
                InspectorUiUtility.DrawSerializedProperties(serializedObject, "tags");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6);
            showReadOnlyRuntime = InspectorUiUtility.DrawFoldoutHeader("Read-Only Runtime", showReadOnlyRuntime, readOnlyHeaderColor);
            if (showReadOnlyRuntime)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Runtime Observation", "This section is read-only and intended for diagnostics.", new Color(0.42f, 0.78f, 1f, 1f));

                if (Application.isPlaying)
                {
                    var pc = gm.GetPlayerController();
                    if (pc != null)
                    {
                        GUI.color = new Color(0.7f, 1f, 0.7f);
                        EditorGUILayout.LabelField("PlayerController:", EditorStyles.miniBoldLabel);
                        EditorGUILayout.BeginHorizontal();
                        GUI.enabled = false;
                        EditorGUILayout.ObjectField(pc, typeof(PlayerController), true);
                        GUI.enabled = true;
                        if (GUILayout.Button("Ping", GUILayout.Width(50)))
                            EditorGUIUtility.PingObject(pc);
                        EditorGUILayout.EndHorizontal();

                        var pawn = pc.GetPawn();
                        if (pawn != null)
                        {
                            EditorGUILayout.LabelField("Possessed Pawn:", EditorStyles.miniBoldLabel);
                            EditorGUILayout.BeginHorizontal();
                            GUI.enabled = false;
                            EditorGUILayout.ObjectField(pawn, typeof(Pawn), true);
                            GUI.enabled = true;
                            if (GUILayout.Button("Ping", GUILayout.Width(50)))
                                EditorGUIUtility.PingObject(pawn);
                            EditorGUILayout.EndHorizontal();
                        }
                        else
                        {
                            GUI.color = Color.yellow;
                            EditorGUILayout.LabelField("Possessed Pawn: None", EditorStyles.miniLabel);
                        }
                    }
                    else
                    {
                        GUI.color = Color.gray;
                        EditorGUILayout.LabelField("PlayerController: Not spawned yet", EditorStyles.miniLabel);
                    }
                }
                else
                {
                    GUI.color = Color.gray;
                    EditorGUILayout.HelpBox("Runtime status will be displayed here during Play Mode.", MessageType.Info);
                }

                GUI.color = Color.white;
                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
