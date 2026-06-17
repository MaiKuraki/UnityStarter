using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Runtime.Interaction;

namespace CycloneGames.RPGFoundation.Editor.Interaction
{
    [CustomEditor(typeof(TwoStateInteractionBase), true)]
    [CanEditMultipleObjects]
    public class TwoStateInteractionEditor : UnityEditor.Editor
    {
        private TwoStateInteractionBase _target;
        private SerializedProperty _startActivated;

        private static bool s_stateFoldout = true;
        private static bool s_runtimeFoldout = true;

        private static readonly Color ColorActivated = new(0.3f, 0.9f, 0.4f, 1f);
        private static readonly Color ColorDeactivated = new(0.6f, 0.6f, 0.6f, 1f);

        private static GUIStyle s_gizmoLabelStyle;

        private static readonly string[] BaseClassFields =
        {
            "m_Script",
            "startActivated",
            "interactionPrompt",
            "isInteractable",
            "autoInteract",
            "priority",
            "interactionDistance",
            "interactionPoint",
            "interactionCooldown",
            "resetToIdleOnComplete",
            "channel",
            "useLocalization",
            "promptData",
            "actions",
            "holdDuration",
            "maxInteractionRange",
            "positionUpdateThreshold",
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

            EditorGUILayout.LabelField("Two-State Interaction", EditorStyles.boldLabel);
            InteractionInspectorUiUtility.DrawHelpBox(
                "Binary state helper for doors, switches, platforms, show/hide objects, and other toggle-style interactions.",
                MessageType.None);

            DrawStateControl();
            DrawRuntimeControl();
            InteractionInspectorUiUtility.DrawDerivedProperties(
                serializedObject,
                target.GetType().Name + " Settings",
                BaseClassFields);

            serializedObject.ApplyModifiedProperties();

            if (Application.isPlaying)
                Repaint();
        }

        private void DrawStateControl()
        {
            s_stateFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "State",
                s_stateFoldout,
                InteractionInspectorUiUtility.ColorBehavior);
            if (!s_stateFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                EditorGUILayout.PropertyField(_startActivated, new GUIContent("Start Activated"));
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Initial State", GUILayout.Width(100f));
                Rect badgeRect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(140f));
                InteractionInspectorUiUtility.DrawStatusBadge(
                    badgeRect,
                    _startActivated.boolValue ? "Activated" : "Deactivated",
                    _startActivated.boolValue ? ColorActivated : ColorDeactivated);
                EditorGUILayout.EndHorizontal();

                InteractionInspectorUiUtility.DrawHelpBox(
                    "Start Activated is copied into runtime state during Awake. Changing it while playing does not rewrite the already-active state.",
                    MessageType.None);
            }
        }

        private void DrawRuntimeControl()
        {
            if (!Application.isPlaying)
                return;

            s_runtimeFoldout = InteractionInspectorUiUtility.DrawFoldoutHeader(
                "Runtime Control",
                s_runtimeFoldout,
                InteractionInspectorUiUtility.ColorRuntime);
            if (!s_runtimeFoldout)
                return;

            using (new EditorGUILayout.VerticalScope(EditorStyles.helpBox))
            {
                bool isActivated = _target.IsActivated;
                EditorGUILayout.BeginHorizontal();
                EditorGUILayout.LabelField("Current State", GUILayout.Width(100f));
                Rect badgeRect = EditorGUILayout.GetControlRect(false, 18f, GUILayout.Width(140f));
                InteractionInspectorUiUtility.DrawStatusBadge(
                    badgeRect,
                    isActivated ? "Activated" : "Deactivated",
                    isActivated ? ColorActivated : ColorDeactivated);
                EditorGUILayout.EndHorizontal();

                EditorGUILayout.BeginHorizontal();
                using (new EditorGUI.DisabledScope(isActivated))
                {
                    if (GUILayout.Button("Activate"))
                        _target.ActivateState();
                }

                using (new EditorGUI.DisabledScope(!isActivated))
                {
                    if (GUILayout.Button("Deactivate"))
                        _target.DeactivateState();
                }

                if (GUILayout.Button("Toggle"))
                    _target.ToggleState();
                EditorGUILayout.EndHorizontal();
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

            if (s_gizmoLabelStyle == null)
            {
                s_gizmoLabelStyle = new GUIStyle(EditorStyles.boldLabel)
                {
                    alignment = TextAnchor.MiddleCenter
                };
            }

            s_gizmoLabelStyle.normal.textColor = color;
            Handles.Label(pos + Vector3.up * 0.5f, target.IsActivated ? "ON" : "OFF", s_gizmoLabelStyle);
        }
    }
}
