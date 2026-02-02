using UnityEngine;
using UnityEditor;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(TwoStateInteractionBase), true)]
    [CanEditMultipleObjects]
    public class TwoStateInteractionEditor : UnityEditor.Editor
    {
        private TwoStateInteractionBase _target;
        private SerializedProperty _startActivated;

        private static readonly Color ColorActivated = new(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color ColorDeactivated = new(0.6f, 0.6f, 0.6f, 1f);

        // Base class fields to exclude from derived class section
        private static readonly string[] BaseClassFields =
        {
            "m_Script",
            "startActivated",
            // Interactable base fields
            "interactionPrompt",
            "isInteractable",
            "autoInteract",
            "priority",
            "interactionDistance",
            "interactionPoint",
            "interactionCooldown",
            "resetToIdleOnComplete",
            "useLocalization",
            "promptData",
            "onInteract",
            "onFocus",
            "onDefocus"
        };

        private void OnEnable()
        {
            _target = (TwoStateInteractionBase)target;
            _startActivated = serializedObject.FindProperty("startActivated");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            DrawHeader();
            DrawStateControl();

            EditorGUILayout.Space(8);

            DrawDerivedClassFields();

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private new void DrawHeader()
        {
            EditorGUILayout.LabelField("🔀 Two-State Interaction", EditorStyles.boldLabel);
            EditorGUILayout.HelpBox(
                "Binary state object that can be activated or deactivated.\n" +
                "Use for doors, switches, platforms, show/hide elements.",
                MessageType.None);
        }

        private void DrawStateControl()
        {
            EditorGUILayout.Space(4);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_startActivated, new GUIContent("Start Activated", "Initial state when scene loads"));

                if (Application.isPlaying)
                {
                    EditorGUILayout.Space(4);

                    bool isActivated = _target.IsActivated;
                    Color stateColor = isActivated ? ColorActivated : ColorDeactivated;

                    EditorGUILayout.BeginHorizontal();
                    EditorGUILayout.LabelField("Current State:", GUILayout.Width(100));
                    GUI.color = stateColor;
                    EditorGUILayout.LabelField(isActivated ? "ACTIVATED" : "DEACTIVATED", EditorStyles.boldLabel);
                    GUI.color = Color.white;
                    EditorGUILayout.EndHorizontal();

                    EditorGUILayout.Space(4);

                    EditorGUILayout.BeginHorizontal();

                    GUI.backgroundColor = ColorActivated;
                    GUI.enabled = !isActivated;
                    if (GUILayout.Button("▶ Activate", GUILayout.Height(24)))
                    {
                        _target.ActivateState();
                    }

                    GUI.backgroundColor = ColorDeactivated;
                    GUI.enabled = isActivated;
                    if (GUILayout.Button("■ Deactivate", GUILayout.Height(24)))
                    {
                        _target.DeactivateState();
                    }

                    GUI.backgroundColor = Color.white;
                    GUI.enabled = true;

                    if (GUILayout.Button("⟳ Toggle", GUILayout.Height(24)))
                    {
                        _target.ToggleState();
                    }

                    EditorGUILayout.EndHorizontal();
                }
            }
        }

        private void DrawDerivedClassFields()
        {
            // Check if there are any derived class specific fields
            SerializedProperty iterator = serializedObject.GetIterator();
            bool hasDerivedFields = false;

            if (iterator.NextVisible(true))
            {
                do
                {
                    if (System.Array.IndexOf(BaseClassFields, iterator.name) < 0)
                    {
                        hasDerivedFields = true;
                        break;
                    }
                } while (iterator.NextVisible(false));
            }

            if (!hasDerivedFields) return;

            // Draw derived class fields header
            string typeName = target.GetType().Name;
            EditorGUILayout.LabelField($"🔧 {typeName} Settings", EditorStyles.boldLabel);

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                iterator = serializedObject.GetIterator();
                if (iterator.NextVisible(true))
                {
                    do
                    {
                        if (System.Array.IndexOf(BaseClassFields, iterator.name) < 0)
                        {
                            EditorGUILayout.PropertyField(iterator, true);
                        }
                    } while (iterator.NextVisible(false));
                }
            }
        }

        [DrawGizmo(GizmoType.NonSelected | GizmoType.Selected)]
        private static void DrawTwoStateGizmos(TwoStateInteractionBase target, GizmoType gizmoType)
        {
            if (!Application.isPlaying) return;

            bool isSelected = (gizmoType & GizmoType.Selected) != 0;
            if (!isSelected) return;

            Vector3 pos = target.transform.position + Vector3.up * 0.5f;
            Color color = target.IsActivated ? ColorActivated : ColorDeactivated;
            color.a = 0.8f;

            Handles.color = color;
            Handles.DrawSolidDisc(pos, Vector3.up, 0.2f);
            Handles.DrawWireDisc(pos, Vector3.up, 0.3f);

            GUIStyle style = new GUIStyle(EditorStyles.boldLabel)
            {
                alignment = TextAnchor.MiddleCenter,
                normal = { textColor = color }
            };
            string label = target.IsActivated ? "ON" : "OFF";
            Handles.Label(pos + Vector3.up * 0.5f, label, style);
        }
    }
}