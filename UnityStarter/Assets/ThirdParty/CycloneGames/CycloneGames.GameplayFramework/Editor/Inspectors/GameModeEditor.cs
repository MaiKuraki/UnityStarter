using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    [CustomEditor(typeof(GameMode), true)]
    public class GameModeEditor : UnityEditor.Editor
    {
        private static readonly string[] paddedProperties = { "tags" };
        private static readonly string[] actorTickProperties = { "PrimaryTickPhase", "StartWithTickEnabled" };
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
                InspectorUiUtility.DrawSerializedPropertiesExcluding(
                    serializedObject,
                    paddedProperties,
                    actorTickProperties);
                EditorGUILayout.Space(4f);
                InspectorUiUtility.DrawActorTickConfiguration(serializedObject);
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
                    EditorGUILayout.LabelField("Lifecycle", gm.ModeState.ToString());
                    EditorGUILayout.LabelField("Tick Phase", gm.TickPhase.ToString());
                    EditorGUILayout.LabelField("Tick Enabled", gm.IsActorTickEnabled() ? "Yes" : "No");
                    IGameSession session = gm.GetGameSession();
                    if (session != null)
                    {
                        EditorGUILayout.LabelField("Players", $"{session.PlayerCount} / {session.MaxPlayers}");
                        EditorGUILayout.LabelField("Spectators", $"{session.SpectatorCount} / {session.MaxSpectators}");
                    }

                    int controllerCount = gm.World?.PlayerControllers.Count ?? 0;
                    EditorGUILayout.LabelField("Player Controllers", controllerCount.ToString());
                    for (int i = 0; i < controllerCount; i++)
                    {
                        PlayerController pc = gm.World.PlayerControllers[i];
                        EditorGUILayout.BeginHorizontal();
                        using (new EditorGUI.DisabledScope(true))
                        {
                            EditorGUILayout.ObjectField($"[{i}]", pc, typeof(PlayerController), true);
                        }

                        if (pc != null && GUILayout.Button("Ping", GUILayout.Width(50f)))
                        {
                            EditorGUIUtility.PingObject(pc);
                        }

                        EditorGUILayout.EndHorizontal();
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
