using System.Collections.Generic;
using CycloneGames.BehaviorTree.Editor.Attributes;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Editor.CustomEditors.NodeEditors
{
    [CustomEditor(typeof(BBComparisonNode), true)]
    [CanEditMultipleObjects]
    internal sealed class BBComparisonNodeEditor : UnityEditor.Editor
    {
        private static readonly HashSet<string> ExplicitProperties = new HashSet<string>
        {
            "_key",
            "_operator",
            "_valueType",
            "_refInt",
            "_refFloat",
            "_refBool",
            "_refKey",
            "_floatEpsilon"
        };

        private SerializedProperty _key;
        private SerializedProperty _operator;
        private SerializedProperty _valueType;
        private SerializedProperty _refInt;
        private SerializedProperty _refFloat;
        private SerializedProperty _refBool;
        private SerializedProperty _refKey;
        private SerializedProperty _floatEpsilon;

        private void OnEnable()
        {
            _key = serializedObject.FindProperty("_key");
            _operator = serializedObject.FindProperty("_operator");
            _valueType = serializedObject.FindProperty("_valueType");
            _refInt = serializedObject.FindProperty("_refInt");
            _refFloat = serializedObject.FindProperty("_refFloat");
            _refBool = serializedObject.FindProperty("_refBool");
            _refKey = serializedObject.FindProperty("_refKey");
            _floatEpsilon = serializedObject.FindProperty("_floatEpsilon");
        }

        public override void OnInspectorGUI()
        {
            serializedObject.Update();

            using (new EditorGUI.DisabledScope(true))
            {
                EditorGUILayout.PropertyField(serializedObject.FindProperty("m_Script"));
            }

            EditorGUILayout.LabelField("Condition", EditorStyles.boldLabel);
            EditorGUILayout.PropertyField(_operator, new GUIContent("Operator"));

            bool mixedOperator = _operator.hasMultipleDifferentValues;
            BBComparisonOp comparisonOperator = (BBComparisonOp)_operator.intValue;
            bool existenceCheck = !mixedOperator &&
                                  (comparisonOperator == BBComparisonOp.IsSet ||
                                   comparisonOperator == BBComparisonOp.IsNotSet);

            if (existenceCheck)
            {
                BehaviorTreeBlackboardKeyGui.DrawLayout(
                    _key,
                    new GUIContent("Key"),
                    hasExpectedType: false,
                    expectedType: default,
                    allowEmpty: false);
            }
            else
            {
                EditorGUILayout.PropertyField(_valueType, new GUIContent("Value Type"));
                bool hasValidType = TryGetExpectedType(_valueType, out RuntimeBlackboardValueType expectedType);
                BehaviorTreeBlackboardKeyGui.DrawLayout(
                    _key,
                    new GUIContent("Key"),
                    hasValidType,
                    expectedType,
                    allowEmpty: false);

                EditorGUILayout.Space(2f);
                EditorGUILayout.LabelField("Reference", EditorStyles.boldLabel);
                BehaviorTreeBlackboardKeyGui.DrawLayout(
                    _refKey,
                    new GUIContent(
                        "Reference Key",
                        "Leave empty to compare against the constant value below."),
                    hasValidType,
                    expectedType,
                    allowEmpty: true);

                if (_refKey.hasMultipleDifferentValues || string.IsNullOrEmpty(_refKey.stringValue))
                {
                    DrawConstant(_valueType);
                }

                if (hasValidType &&
                    expectedType == RuntimeBlackboardValueType.Float &&
                    (mixedOperator ||
                     comparisonOperator == BBComparisonOp.Equal ||
                     comparisonOperator == BBComparisonOp.NotEqual))
                {
                    EditorGUILayout.PropertyField(
                        _floatEpsilon,
                        new GUIContent("Float Epsilon", "Tolerance used by float equality comparisons."));
                }

                if (!mixedOperator &&
                    hasValidType &&
                    !IsOperatorValidForType(comparisonOperator, expectedType))
                {
                    EditorGUILayout.HelpBox(
                        $"{expectedType} does not support {comparisonOperator}.",
                        MessageType.Error);
                }
            }

            BehaviorTreeInspectorUi.DrawRemainingProperties(serializedObject, ExplicitProperties);
            serializedObject.ApplyModifiedProperties();
        }

        private void DrawConstant(SerializedProperty valueTypeProperty)
        {
            if (valueTypeProperty.hasMultipleDifferentValues)
            {
                EditorGUILayout.PropertyField(_refInt, new GUIContent("Int Constant"));
                EditorGUILayout.PropertyField(_refFloat, new GUIContent("Float Constant"));
                EditorGUILayout.PropertyField(_refBool, new GUIContent("Bool Constant"));
                return;
            }

            switch ((BBValueType)valueTypeProperty.intValue)
            {
                case BBValueType.Int:
                    EditorGUILayout.PropertyField(_refInt, new GUIContent("Constant"));
                    break;
                case BBValueType.Float:
                    EditorGUILayout.PropertyField(_refFloat, new GUIContent("Constant"));
                    break;
                case BBValueType.Bool:
                    EditorGUILayout.PropertyField(_refBool, new GUIContent("Constant"));
                    break;
            }
        }

        private static bool TryGetExpectedType(
            SerializedProperty valueTypeProperty,
            out RuntimeBlackboardValueType expectedType)
        {
            expectedType = default;
            if (valueTypeProperty.hasMultipleDifferentValues)
            {
                return false;
            }

            switch ((BBValueType)valueTypeProperty.intValue)
            {
                case BBValueType.Int:
                    expectedType = RuntimeBlackboardValueType.Int;
                    return true;
                case BBValueType.Float:
                    expectedType = RuntimeBlackboardValueType.Float;
                    return true;
                case BBValueType.Bool:
                    expectedType = RuntimeBlackboardValueType.Bool;
                    return true;
                case BBValueType.Object:
                    expectedType = RuntimeBlackboardValueType.Object;
                    return true;
                default:
                    return false;
            }
        }

        private static bool IsOperatorValidForType(
            BBComparisonOp comparisonOperator,
            RuntimeBlackboardValueType valueType)
        {
            if (valueType == RuntimeBlackboardValueType.Bool)
            {
                return comparisonOperator == BBComparisonOp.Equal ||
                       comparisonOperator == BBComparisonOp.NotEqual;
            }

            if (valueType == RuntimeBlackboardValueType.Object)
            {
                return false;
            }

            return (uint)comparisonOperator <= (uint)BBComparisonOp.LessOrEqual;
        }
    }
}
