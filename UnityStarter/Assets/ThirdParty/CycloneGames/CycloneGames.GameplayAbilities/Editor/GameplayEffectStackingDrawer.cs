using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Custom property drawer for GameplayEffectStacking struct.
    /// Shows Limit, DurationPolicy, and ExpirationPolicy only when Type is not None.
    /// </summary>
    [CustomPropertyDrawer(typeof(Runtime.GameplayEffectStacking))]
    public class GameplayEffectStackingDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(SerializedProperty property, GUIContent label)
        {
            var typeProp = property.FindPropertyRelative("Type");
            var stackingType = (Runtime.EGameplayEffectStackingType)typeProp.enumValueIndex;

            if (stackingType == Runtime.EGameplayEffectStackingType.None)
            {
                return EditorGUIUtility.singleLineHeight;
            }

            // Type + Limit + DurationPolicy + ExpirationPolicy = 4 lines
            return EditorGUIUtility.singleLineHeight * 4 + EditorGUIUtility.standardVerticalSpacing * 3;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var typeProp = property.FindPropertyRelative("Type");
            var limitProp = property.FindPropertyRelative("Limit");
            var durationPolicyProp = property.FindPropertyRelative("DurationPolicy");
            var expirationPolicyProp = property.FindPropertyRelative("ExpirationPolicy");

            float lineHeight = EditorGUIUtility.singleLineHeight;
            float spacing = EditorGUIUtility.standardVerticalSpacing;

            Rect typeRect = new Rect(position.x, position.y, position.width, lineHeight);
            EditorGUI.PropertyField(typeRect, typeProp, new GUIContent("Stacking Type"));

            var stackingType = (Runtime.EGameplayEffectStackingType)typeProp.enumValueIndex;

            if (stackingType != Runtime.EGameplayEffectStackingType.None)
            {
                EditorGUI.indentLevel++;

                Rect limitRect = new Rect(position.x, position.y + lineHeight + spacing, position.width, lineHeight);
                EditorGUI.PropertyField(limitRect, limitProp, new GUIContent("Stack Limit"));

                Rect durationRect = new Rect(position.x, position.y + (lineHeight + spacing) * 2, position.width, lineHeight);
                EditorGUI.PropertyField(durationRect, durationPolicyProp, new GUIContent("Duration Refresh"));

                Rect expirationRect = new Rect(position.x, position.y + (lineHeight + spacing) * 3, position.width, lineHeight);
                EditorGUI.PropertyField(expirationRect, expirationPolicyProp, new GUIContent("Expiration Policy"));

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }
}
