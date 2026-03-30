using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(GameMode), true)]
    public class GameModeEditor : UnityEditor.Editor
    {
        public override void OnInspectorGUI()
        {
            var gm = (GameMode)target;

            EditorGUILayout.LabelField("Game Mode Status", EditorStyles.boldLabel);
            EditorGUILayout.BeginVertical(GUI.skin.box);

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
            EditorGUILayout.Space(8);

            DrawDefaultInspector();
        }
    }
}
