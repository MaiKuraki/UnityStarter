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
        private bool showRuntimeInfo = true;

        public override void OnInspectorGUI()
        {
            var actor = (Actor)target;

            if (Application.isPlaying)
            {
                showRuntimeInfo = EditorGUILayout.Foldout(showRuntimeInfo, "Actor Runtime Info", true, EditorStyles.foldoutHeader);
                if (showRuntimeInfo)
                {
                    EditorGUILayout.BeginVertical(GUI.skin.box);
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

                    EditorGUILayout.LabelField("Can Be Damaged", actor.CanBeDamaged() ? "Yes" : "No");
                    EditorGUILayout.LabelField("Is Hidden", actor.IsHidden() ? "Yes" : "No");
                    EditorGUILayout.LabelField("Has Authority", actor.HasAuthority() ? "Yes" : "No");

                    var tags = actor.GetTags();
                    if (tags != null && tags.Count > 0)
                    {
                        EditorGUILayout.LabelField("Tags:", string.Join(", ", tags));
                    }

                    EditorGUI.indentLevel--;
                    EditorGUILayout.EndVertical();
                    EditorGUILayout.Space(4);
                }
            }

            DrawDefaultInspector();
        }
    }
}
