using System;

using UnityEditor;
using UnityEngine;

using CachedConstantData = CycloneGames.InputSystem.Editor.StringAsConstSelectorCache.CachedConstantData;
using CachedMenuData = CycloneGames.InputSystem.Editor.StringAsConstSelectorCache.CachedMenuData;

namespace CycloneGames.InputSystem.Editor
{
    /// <summary>
    /// Package-local property drawer for <see cref="StringAsConstSelectorAttribute"/>.
    /// </summary>
    /// <remarks>
    /// Registration binds to the local attribute type only, so this drawer never intercepts
    /// fields annotated by the CycloneGames.Utility copy. The draw path is allocation-free;
    /// menu construction, target copying and one command object per option are cold paths
    /// that run only when the picker is opened.
    /// </remarks>
    [CustomPropertyDrawer(typeof(StringAsConstSelectorAttribute))]
    internal sealed class StringAsConstSelectorDrawer : PropertyDrawer
    {
        private const float PickerWidth = 24f;
        private const float ControlGap = 3f;

        private static readonly GUIContent WrongPropertyTypeContent =
            new GUIContent("Use [StringAsConstSelector] with string fields only.");

        private static readonly GUIContent MissingAttributeContent =
            new GUIContent("StringAsConstSelectorAttribute is unavailable.");

        private static readonly GUIContent PickerContent =
            new GUIContent("\u25be", "Choose a known constant without disabling custom input.");

        // Shared by design: IMGUI consumes the content synchronously inside the same OnGUI call,
        // so refreshing text immediately before the draw keeps the hot path allocation-free.
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

            CachedConstantData cachedData = StringAsConstSelectorCache.Get(selectorAttribute.ConstantsType);
            if (cachedData.ErrorContent != null)
            {
                EditorGUI.LabelField(position, label, cachedData.ErrorContent);
                return;
            }

            EditorGUI.BeginProperty(position, label, property);
            bool previousShowMixedValue = EditorGUI.showMixedValue;
            int previousIndentLevel = EditorGUI.indentLevel;
            try
            {
                EditorGUI.showMixedValue = property.hasMultipleDifferentValues;
                Rect contentPosition = EditorGUI.PrefixLabel(position, label);
                EditorGUI.indentLevel = 0;

                if (selectorAttribute.AllowCustom)
                {
                    DrawEditableSelector(contentPosition, property, selectorAttribute, cachedData);
                    return;
                }

                string currentValue = property.stringValue;
                bool isValueValid = string.IsNullOrEmpty(currentValue) ||
                                    cachedData.ValueToIndexMap.ContainsKey(currentValue);
                if (!isValueValid)
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
            float textWidth = Mathf.Max(0f, position.width - PickerWidth - ControlGap);
            Rect textRect = new Rect(position.x, position.y, textWidth, position.height);
            Rect pickerRect = new Rect(textRect.xMax + ControlGap, position.y, PickerWidth, position.height);

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
            GUIContent buttonContent = StringAsConstSelectorCache.NoneContent;
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
            GUIContent noneContent = StringAsConstSelectorCache.NoneContent;

            menu.AddItem(
                noneContent,
                !hasMixedValues && string.IsNullOrEmpty(property.stringValue),
                ApplySelection,
                new SelectionCommand(targets, propertyPath, string.Empty));
            menu.AddSeparator(string.Empty);

            for (int index = 0; index < cachedData.ValueOptions.Length; index++)
            {
                string value = cachedData.ValueOptions[index];
                menu.AddItem(
                    selectorAttribute.UseMenu ? menuData.OptionContents[index] : cachedData.DisplayOptions[index],
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
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] != null)
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
            for (int index = 0; index < targets.Length; index++)
            {
                if (targets[index] != null)
                {
                    validTargets[writeIndex++] = targets[index];
                }
            }

            return validTargets;
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
    }
}
