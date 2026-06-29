using System;
using System.Linq;
using System.Reflection;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.GameplayAbilities.Editor
{
    [CustomPropertyDrawer(typeof(Runtime.AttributeNameSelectorAttribute))]
    public sealed class AttributeNameSelectorDrawer : PropertyDrawer
    {
        private const float BUTTON_WIDTH = 24f;
        private static readonly List<string> s_DisplayOptions = new List<string>(64);
        private static readonly List<string> s_ValueOptions = new List<string>(64);
        private static readonly HashSet<string> s_UniqueValues = new HashSet<string>(StringComparer.Ordinal);

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use [AttributeNameSelector] with string fields only.");
                return;
            }

            Type constantsType = (attribute as Runtime.AttributeNameSelectorAttribute)?.ConstantsType;
            if (constantsType == null)
            {
                EditorGUI.PropertyField(position, property, label);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);

            Rect fieldRect = position;
            fieldRect.width -= BUTTON_WIDTH + 2f;
            Rect buttonRect = position;
            buttonRect.x = fieldRect.xMax + 2f;
            buttonRect.width = BUTTON_WIDTH;

            EditorGUI.PropertyField(fieldRect, property, label);

            if (EditorGUI.DropdownButton(buttonRect, GUIContent.none, FocusType.Keyboard))
            {
                RebuildOptions(constantsType);
                ShowMenu(buttonRect, property);
            }

            EditorGUI.EndProperty();
        }

        private static void ShowMenu(Rect position, SerializedProperty property)
        {
            var menu = new GenericMenu();
            string currentValue = property.stringValue;

            menu.AddItem(new GUIContent("None"), string.IsNullOrEmpty(currentValue), () =>
            {
                property.stringValue = string.Empty;
                property.serializedObject.ApplyModifiedProperties();
            });

            if (s_ValueOptions.Count == 0)
            {
                menu.AddDisabledItem(new GUIContent("No attribute constants found"));
            }
            else
            {
                menu.AddSeparator("");
                for (int i = 0; i < s_ValueOptions.Count; i++)
                {
                    string value = s_ValueOptions[i];
                    string display = s_DisplayOptions[i];
                    menu.AddItem(new GUIContent(display), string.Equals(currentValue, value, StringComparison.Ordinal), () =>
                    {
                        property.stringValue = value;
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
            }

            menu.DropDown(position);
        }

        private static void RebuildOptions(Type constantsType)
        {
            s_DisplayOptions.Clear();
            s_ValueOptions.Clear();
            s_UniqueValues.Clear();

            AddConstantsFromType(constantsType);
            SortOptions();
        }

        private static void AddConstantsFromType(Type type)
        {
            var fields = type.GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
            for (int i = 0; i < fields.Length; i++)
            {
                var field = fields[i];
                if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                {
                    continue;
                }

                string value = field.GetRawConstantValue() as string;
                if (string.IsNullOrEmpty(value))
                {
                    continue;
                }

                if (s_UniqueValues.Add(value))
                {
                    s_ValueOptions.Add(value);
                    s_DisplayOptions.Add(value.Replace('.', '/'));
                }
            }
        }

        private static void SortOptions()
        {
            for (int i = 1; i < s_ValueOptions.Count; i++)
            {
                string value = s_ValueOptions[i];
                string display = s_DisplayOptions[i];
                int j = i - 1;
                while (j >= 0 && string.CompareOrdinal(s_DisplayOptions[j], display) > 0)
                {
                    s_ValueOptions[j + 1] = s_ValueOptions[j];
                    s_DisplayOptions[j + 1] = s_DisplayOptions[j];
                    j--;
                }

                s_ValueOptions[j + 1] = value;
                s_DisplayOptions[j + 1] = display;
            }
        }
    }

    /// <summary>
    /// An abstract base PropertyDrawer for string fields marked with [AttributeNameSelector].
    /// It renders a dropdown menu populated with public constant string values from a specified Type.
    /// This approach decouples the core drawer logic from project-specific constant definitions.
    /// </summary>
    public abstract class AttributeNameSelectorDrawer_Base : PropertyDrawer
    {
        // Static cache to store reflected constant data, avoiding repeated reflection calls per repaint.
        private static readonly Dictionary<Type, CachedConstantData> s_constantsCache = new Dictionary<Type, CachedConstantData>();

        // A static, reusable GUIContent to avoid allocations in OnGUI.
        private static readonly GUIContent s_tempContent = new GUIContent();

        /// <summary>
        /// A private class to hold the cached data from reflection.
        /// </summary>
        private class CachedConstantData
        {
            public readonly string[] DisplayOptions;
            public readonly string[] ValueOptions;
            public readonly Dictionary<string, string> ValueToDisplayMap;

            public CachedConstantData(string[] displayOptions, string[] valueOptions)
            {
                DisplayOptions = displayOptions;
                ValueOptions = valueOptions;
                ValueToDisplayMap = new Dictionary<string, string>(ValueOptions.Length);

                for (int i = 0; i < ValueOptions.Length; i++)
                {
                    // Gracefully handle potential duplicate values by only adding the first one found.
                    if (!ValueToDisplayMap.ContainsKey(ValueOptions[i]))
                    {
                        ValueToDisplayMap.Add(ValueOptions[i], DisplayOptions[i]);
                    }
                }
            }
        }

        /// <summary>
        /// Contract for subclasses. Must be implemented to provide the Type that contains the attribute string constants.
        /// </summary>
        protected abstract Type GetConstantsType();

        /// <summary>
        /// Subclasses can override this to specify the character used for creating hierarchical menus.
        /// The default '.' separator will render constants like "Parent.Child" as "Parent/Child" in the dropdown.
        /// </summary>
        protected virtual char GetSeparator() => '.';

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label.text, "Use [AttributeNameSelector] with string fields only.");
                return;
            }

            Type constantsType = GetConstantsType();
            if (constantsType == null)
            {
                EditorGUI.LabelField(position, label.text, "Constants Type is not provided by the concrete drawer.");
                return;
            }

            CachedConstantData cachedData = GetAndCacheConstants(constantsType);

            if (cachedData == null || cachedData.DisplayOptions.Length == 0)
            {
                EditorGUI.LabelField(position, label.text, $"No public const strings found in {constantsType.Name}.");
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            position = EditorGUI.PrefixLabel(position, label);

            string currentValue = property.stringValue;

            // Determine button text without allocating new strings for the common cases.
            s_tempContent.text = "Select Attribute...";
            bool isValueValid = !string.IsNullOrEmpty(currentValue) && cachedData.ValueToDisplayMap.ContainsKey(currentValue);

            var originalBackgroundColor = GUI.backgroundColor;

            if (isValueValid)
            {
                s_tempContent.text = cachedData.ValueToDisplayMap[currentValue].Replace(GetSeparator(), '/');
            }
            else if (!string.IsNullOrEmpty(currentValue))
            {
                GUI.backgroundColor = Color.red;
                s_tempContent.text = $"[Invalid] {currentValue}";
            }

            if (EditorGUI.DropdownButton(position, s_tempContent, FocusType.Keyboard))
            {
                // GenericMenu construction happens only on-click, which is an acceptable allocation cost.
                var menu = new GenericMenu();
                menu.AddItem(new GUIContent("None"), string.IsNullOrEmpty(currentValue), () =>
                {
                    property.stringValue = string.Empty;
                    property.serializedObject.ApplyModifiedProperties();
                });
                menu.AddSeparator("");

                for (int i = 0; i < cachedData.DisplayOptions.Length; i++)
                {
                    string displayName = cachedData.DisplayOptions[i];
                    string value = cachedData.ValueOptions[i];

                    // The allocation for menuPath only occurs when building the menu on click.
                    string menuPath = displayName.Replace(GetSeparator(), '/');

                    menu.AddItem(new GUIContent(menuPath), currentValue == value, () =>
                    {
                        property.stringValue = value;
                        property.serializedObject.ApplyModifiedProperties();
                    });
                }
                menu.DropDown(position);
            }

            GUI.backgroundColor = originalBackgroundColor;
            EditorGUI.EndProperty();
        }

        /// <summary>
        /// Retrieves constant data from the cache. If not present, it performs reflection and populates the cache.
        /// This version uses lists to avoid the multiple allocations from LINQ's ToArray().
        /// </summary>
        private static CachedConstantData GetAndCacheConstants(Type constantsType)
        {
            if (s_constantsCache.TryGetValue(constantsType, out CachedConstantData cachedData))
            {
                return cachedData;
            }

            // Perform reflection once and cache the results.
            var stringFields = constantsType
                .GetFields(BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy)
                .Where(f => f.IsLiteral && !f.IsInitOnly && f.FieldType == typeof(string))
                .ToList();

            // Use lists with pre-defined capacity to minimize allocations during population.
            var values = new List<string>(stringFields.Count);
            foreach (var field in stringFields)
            {
                values.Add((string)field.GetValue(null));
            }

            // For this implementation, both display and value options are the same.
            cachedData = new CachedConstantData(values.ToArray(), values.ToArray());

            s_constantsCache[constantsType] = cachedData;
            return cachedData;
        }
    }
}
