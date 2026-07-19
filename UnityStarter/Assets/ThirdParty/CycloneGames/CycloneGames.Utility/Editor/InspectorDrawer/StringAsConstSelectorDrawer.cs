using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEditor;
using UnityEngine;
using CycloneGames.Utility.Runtime;

namespace CycloneGames.Utility.Editor
{
    [CustomPropertyDrawer(typeof(StringAsConstSelectorAttribute))]
    public sealed class StringAsConstSelectorDrawer : PropertyDrawer
    {
        private const float PICKER_WIDTH = 24f;
        private const float CONTROL_GAP = 3f;

        private static readonly Dictionary<Type, CachedConstantData> ConstantsCache =
            new Dictionary<Type, CachedConstantData>();

        private static readonly GUIContent WrongPropertyTypeContent =
            new GUIContent("Use [StringAsConstSelector] with string fields only.");

        private static readonly GUIContent MissingAttributeContent =
            new GUIContent("StringAsConstSelectorAttribute is unavailable.");

        private static readonly GUIContent MissingConstantsTypeContent =
            new GUIContent("The constants type is not configured.");

        private static readonly GUIContent PickerContent =
            new GUIContent("\u25be", "Choose a known constant without disabling custom input.");

        private static readonly GUIContent NoneContent = new GUIContent("None");

        private static readonly GUIContent InvalidValueContent =
            new GUIContent(string.Empty, "This value is not declared by the configured constants type.");

