using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using CycloneGames.GameplayFramework.Runtime;

namespace CycloneGames.GameplayFramework.Runtime.Editor
{
    /// <summary>
    /// Custom Inspector for CameraActionStateBehaviour.
    /// Hides irrelevant exit-key fields based on the selected ExitActionMode to prevent
    /// designer confusion (e.g. configuring OnExitActionKey when PlayActionKey mode is active).
    /// </summary>
    [CustomEditor(typeof(CameraActionStateBehaviour))]
    public class CameraActionStateBehaviourEditor : UnityEditor.Editor
    {
        // Enter
        private SerializedProperty _onEnterActionKey;
        private SerializedProperty _allowEnterTriggerInTransition;

        // Exit
        private SerializedProperty _onExitMode;
        private SerializedProperty _onExitActionKey;
        private SerializedProperty _onExitPlayActionKey;

        // Progress
        private SerializedProperty _onProgressActionKey;
        private SerializedProperty _triggerNormalizedTime;
        private SerializedProperty _triggerEveryLoop;
        private SerializedProperty _allowProgressTriggerInTransition;

        // Duration
        private SerializedProperty _durationOverride;

        private static readonly GUIContent EnterLabel       = new GUIContent("Enter");
        private static readonly GUIContent ExitLabel        = new GUIContent("Exit");
        private static readonly GUIContent ProgressLabel    = new GUIContent("Progress");
        private static readonly GUIContent DurationLabel    = new GUIContent("Duration");

        private static readonly Color enterColor    = new Color(0.30f, 0.55f, 0.45f);
        private static readonly Color exitColor     = new Color(0.55f, 0.35f, 0.35f);
        private static readonly Color progressColor = new Color(0.35f, 0.45f, 0.65f);
        private static readonly Color durationColor = new Color(0.45f, 0.45f, 0.45f);

        private GUIStyle _headerStyle;
        private bool _stylesInitialized;

        private void OnEnable()
        {
            _onEnterActionKey              = serializedObject.FindProperty("onEnterActionKey");
            _allowEnterTriggerInTransition = serializedObject.FindProperty("allowEnterTriggerInTransition");
            _onExitMode                    = serializedObject.FindProperty("onExitMode");
            _onExitActionKey               = serializedObject.FindProperty("onExitActionKey");
            _onExitPlayActionKey           = serializedObject.FindProperty("onExitPlayActionKey");
            _onProgressActionKey           = serializedObject.FindProperty("onProgressActionKey");
            _triggerNormalizedTime         = serializedObject.FindProperty("triggerNormalizedTime");
            _triggerEveryLoop              = serializedObject.FindProperty("triggerEveryLoop");
            _allowProgressTriggerInTransition = serializedObject.FindProperty("allowProgressTriggerInTransition");
            _durationOverride              = serializedObject.FindProperty("durationOverride");
        }

        public override void OnInspectorGUI()
        {
            EnsureStyles();
            serializedObject.Update();

            // ── Enter ────────────────────────────────────────────────────────
            DrawSectionHeader(EnterLabel, enterColor);
            EditorGUILayout.PropertyField(_onEnterActionKey);
            EditorGUILayout.PropertyField(_allowEnterTriggerInTransition);

            EditorGUILayout.Space(4f);

            // ── Exit ─────────────────────────────────────────────────────────
            DrawSectionHeader(ExitLabel, exitColor);
            EditorGUILayout.PropertyField(_onExitMode);

            var mode = (CameraActionStateBehaviour.ExitActionMode)_onExitMode.enumValueIndex;
            switch (mode)
            {
                case CameraActionStateBehaviour.ExitActionMode.StopActionKey:
                    EditorGUILayout.PropertyField(_onExitActionKey);
                    break;
                case CameraActionStateBehaviour.ExitActionMode.PlayActionKey:
                    EditorGUILayout.PropertyField(_onExitPlayActionKey);
                    break;
                // None: no additional fields needed
            }

            EditorGUILayout.Space(4f);

            // ── Progress ─────────────────────────────────────────────────────
            DrawSectionHeader(ProgressLabel, progressColor);
            EditorGUILayout.PropertyField(_onProgressActionKey);

            bool hasProgressKey = !string.IsNullOrEmpty(_onProgressActionKey.stringValue);

            using (new EditorGUI.DisabledGroupScope(!hasProgressKey))
            {
                EditorGUILayout.PropertyField(_triggerNormalizedTime);
                EditorGUILayout.PropertyField(_triggerEveryLoop);
                EditorGUILayout.PropertyField(_allowProgressTriggerInTransition);
            }

            if (!hasProgressKey)
            {
                EditorGUILayout.HelpBox("Set On Progress Action Key to enable mid-state threshold triggering.", MessageType.None);
            }

            EditorGUILayout.Space(4f);

            // ── Duration ─────────────────────────────────────────────────────
            DrawSectionHeader(DurationLabel, durationColor);
            EditorGUILayout.PropertyField(_durationOverride);
            EditorGUILayout.HelpBox("Applied to enter, exit, and progress actions. ≤ 0 uses the preset's own duration.", MessageType.None);

            serializedObject.ApplyModifiedProperties();
        }

        private void DrawSectionHeader(GUIContent label, Color color)
        {
            Rect rect = GUILayoutUtility.GetRect(1f, 20f, GUILayout.ExpandWidth(true));
            EditorGUI.DrawRect(rect, color * (EditorGUIUtility.isProSkin ? 0.6f : 0.5f));
            rect.xMin += 4f;
            GUI.Label(rect, label, _headerStyle);
        }

        private void EnsureStyles()
        {
            if (_stylesInitialized) return;
            _headerStyle = new GUIStyle(EditorStyles.boldLabel)
            {
                normal = { textColor = Color.white },
                fontSize = 11
            };
            _stylesInitialized = true;
        }
    }
}
