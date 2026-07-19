using UnityEditor;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;
using System.Text;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    /// <summary>
    /// Custom editor for all Actor-derived classes. Shows runtime state (owner, tags, lifecycle).
    /// </summary>
    [CustomEditor(typeof(Actor), true)]
    [CanEditMultipleObjects]
    public class ActorEditor : UnityEditor.Editor
    {
        private static readonly string[] paddedProperties = { "tags" };
        private static readonly string[] actorTickProperties = { "PrimaryTickPhase", "StartWithTickEnabled" };
        private static readonly Color editableHeaderColor = new Color(0.50f, 0.58f, 0.38f);
        private static readonly Color readOnlyHeaderColor = new Color(0.30f, 0.50f, 0.70f);

        private bool showEditableConfiguration = true;
        private bool showRuntimeInfo = true;
        private readonly StringBuilder tagBuilder = new StringBuilder(128);

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            var actor = (Actor)target;

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
                ActorTickPhase? codeOwnedPhase = actor is AIController
                    ? ActorTickPhase.Update
                    : actor is CameraManager
                        ? ActorTickPhase.LateUpdate
                        : (ActorTickPhase?)null;
                InspectorUiUtility.DrawActorTickConfiguration(serializedObject, codeOwnedPhase);
                EditorGUILayout.EndVertical();
            }

            EditorGUILayout.Space(6);
            showRuntimeInfo = InspectorUiUtility.DrawFoldoutHeader("Runtime Diagnostics and Controls", showRuntimeInfo, readOnlyHeaderColor);
            if (showRuntimeInfo)
            {
                EditorGUILayout.BeginVertical(GUI.skin.box);
                InspectorUiUtility.DrawSectionHeader(
                    "Runtime State",
                    "Inspect lifecycle and Tick state. Tick controls change only the current Play Mode session.",
                    new Color(0.42f, 0.78f, 1f, 1f));

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
                        EditorGUILayout.LabelField("Lifecycle", actor.LifecycleState.ToString());
                        EditorGUILayout.LabelField("Can Ever Tick", actor.CanEverTick ? "Yes" : "No");
                        EditorGUILayout.LabelField("Tick Phase", actor.TickPhase.ToString());
                        EditorGUILayout.LabelField("Tick Enabled", actor.IsActorTickEnabled() ? "Yes" : "No");
                        EditorGUILayout.ObjectField("World GameMode", actor.World?.GameMode, typeof(GameMode), true);

                        if (actor.TagCount > 0)
                        {
                            tagBuilder.Clear();
                            for (int i = 0; i < actor.TagCount; i++)
                            {
                                if (i > 0) tagBuilder.Append(", ");
                                tagBuilder.Append(actor.GetTagAt(i));
                            }

                            EditorGUILayout.LabelField("Tags:", tagBuilder.ToString());
                        }
                    }

                    if (HasTickableTarget())
                    {
                        EditorGUILayout.Space(4f);
                        EditorGUILayout.BeginHorizontal();
                        if (GUILayout.Button("Enable Runtime Tick"))
                        {
                            SetRuntimeTickEnabled(true);
                        }

                        if (GUILayout.Button("Disable Runtime Tick"))
                        {
                            SetRuntimeTickEnabled(false);
                        }
                        EditorGUILayout.EndHorizontal();
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

        private bool HasTickableTarget()
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is Actor actor && actor.CanEverTick)
                {
                    return true;
                }
            }

            return false;
        }

        private void SetRuntimeTickEnabled(bool enabled)
        {
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] is Actor actor && actor.CanEverTick)
                {
                    actor.SetActorTickEnabled(enabled);
                }
            }

            Repaint();
        }
    }
}
