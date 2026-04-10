using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    /// <summary>
    /// Custom editor for all Actor-derived classes. Shows runtime state (owner, tags, lifecycle).
    /// </summary>
    [CustomEditor(typeof(Actor), true)]
    [CanEditMultipleObjects]
    public class ActorEditor : UnityEditor.Editor
    {
        private static readonly Color editableHeaderColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color readOnlyHeaderColor = new Color(0.30f, 0.50f, 0.70f);

        private bool showEditableConfiguration = true;
        private bool showRuntimeInfo = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var actor = (Actor)target;

            showEditableConfiguration = InspectorUiUtility.DrawFoldoutHeader("Editable Configuration", showEditableConfiguration, editableHeaderColor);
            if (showEditableConfiguration)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Editable Fields", "These fields are writable and persist on the component.", new Color(1f, 0.76f, 0.38f, 1f));
                InspectorUiUtility.DrawSerializedProperties(serializedObject, "tags");
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6);
            showRuntimeInfo = InspectorUiUtility.DrawFoldoutHeader("Read-Only Runtime", showRuntimeInfo, readOnlyHeaderColor);
            if (showRuntimeInfo)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader("Runtime Observation", "This section is read-only and intended for diagnostics.", new Color(0.42f, 0.78f, 1f, 1f));

                if (Application.isPlaying)
                {
                    EditorGUI.indentLevel++;

                    var owner = actor.GetOwner();
                    if (owner != null)
                    {
                        EditorGUILayout.BeginHorizontal();
                        EditorGUILayout.LabelField("Owner:", GUILayout.Width(60));
                        GUI.enabled = false;
                        EditorGUILayout.ObjectField(owner, typeof(Actor), true);
                        GUI.enabled = true;
                        EditorGUILayout.EndHorizontal();
                    }

                    using (new EditorGUI.DisabledScope(true))
                    {
                        EditorGUILayout.LabelField("Can Be Damaged", actor.CanBeDamaged() ? "Yes" : "No");
                        EditorGUILayout.LabelField("Is Hidden", actor.IsHidden() ? "Yes" : "No");
                        EditorGUILayout.LabelField("Has Authority", actor.HasAuthority() ? "Yes" : "No");

                        var tags = actor.GetTags();
                        if (tags != null && tags.Count > 0)
                        {
                            EditorGUILayout.LabelField("Tags:", string.Join(", ", tags));
                        }
                    }

                    EditorGUI.indentLevel--;
                }
                else
                {
                    EditorGUILayout.HelpBox("Runtime status will be displayed here during Play Mode.", MessageType.Info);
                }

                EditorGUILayout.EndVertical();
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
}