        public override void OnGUI(Rect position, SerializedProperty property, GUIContent label)
        {
            if (property.propertyType != SerializedPropertyType.String)
            {
                EditorGUI.LabelField(position, label, WrongPropertyTypeContent);
                return;
            }

            StringAsConstSelectorAttribute selectorAttribute = attribute as StringAsConstSelectorAttribute;
            if (selectorAttribute == null)
            {
                EditorGUI.LabelField(position, label, MissingAttributeContent);
                return;
            }

            CachedConstantData cachedData = GetAndCacheConstants(selectorAttribute.ConstantsType);
            if (cachedData.ErrorContent != null)
            {
                EditorGUI.LabelField(position, label, cachedData.ErrorContent);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            bool previousShowMixedValue = EditorGUI.showMixedValue;
            int previousIndentLevel = EditorGUI.indentLevel;
            EditorGUI.showMixedValue = property.hasMultipleDifferentValues;

            try
            {
                Rect contentPosition = EditorGUI.PrefixLabel(position, label);
                EditorGUI.indentLevel = 0;
                string currentValue = property.stringValue;
                bool isValueValid = string.IsNullOrEmpty(currentValue) ||
                                    cachedData.ValueToIndexMap.ContainsKey(currentValue);

                if (selectorAttribute.AllowCustom)
                {
                    DrawEditableSelector(contentPosition, property, selectorAttribute, cachedData);
                }
                else if (!isValueValid)
                {
                    DrawInvalidState(contentPosition, property, selectorAttribute, cachedData);
                }
                else if (selectorAttribute.UseMenu)
                {
                    DrawMenuSelector(contentPosition, property, selectorAttribute, cachedData);
                }
                else
                {
                    DrawPopupSelector(contentPosition, property, cachedData);
                }
            }
            finally
            {
                EditorGUI.indentLevel = previousIndentLevel;
                EditorGUI.showMixedValue = previousShowMixedValue;
                EditorGUI.EndProperty();
            }
        }

        private static void DrawEditableSelector(
            Rect position,
            SerializedProperty property,
            StringAsConstSelectorAttribute selectorAttribute,
            CachedConstantData cachedData)
        {
            float textWidth = Mathf.Max(0f, position.width - PICKER_WIDTH - CONTROL_GAP);
            Rect textRect = new Rect(position.x, position.y, textWidth, position.height);
            Rect pickerRect = new Rect(textRect.xMax + CONTROL_GAP, position.y, PICKER_WIDTH, position.height);

            EditorGUI.BeginChangeCheck();
            string value = EditorGUI.TextField(textRect, property.stringValue ?? string.Empty);
            if (EditorGUI.EndChangeCheck())
            {
                property.stringValue = value;
            }

            if (EditorGUI.DropdownButton(
                    pickerRect,
                    PickerContent,
                    FocusType.Keyboard,
                    EditorStyles.miniButton))
            {
                ShowSelectionMenu(pickerRect, property, selectorAttribute, cachedData);
            }
        }

        private static void DrawInvalidState(
            Rect position,
            SerializedProperty property,
            StringAsConstSelectorAttribute selectorAttribute,
            CachedConstantData cachedData)
        {
            Color previousBackgroundColor = GUI.backgroundColor;
            InvalidValueContent.text = property.stringValue ?? string.Empty;

            try
            {
                GUI.backgroundColor = new Color(1f, 0.55f, 0.55f, 1f);
                if (EditorGUI.DropdownButton(position, InvalidValueContent, FocusType.Keyboard))
                {
                    ShowSelectionMenu(position, property, selectorAttribute, cachedData);
                }
            }
            finally
            {
                GUI.backgroundColor = previousBackgroundColor;
            }
        }

        private static void DrawPopupSelector(
            Rect position,
            SerializedProperty property,
            CachedConstantData cachedData)
        {
            bool found = cachedData.ValueToIndexMap.TryGetValue(property.stringValue, out int valueIndex);
            int currentIndex = found ? valueIndex + 1 : 0;

            int newIndex = EditorGUI.Popup(position, currentIndex, cachedData.PopupDisplayOptions);
            if (newIndex != currentIndex && newIndex >= 0 && newIndex < cachedData.PopupDisplayOptions.Length)
            {
                property.stringValue = newIndex == 0
                    ? string.Empty
                    : cachedData.ValueOptions[newIndex - 1];
            }
        }

        private static void DrawMenuSelector(
            Rect position,
            SerializedProperty property,
            StringAsConstSelectorAttribute selectorAttribute,
            CachedConstantData cachedData)
        {
            GUIContent buttonContent = NoneContent;
            if (!string.IsNullOrEmpty(property.stringValue) &&
                cachedData.ValueToIndexMap.TryGetValue(property.stringValue, out int currentIndex))
            {
                buttonContent = cachedData.GetMenuData(selectorAttribute.Separator).OptionContents[currentIndex];
            }

            if (EditorGUI.DropdownButton(position, buttonContent, FocusType.Keyboard))
            {
                ShowSelectionMenu(position, property, selectorAttribute, cachedData);
            }
        }

        private static void ShowSelectionMenu(
            Rect position,
            SerializedProperty property,
            StringAsConstSelectorAttribute selectorAttribute,
            CachedConstantData cachedData)
        {
            GenericMenu menu = new GenericMenu();
            UnityEngine.Object[] targets = CopyTargets(property.serializedObject.targetObjects);
            string propertyPath = property.propertyPath;
            bool hasMixedValues = property.hasMultipleDifferentValues;
            CachedMenuData menuData = cachedData.GetMenuData(selectorAttribute.Separator);

            menu.AddItem(
                NoneContent,
                !hasMixedValues && string.IsNullOrEmpty(property.stringValue),
                ApplySelection,
                new SelectionCommand(targets, propertyPath, string.Empty));
            menu.AddSeparator(string.Empty);

            for (int i = 0; i < cachedData.ValueOptions.Length; i++)
            {
                string value = cachedData.ValueOptions[i];
                menu.AddItem(
                    selectorAttribute.UseMenu ? menuData.OptionContents[i] : cachedData.DisplayOptions[i],
                    !hasMixedValues && string.Equals(property.stringValue, value, StringComparison.Ordinal),
                    ApplySelection,
                    new SelectionCommand(targets, propertyPath, value));
            }

            menu.DropDown(position);
        }

        private static void ApplySelection(object userData)
        {
            SelectionCommand command = userData as SelectionCommand;
            if (command == null)
            {
                return;
            }

            UnityEngine.Object[] validTargets = GetValidTargets(command.Targets);
            if (validTargets.Length == 0)
            {
                return;
            }

            using (var serializedTargets = new SerializedObject(validTargets))
            {
                serializedTargets.UpdateIfRequiredOrScript();
                SerializedProperty liveProperty = serializedTargets.FindProperty(command.PropertyPath);
                if (liveProperty == null || liveProperty.propertyType != SerializedPropertyType.String)
                {
                    return;
                }

                liveProperty.stringValue = command.Value;
                serializedTargets.ApplyModifiedProperties();
            }
        }

        private static UnityEngine.Object[] CopyTargets(UnityEngine.Object[] sourceTargets)
        {
            UnityEngine.Object[] targets = new UnityEngine.Object[sourceTargets.Length];
            Array.Copy(sourceTargets, targets, sourceTargets.Length);
            return targets;
        }

        private static UnityEngine.Object[] GetValidTargets(UnityEngine.Object[] targets)
        {
            int validCount = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    validCount++;
                }
            }

