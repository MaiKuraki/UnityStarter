using UnityEditor;
using UnityEngine;
using CycloneGames.RPGFoundation.Movement.Runtime;

namespace CycloneGames.RPGFoundation.Movement.Editor
{
    [CustomEditor(typeof(MovementAttributeAuthorityBase), true)]
    [CanEditMultipleObjects]
    public sealed class MovementAttributeAuthorityEditor : UnityEditor.Editor
    {
        private readonly string[] _overrideFields =
        {
            "walkSpeedOverride", "runSpeedOverride", "sprintSpeedOverride", "crouchSpeedOverride",
            "jumpForceOverride", "maxJumpCountOverride", "gravityOverride", "airControlMultiplierOverride",
            "rotationSpeedOverride", "climbSpeedOverride", "swimSpeedOverride", "flySpeedOverride"
        };

        private readonly string[] _multiplierFields =
        {
            "walkSpeedMultiplier", "runSpeedMultiplier", "sprintSpeedMultiplier", "crouchSpeedMultiplier",
            "jumpForceMultiplier", "maxJumpCountMultiplier", "gravityMultiplier", "airControlMultiplier",
            "rotationSpeedMultiplier", "climbSpeedMultiplier", "swimSpeedMultiplier", "flySpeedMultiplier"
        };

        private bool _showOverrides;
        private bool _showMultipliers = true;

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            EditorGUILayout.BeginVertical(EditorStyles.helpBox);
            EditorGUILayout.LabelField(
                "Implements IMovementAuthority. The component discovers it with GetComponent.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.LabelField(
                "An enabled override replaces one base value; a multiplier scales it.",
                EditorStyles.wordWrappedMiniLabel);
            EditorGUILayout.EndVertical();

            _showOverrides = EditorGUILayout.Foldout(_showOverrides, "Base Value Overrides", true);
            if (_showOverrides)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _overrideFields.Length; i++)
                {
                    SerializedProperty property = serializedObject.FindProperty(_overrideFields[i]);
                    if (property != null) EditorGUILayout.PropertyField(property);
                }
                EditorGUI.indentLevel--;
            }

            _showMultipliers = EditorGUILayout.Foldout(_showMultipliers, "Multipliers", true);
            if (_showMultipliers)
            {
                EditorGUI.indentLevel++;
                for (int i = 0; i < _multiplierFields.Length; i++)
                {
                    SerializedProperty property = serializedObject.FindProperty(_multiplierFields[i]);
                    if (property != null) EditorGUILayout.PropertyField(property);
                }
                EditorGUI.indentLevel--;
            }

            serializedObject.ApplyModifiedProperties();
        }
    }
    [CustomPropertyDrawer(typeof(MovementFloatOverride))]
    public sealed class MovementFloatOverrideDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            return EditorGUIUtility.singleLineHeight;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            SerializedProperty enabled = property.FindPropertyRelative("Enabled");
            SerializedProperty value = property.FindPropertyRelative("Value");
            if (enabled == null || value == null) return;

            float labelWidth = position.width * 0.52f;
            float toggleWidth = 18f;
            var labelRect = new Rect(position.x, position.y, labelWidth, position.height);
            var toggleRect = new Rect(position.x + labelWidth, position.y, toggleWidth, position.height);
            var valueRect = new Rect(toggleRect.xMax + 4f, position.y,
                position.width - labelWidth - toggleWidth - 4f, position.height);

            EditorGUI.LabelField(labelRect, label);
            EditorGUI.PropertyField(toggleRect, enabled, GUIContent.none);
            EditorGUI.PropertyField(valueRect, value, GUIContent.none);
        }
    }

}
