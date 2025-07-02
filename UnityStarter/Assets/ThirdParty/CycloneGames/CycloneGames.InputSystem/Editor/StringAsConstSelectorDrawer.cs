using UnityEngine;
using UnityEditor;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Attribute to mark a string field to be drawn as a dropdown of constants from a specified type.
    /// </summary>
    public class StringAsConstSelectorAttribute : PropertyAttribute
    {
        public System.Type ConstantsType { get; }
        public StringAsConstSelectorAttribute(System.Type constantsType)
        {
            ConstantsType = constantsType;
        }
    }

    [CustomPropertyDrawer(typeof(StringAsConstSelectorAttribute))]
    public class StringAsConstSelectorDrawer : PropertyDrawer
    {
        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use StringAsConstSelector with string fields only.");
                return;
            }

            var attrib = attribute as StringAsConstSelectorAttribute;
            if (attrib == null) return;
            
            // Get all public constant string fields from the specified type
            var stringFields = attrib.ConstantsType.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();

            if (stringFields.Count == 0)
            {
                EditorGUI.LabelField(position, label.text, $"No public const strings found in {attrib.ConstantsType.Name}.");
                return;
            }

            var displayOptions = stringFields.Select(f => f.Name).ToArray();
            var valueOptions = stringFields.Select(f => (string)f.GetValue(null)).ToArray();
            
            int currentIndex = System.Array.IndexOf(valueOptions, property.stringValue);
            if (currentIndex < 0) currentIndex = 0; // Default to first option if current value is not found

            EditorGUI.BeginProperty(position, label, property);
            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, displayOptions);
            
            if (newIndex != currentIndex)
            {
                property.stringValue = valueOptions[newIndex];
            }
            EditorGUI.EndProperty();
        }
    }
}