            if (validCount == targets.Length)
            {
                return targets;
            }

            if (validCount == 0)
            {
                return Array.Empty<UnityEngine.Object>();
            }

            UnityEngine.Object[] validTargets = new UnityEngine.Object[validCount];
            int writeIndex = 0;
            for (int i = 0; i < targets.Length; i++)
            {
                if (targets[i] != null)
                {
                    validTargets[writeIndex++] = targets[i];
                }
            }

            return validTargets;
        }

        private static CachedConstantData GetAndCacheConstants(Type constantsType)
        {
            if (constantsType == null)
            {
                return CachedConstantData.MissingType;
            }

            if (ConstantsCache.TryGetValue(constantsType, out CachedConstantData cachedData))
            {
                return cachedData;
            }

            cachedData = BuildConstantData(constantsType);
            ConstantsCache.Add(constantsType, cachedData);
            return cachedData;
        }

        private static CachedConstantData BuildConstantData(Type constantsType)
        {
            try
            {
                FieldInfo[] fields = constantsType.GetFields(
                    BindingFlags.Public | BindingFlags.Static | BindingFlags.FlattenHierarchy);
                List<ReflectedConstant> reflectedConstants = new List<ReflectedConstant>(fields.Length);

                for (int i = 0; i < fields.Length; i++)
                {
                    FieldInfo field = fields[i];
                    if (!field.IsLiteral || field.IsInitOnly || field.FieldType != typeof(string))
                    {
                        continue;
                    }

                    string value = field.GetRawConstantValue() as string;
                    if (string.IsNullOrEmpty(value))
                    {
                        continue;
                    }

                    reflectedConstants.Add(new ReflectedConstant(
                        field.Name,
                        value,
                        field.DeclaringType == null ? string.Empty : field.DeclaringType.FullName));
                }

                reflectedConstants.Sort(ReflectedConstantComparer.Instance);

                List<ReflectedConstant> uniqueConstants =
                    new List<ReflectedConstant>(reflectedConstants.Count);
                HashSet<string> uniqueValues = new HashSet<string>(StringComparer.Ordinal);
                for (int i = 0; i < reflectedConstants.Count; i++)
                {
                    ReflectedConstant reflectedConstant = reflectedConstants[i];
                    if (uniqueValues.Add(reflectedConstant.Value))
                    {
                        uniqueConstants.Add(reflectedConstant);
                    }
                }

                if (uniqueConstants.Count == 0)
                {
                    string message = string.Concat(
                        "No non-empty public const string values were found in ",
                        constantsType.FullName,
                        ".");
                    return CachedConstantData.CreateError(message);
                }

                return new CachedConstantData(uniqueConstants);
            }
            catch (Exception exception)
            {
                string message = string.Concat(
                    "Failed to read constants from ",
                    constantsType.FullName,
                    " (",
                    exception.GetType().Name,
                    ").");
                return CachedConstantData.CreateError(message);
            }
        }

        private sealed class SelectionCommand
        {
            public readonly UnityEngine.Object[] Targets;
            public readonly string PropertyPath;
            public readonly string Value;

