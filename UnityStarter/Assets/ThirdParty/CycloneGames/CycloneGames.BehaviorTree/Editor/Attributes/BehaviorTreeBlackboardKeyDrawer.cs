using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Attributes;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using UnityEditor;
using UnityEditor.IMGUI.Controls;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.BehaviorTree.Editor.Attributes
{
    [CustomPropertyDrawer(typeof(BehaviorTreeBlackboardKeyAttribute))]
    internal sealed class BehaviorTreeBlackboardKeyDrawer : PropertyDrawer
    {
        public override float GetPropertyHeight(
            SerializedProperty property,
            GUIContent label)
        {
            var keyAttribute = (BehaviorTreeBlackboardKeyAttribute)attribute;
            return BehaviorTreeBlackboardKeyGui.GetHeight(
                property,
                keyAttribute.HasExpectedType,
                keyAttribute.ExpectedType,
                keyAttribute.AllowEmpty);
        }

        public override void OnGUI(
            Rect position,
            SerializedProperty property,
            GUIContent label)
        {
            var keyAttribute = (BehaviorTreeBlackboardKeyAttribute)attribute;
            BehaviorTreeBlackboardKeyGui.Draw(
                position,
                property,
                label,
                keyAttribute.HasExpectedType,
                keyAttribute.ExpectedType,
                keyAttribute.AllowEmpty);
        }
    }

    internal static class BehaviorTreeBlackboardKeyGui
    {
        private const float ButtonWidth = 22f;
        private const float Spacing = 2f;
        private const float WarningHeight = 36f;

        private static readonly GUIContent DropdownContent = new GUIContent(
            string.Empty,
            "Select a key from the strict blackboard schema. Manual text editing remains available.");

        internal static float GetHeight(
            SerializedProperty property,
            bool hasExpectedType,
            RuntimeBlackboardValueType expectedType,
            bool allowEmpty)
        {
            KeyContext context = ResolveContext(property, hasExpectedType, expectedType, allowEmpty);
            return EditorGUIUtility.singleLineHeight +
                   (context.Warning == null ? 0f : Spacing + WarningHeight);
        }

        internal static void DrawLayout(
            SerializedProperty property,
            GUIContent label,
            bool hasExpectedType,
            RuntimeBlackboardValueType expectedType,
            bool allowEmpty)
        {
            float height = GetHeight(property, hasExpectedType, expectedType, allowEmpty);
            Rect rect = EditorGUILayout.GetControlRect(true, height);
            Draw(rect, property, label, hasExpectedType, expectedType, allowEmpty);
        }

        internal static void Draw(
            Rect position,
            SerializedProperty property,
            GUIContent label,
            bool hasExpectedType,
            RuntimeBlackboardValueType expectedType,
            bool allowEmpty)
        {
            EditorGUI.BeginProperty(position, label, property);
            KeyContext context = ResolveContext(property, hasExpectedType, expectedType, allowEmpty);
            Rect lineRect = new Rect(
                position.x,
                position.y,
                position.width,
                EditorGUIUtility.singleLineHeight);
            Rect valueRect = EditorGUI.PrefixLabel(lineRect, label);
            Rect textRect = new Rect(
                valueRect.x,
                valueRect.y,
                Mathf.Max(0f, valueRect.width - ButtonWidth - Spacing),
                valueRect.height);
            Rect buttonRect = new Rect(
                textRect.xMax + Spacing,
                valueRect.y,
                ButtonWidth,
                valueRect.height);

            EditorGUI.PropertyField(textRect, property, GUIContent.none);
            using (new EditorGUI.DisabledScope(!context.CanSelect))
            {
                if (EditorGUI.DropdownButton(buttonRect, DropdownContent, FocusType.Keyboard))
                {
                    ShowDropdown(
                        buttonRect,
                        property,
                        context.Schema,
                        hasExpectedType,
                        expectedType,
                        allowEmpty);
                }
            }

            if (context.Warning != null)
            {
                Rect warningRect = new Rect(
                    position.x,
                    lineRect.yMax + Spacing,
                    position.width,
                    WarningHeight);
                EditorGUI.HelpBox(warningRect, context.Warning, MessageType.Warning);
            }

            EditorGUI.EndProperty();
        }

        private static KeyContext ResolveContext(
            SerializedProperty property,
            bool hasExpectedType,
            RuntimeBlackboardValueType expectedType,
            bool allowEmpty)
        {
            if (property == null || property.propertyType != SerializedPropertyType.String)
            {
                return new KeyContext(null, false, "Blackboard key selector requires a serialized string field.");
            }

            if (!TryResolveCommonTree(property.serializedObject, out Runtime.BehaviorTree tree))
            {
                return new KeyContext(null, false, null);
            }

            if (tree == null || !tree.BlackboardSchemaEnabled)
            {
                return new KeyContext(null, false, null);
            }

            if (!tree.TryGetRuntimeBlackboardSchema(
                    out RuntimeBlackboardSchema schema,
                    out string schemaError))
            {
                return new KeyContext(null, false, schemaError ?? "The strict blackboard schema is invalid.");
            }

            string warning = null;
            if (!property.hasMultipleDifferentValues)
            {
                string key = property.stringValue;
                if (string.IsNullOrEmpty(key))
                {
                    if (!allowEmpty)
                    {
                        warning = "A blackboard key is required.";
                    }
                }
                else
                {
                    int hash = RuntimeBlackboard.DefaultStringHashFunc(key);
                    if (hash == 0)
                    {
                        warning = "This key hashes to the reserved zero value.";
                    }
                    else if (!schema.TryGetDefinition(hash, out RuntimeBlackboardKeyDefinition definition))
                    {
                        warning = "This key is not declared by the strict blackboard schema.";
                    }
                    else if (!string.Equals(definition.Name, key, StringComparison.Ordinal))
                    {
                        warning = "This key collides with a different declared key hash.";
                    }
                    else if (hasExpectedType && definition.ValueType != expectedType)
                    {
                        warning = "The selected key type does not match this field's required blackboard type.";
                    }
                }
            }

            return new KeyContext(schema, allowEmpty || schema.Count > 0, warning);
        }

        private static bool TryResolveCommonTree(
            SerializedObject serializedObject,
            out Runtime.BehaviorTree tree)
        {
            tree = null;
            if (serializedObject == null)
            {
                return false;
            }

            if (!serializedObject.isEditingMultipleObjects)
            {
                tree = ResolveTree(serializedObject.targetObject);
                return true;
            }

            Object[] targets = serializedObject.targetObjects;
            for (int i = 0; i < targets.Length; i++)
            {
                Runtime.BehaviorTree current = ResolveTree(targets[i]);
                if (i == 0)
                {
                    tree = current;
                }
                else if (!ReferenceEquals(tree, current))
                {
                    tree = null;
                    return false;
                }
            }

            return true;
        }

        private static Runtime.BehaviorTree ResolveTree(Object target)
        {
            if (target is BTNode node)
            {
                return node.Tree;
            }

            if (target is BTRunnerComponent runner)
            {
                return runner.Tree;
            }

            return target as Runtime.BehaviorTree;
        }

        private static void ShowDropdown(
            Rect buttonRect,
            SerializedProperty property,
            RuntimeBlackboardSchema schema,
            bool hasExpectedType,
            RuntimeBlackboardValueType expectedType,
            bool allowEmpty)
        {
            Object[] targets = property.serializedObject.targetObjects;
            var dropdown = new BlackboardKeyDropdown(
                new AdvancedDropdownState(),
                schema,
                hasExpectedType,
                expectedType,
                allowEmpty,
                targets,
                property.propertyPath);
            dropdown.Show(buttonRect);
        }

        private readonly struct KeyContext
        {
            internal KeyContext(
                RuntimeBlackboardSchema schema,
                bool canSelect,
                string warning)
            {
                Schema = schema;
                CanSelect = canSelect;
                Warning = warning;
            }

            internal RuntimeBlackboardSchema Schema { get; }
            internal bool CanSelect { get; }
            internal string Warning { get; }
        }

        private sealed class BlackboardKeyDropdown : AdvancedDropdown
        {
            private static readonly Comparison<RuntimeBlackboardKeyDefinition> NameComparison =
                CompareByName;

            private readonly RuntimeBlackboardSchema _schema;
            private readonly bool _hasExpectedType;
            private readonly RuntimeBlackboardValueType _expectedType;
            private readonly bool _allowEmpty;
            private readonly Object[] _targets;
            private readonly string _propertyPath;

            internal BlackboardKeyDropdown(
                AdvancedDropdownState state,
                RuntimeBlackboardSchema schema,
                bool hasExpectedType,
                RuntimeBlackboardValueType expectedType,
                bool allowEmpty,
                Object[] targets,
                string propertyPath)
                : base(state)
            {
                _schema = schema;
                _hasExpectedType = hasExpectedType;
                _expectedType = expectedType;
                _allowEmpty = allowEmpty;
                _targets = targets;
                _propertyPath = propertyPath;
                minimumSize = new Vector2(320f, 280f);
            }

            protected override AdvancedDropdownItem BuildRoot()
            {
                var root = new AdvancedDropdownItem("Blackboard Keys");
                if (_allowEmpty)
                {
                    root.AddChild(new BlackboardKeyItem("None (Empty)", string.Empty));
                }

                if (_schema == null)
                {
                    return root;
                }

                var definitions = new List<RuntimeBlackboardKeyDefinition>(_schema.Count);
                for (int i = 0; i < _schema.Count; i++)
                {
                    RuntimeBlackboardKeyDefinition definition = _schema.GetEntry(i);
                    if (!_hasExpectedType || definition.ValueType == _expectedType)
                    {
                        definitions.Add(definition);
                    }
                }

                definitions.Sort(NameComparison);
                for (int i = 0; i < definitions.Count; i++)
                {
                    RuntimeBlackboardKeyDefinition definition = definitions[i];
                    root.AddChild(new BlackboardKeyItem(
                        $"{definition.Name}  [{definition.ValueType}]",
                        definition.Name));
                }

                return root;
            }

            protected override void ItemSelected(AdvancedDropdownItem item)
            {
                if (!(item is BlackboardKeyItem keyItem) || _targets == null)
                {
                    return;
                }

                Undo.RecordObjects(_targets, "Select Behavior Tree Blackboard Key");
                for (int i = 0; i < _targets.Length; i++)
                {
                    Object target = _targets[i];
                    if (target == null)
                    {
                        continue;
                    }

                    var serializedObject = new SerializedObject(target);
                    serializedObject.UpdateIfRequiredOrScript();
                    SerializedProperty keyProperty = serializedObject.FindProperty(_propertyPath);
                    if (keyProperty == null || keyProperty.propertyType != SerializedPropertyType.String)
                    {
                        continue;
                    }

                    keyProperty.stringValue = keyItem.Key;
                    serializedObject.ApplyModifiedProperties();
                }
            }

            private static int CompareByName(
                RuntimeBlackboardKeyDefinition left,
                RuntimeBlackboardKeyDefinition right)
            {
                return string.Compare(left.Name, right.Name, StringComparison.OrdinalIgnoreCase);
            }
        }

        private sealed class BlackboardKeyItem : AdvancedDropdownItem
        {
            internal BlackboardKeyItem(string displayName, string key)
                : base(displayName)
            {
                Key = key;
            }

            internal string Key { get; }
        }
    }
}
