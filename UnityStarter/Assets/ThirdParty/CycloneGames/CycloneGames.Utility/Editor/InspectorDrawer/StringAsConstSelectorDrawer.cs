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

            public CachedConstantData(List<FieldInfo> stringFields)
            {
                DisplayOptions = stringFields.Select(f => f.Name).ToArray();
                ValueOptions = stringFields.Select(f => (string)f.GetValue(null)).ToArray();
                ValueToIndexMap = new Dictionary<string, int>(ValueOptions.Length);
                for (int i = 0; i < ValueOptions.Length; i++)
                {
                    ValueToIndexMap[ValueOptions[i]] = i;
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

            // Find the current index of the property's value. Use the fast dictionary lookup.
            cachedData.ValueToIndexMap.TryGetValue(property.stringValue, out int currentIndex);

            int newIndex = EditorGUI.Popup(position, label.text, currentIndex, cachedData.DisplayOptions);

            // If the user selected a new value, update the property.
            if (newIndex != currentIndex)
            {
                property.stringValue = cachedData.ValueOptions[newIndex];
            }

            EditorGUI.EndProperty();
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
    }
}