            public SelectionCommand(UnityEngine.Object[] targets, string propertyPath, string value)
            {
                Targets = targets;
                PropertyPath = propertyPath;
                Value = value;
            }
        }

        private readonly struct ReflectedConstant
        {
            public readonly string DisplayName;
            public readonly string Value;
            public readonly string DeclaringTypeName;

            public ReflectedConstant(string displayName, string value, string declaringTypeName)
            {
                DisplayName = displayName;
                Value = value;
                DeclaringTypeName = declaringTypeName;
            }
        }

        private sealed class ReflectedConstantComparer : IComparer<ReflectedConstant>
        {
            public static readonly ReflectedConstantComparer Instance = new ReflectedConstantComparer();

            private ReflectedConstantComparer()
            {
            }

            public int Compare(ReflectedConstant x, ReflectedConstant y)
            {
                int nameComparison = string.Compare(x.DisplayName, y.DisplayName, StringComparison.Ordinal);
                if (nameComparison != 0)
                {
                    return nameComparison;
                }

                return string.Compare(x.DeclaringTypeName, y.DeclaringTypeName, StringComparison.Ordinal);
            }
        }

        private sealed class CachedConstantData
        {
            public static readonly CachedConstantData MissingType =
                CreateError(MissingConstantsTypeContent.text);

            public readonly GUIContent[] DisplayOptions;
            public readonly GUIContent[] PopupDisplayOptions;
            public readonly string[] ValueOptions;
            public readonly Dictionary<string, int> ValueToIndexMap;
            public readonly GUIContent ErrorContent;

            private readonly string[] _displayNames;
            private readonly Dictionary<char, CachedMenuData> _menuDataBySeparator;

            public CachedConstantData(List<ReflectedConstant> constants)
            {
                int count = constants.Count;
                _displayNames = new string[count];
                DisplayOptions = new GUIContent[count];
                PopupDisplayOptions = new GUIContent[count + 1];
                PopupDisplayOptions[0] = NoneContent;
                ValueOptions = new string[count];
                ValueToIndexMap = new Dictionary<string, int>(count, StringComparer.Ordinal);
                _menuDataBySeparator = new Dictionary<char, CachedMenuData>();

                for (int i = 0; i < count; i++)
                {
                    ReflectedConstant reflectedConstant = constants[i];
                    _displayNames[i] = reflectedConstant.DisplayName;
                    DisplayOptions[i] = new GUIContent(reflectedConstant.DisplayName);
                    PopupDisplayOptions[i + 1] = DisplayOptions[i];
                    ValueOptions[i] = reflectedConstant.Value;
                    ValueToIndexMap.Add(reflectedConstant.Value, i);
                }
            }

            private CachedConstantData(GUIContent errorContent)
            {
                _displayNames = Array.Empty<string>();
                DisplayOptions = Array.Empty<GUIContent>();
                PopupDisplayOptions = Array.Empty<GUIContent>();
                ValueOptions = Array.Empty<string>();
                ValueToIndexMap = new Dictionary<string, int>(0, StringComparer.Ordinal);
                _menuDataBySeparator = new Dictionary<char, CachedMenuData>();
                ErrorContent = errorContent;
            }

            public static CachedConstantData CreateError(string message)
            {
                return new CachedConstantData(new GUIContent(message));
            }

            public CachedMenuData GetMenuData(char separator)
            {
                if (_menuDataBySeparator.TryGetValue(separator, out CachedMenuData menuData))
                {
                    return menuData;
                }

                GUIContent[] optionContents = new GUIContent[_displayNames.Length];
                for (int i = 0; i < _displayNames.Length; i++)
                {
                    string menuPath = _displayNames[i].Replace(separator, '/');
                    optionContents[i] = new GUIContent(menuPath);
                }

                menuData = new CachedMenuData(optionContents);
                _menuDataBySeparator.Add(separator, menuData);
                return menuData;
            }
        }

        private sealed class CachedMenuData
        {
            public readonly GUIContent[] OptionContents;

            public CachedMenuData(GUIContent[] optionContents)
            {
                OptionContents = optionContents;
            }
        }
    }
}
