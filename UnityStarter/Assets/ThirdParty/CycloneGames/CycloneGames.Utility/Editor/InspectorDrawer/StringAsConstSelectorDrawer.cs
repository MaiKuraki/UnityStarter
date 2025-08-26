using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    [CustomPropertyDrawer(typeof(StringAsConstSelectorAttribute))]
    public class StringAsConstSelectorDrawer : PropertyDrawer
    {
        /// <summary>
        /// Caches the reflected constant data per type to avoid repeated reflection and allocation.
        /// The cache is automatically cleared and rebuilt on domain reloads (e.g., script compilation).
        /// </summary>
        private static readonly Dictionary<Type, CachedConstantData> s_constantsCache = new Dictionary<Type, CachedConstantData>();

        private class CachedConstantData
        {
            public readonly string[] DisplayOptions;
            public readonly string[] ValueOptions;
            public readonly Dictionary<string, int> ValueToIndexMap;
            public readonly Dictionary<string, string> ValueToDisplayMap;

            public CachedConstantData(List<FieldInfo> stringFields)
            {
                DisplayOptions = stringFields.Select(f => f.Name).ToArray();
                ValueOptions = stringFields.Select(f => (string)f.GetValue(null)).ToArray();

                ValueToIndexMap = new Dictionary<string, int>(ValueOptions.Length);
                ValueToDisplayMap = new Dictionary<string, string>(ValueOptions.Length);

                for (int i = 0; i < ValueOptions.Length; i++)
                {
                    ValueToIndexMap[ValueOptions[i]] = i;
                    // handling potential duplicate values gracefully.
                    if (!ValueToDisplayMap.ContainsKey(ValueOptions[i]))
                    {
                        ValueToDisplayMap.Add(ValueOptions[i], DisplayOptions[i]);
                    }
                }
            }
        }

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            // Ensure this drawer is used only on string properties.
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use [StringAsConstSelector] with string fields only.");
                return;
            }

            var attrib = attribute as StringAsConstSelectorAttribute;
            if (attrib == null)
            {
                EditorGUI.LabelField(position, label.text, "Attribute could not be found.");
                return;
            }

            // Fetch the constant data from cache or create it if it doesn't exist.
            CachedConstantData cachedData = GetAndCacheConstants(attrib.ConstantsType);

            if (cachedData == null || cachedData.DisplayOptions.Length == 0)
            {
                EditorGUI.LabelField(position, label.text, $"No public const strings found in {attrib.ConstantsType.Name}.");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            string currentValue = property.stringValue;
            bool isValueValid = string.IsNullOrEmpty(currentValue) || cachedData.ValueToDisplayMap.ContainsKey(currentValue);

            if (!isValueValid)
            {
                DrawInvalidStateUI(position, property, label, attrib, cachedData);
            }
            else if (attrib.UseMenu)
            {
                DrawAsMenu(position, property, label, attrib, cachedData);
            }
            else
            {
                DrawAsPopup(position, property, label, cachedData);
            }

            EditorGUI.EndProperty();
        }

        private void DrawInvalidStateUI(Rect position, SerializedProperty property, GUIContent label, StringAsConstSelectorAttribute attrib, CachedConstantData cachedData)
        {
            var originalColor = GUI.backgroundColor;
            GUI.backgroundColor = Color.red;

            string buttonText = $"INVALID: '{property.stringValue}'";
            if (EditorGUI.DropdownButton(position, new GUIContent(buttonText), FocusType.Keyboard))
            {
                ShowSelectionMenu(position, property, attrib, cachedData);
            }

            GUI.backgroundColor = originalColor;
        }


        private void DrawAsPopup(Rect position, SerializedProperty property, GUIContent label, CachedConstantData cachedData)
        {
            cachedData.ValueToIndexMap.TryGetValue(property.stringValue, out int currentIndex);

            // The display options for popup should be the raw field names
            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, cachedData.DisplayOptions);

            if (newIndex != currentIndex && newIndex >= 0 && newIndex < cachedData.ValueOptions.Length)
            {
                property.stringValue = cachedData.ValueOptions[newIndex];
            }
        }

        private void DrawAsMenu(Rect position, SerializedProperty property, GUIContent label, StringAsConstSelectorAttribute attrib, CachedConstantData cachedData)
        {
            // Determine the text for the dropdown button.
            string buttonText = "None";
            if (!string.IsNullOrEmpty(property.stringValue) && cachedData.ValueToDisplayMap.TryGetValue(property.stringValue, out string currentDisplayName))
            {
                string pretty = MakePrettyDisplayName(currentDisplayName, property.stringValue);
                buttonText = pretty.Replace(attrib.Separator, '/');
            }

            // Draw the dropdown button
            if (EditorGUI.DropdownButton(position, new GUIContent(buttonText), FocusType.Keyboard))
            {
                var menu = new GenericMenu();
                for (int i = 0; i < cachedData.DisplayOptions.Length; i++)
                {
                    string displayName = cachedData.DisplayOptions[i];
                    string value = cachedData.ValueOptions[i];
                    string pretty = MakePrettyDisplayName(displayName, value);
                    string menuPath = pretty.Replace(attrib.Separator, '/');

                    menu.AddItem(new GUIContent(menuPath), property.stringValue == value, () =>
                    {
                        property.stringValue = value;
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.DropDown(position);
            }
        }

        private void ShowSelectionMenu(Rect position, SerializedProperty property, StringAsConstSelectorAttribute attrib, CachedConstantData cachedData)
        {
            var menu = new GenericMenu();

            menu.AddItem(new GUIContent("None"), string.IsNullOrEmpty(property.stringValue), () =>
            {
                property.stringValue = string.Empty;
                property.serializedObject.ApplyModifiedProperties();
            });
            menu.AddSeparator("");

            for (int i = 0; i < cachedData.DisplayOptions.Length; i++)
            {
                string displayName = cachedData.DisplayOptions[i];
                string value = cachedData.ValueOptions[i];
                string pretty = MakePrettyDisplayName(displayName, value);
                string menuPath = pretty.Replace(attrib.Separator, '/');

                menu.AddItem(new GUIContent(menuPath), property.stringValue == value, () =>
                {
                    property.stringValue = value;
                    property.serializedObject.ApplyModifiedProperties();
                });
            }
            menu.DropDown(position);
        }

        /// <summary>
        /// Retrieves constant data from the cache. If not present, it performs reflection and populates the cache.
        /// </summary>
        /// <param name="constantsType">The type to analyze for constants.</param>
        /// <returns>The cached data for the specified type, or null if reflection fails.</returns>
        private static CachedConstantData GetAndCacheConstants(Type constantsType)
        {
            if (s_constantsCache.TryGetValue(constantsType, out CachedConstantData cachedData))
            {
                return cachedData;
            }

            // If not in cache, perform reflection once.
            var stringFields = constantsType
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();

            var newData = new CachedConstantData(stringFields);
            s_constantsCache[constantsType] = newData;
            return newData;
        }

        /// <summary>
        /// Adds context hints to display names without altering the underlying value.
        /// For example, show "Mouse/Delta(Vector2)" for "<Mouse>/delta".
        /// </summary>
        private static string MakePrettyDisplayName(string displayName, string value)
        {
            if (string.Equals(value, "<Mouse>/delta", StringComparison.OrdinalIgnoreCase))
            {
                return displayName + "(Vector2)";
            }
            return displayName;
        }
    }
}