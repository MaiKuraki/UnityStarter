using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Editor
{
    /// <summary>
    /// Custom property drawer for GameplayEffectStacking struct.
    /// Only shows Limit and DurationPolicy when Type is not None.
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

            // Type + Limit + DurationPolicy = 3 lines
            return EditorGUIUtility.singleLineHeight * 3 + EditorGUIUtility.standardVerticalSpacing * 2;
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            EditorGUI.BeginProperty(position, label, property);

            var typeProp = property.FindPropertyRelative("Type");
            var limitProp = property.FindPropertyRelative("Limit");
            var durationPolicyProp = property.FindPropertyRelative("DurationPolicy");

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

                Rect policyRect = new Rect(position.x, position.y + (lineHeight + spacing) * 2, position.width, lineHeight);
                EditorGUI.PropertyField(policyRect, durationPolicyProp, new GUIContent("Duration Refresh"));

                EditorGUI.indentLevel--;
            }

            EditorGUI.EndProperty();
        }
    }
}
