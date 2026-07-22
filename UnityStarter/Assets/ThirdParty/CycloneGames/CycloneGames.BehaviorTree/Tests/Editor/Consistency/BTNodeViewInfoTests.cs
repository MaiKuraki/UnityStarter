using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using CycloneGames.BehaviorTree.Editor;
using CycloneGames.BehaviorTree.Runtime.Conditions.BlackBoards;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using NUnit.Framework;
using UnityEditor;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public class BTNodeViewInfoTests
    {
        [Test]
        public void EditorModeInfo_UsesAuthoringNodeConfiguration()
        {
            var nodes = new List<BTNode>();
            var subTreeAsset = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            subTreeAsset.name = "CombatSubTree";

            try
            {
                var debugLog = CreateNode<DebugLogNode>(nodes);
                ConfigureNode(debugLog, "_message", property => property.stringValue = "Target acquired");
                Assert.That(GetEditorModeInfo(debugLog), Is.EqualTo("\"Target acquired\""));

                var wait = CreateNode<WaitNode>(nodes);
                ConfigureNode(wait, "_duration", property => property.floatValue = 1.25f);
                Assert.That(GetEditorModeInfo(wait), Is.EqualTo($"Duration: {1.25f:F2}s"));

                var messagePass = CreateNode<MessagePassNode>(nodes);
                ConfigureNode(messagePass, "_key", property => property.stringValue = "Alert");
                ConfigureNode(messagePass, "_message", property => property.stringValue = "Enemy seen");
                Assert.That(GetEditorModeInfo(messagePass), Is.EqualTo("[Alert] = \"Enemy seen\""));

                var messageReceive = CreateNode<MessageReceiveNode>(nodes);
                ConfigureNode(messageReceive, "_key", property => property.stringValue = "Alert");
                ConfigureNode(messageReceive, "_message", property => property.stringValue = "Enemy seen");
                Assert.That(GetEditorModeInfo(messageReceive), Is.EqualTo("[Alert] == \"Enemy seen\""));

                var messageRemove = CreateNode<MessageRemoveNode>(nodes);
                ConfigureNode(messageRemove, "_key", property => property.stringValue = "Alert");
                Assert.That(GetEditorModeInfo(messageRemove), Is.EqualTo("Remove [Alert]"));

                var comparison = CreateNode<BBComparisonNode>(nodes);
                ConfigureNode(comparison, "_key", property => property.stringValue = "TargetDistance");
                ConfigureNode(comparison, "_operator", property => property.enumValueIndex = (int)BBComparisonOp.LessThan);
                ConfigureNode(comparison, "_valueType", property => property.enumValueIndex = (int)BBValueType.Float);
                Assert.That(GetEditorModeInfo(comparison), Is.EqualTo("[TargetDistance] LessThan (Float)"));

                var service = CreateNode<ServiceNode>(nodes);
                ConfigureNode(service, "_interval", property => property.floatValue = 0.75f);
                ConfigureNode(service, "_randomDeviation", property => property.floatValue = 0.1f);
                Assert.That(GetEditorModeInfo(service), Is.EqualTo($"Every {0.75f:F2}s ±{0.1f:F2}"));

                var retry = CreateNode<RetryNode>(nodes);
                ConfigureNode(retry, "_maxAttempts", property => property.intValue = 7);
                Assert.That(GetEditorModeInfo(retry), Is.EqualTo("Max Attempts: 7"));

                var timeout = CreateNode<TimeoutNode>(nodes);
                ConfigureNode(timeout, "_timeoutSeconds", property => property.floatValue = 3.5f);
                Assert.That(GetEditorModeInfo(timeout), Is.EqualTo($"Timeout: {3.5f:F1}s"));

                var delay = CreateNode<DelayNode>(nodes);
                ConfigureNode(delay, "_delaySeconds", property => property.floatValue = 0.5f);
                Assert.That(GetEditorModeInfo(delay), Is.EqualTo($"Delay: {0.5f:F1}s"));

                var subTree = CreateNode<SubTreeNode>(nodes);
                ConfigureNode(subTree, "_subTreeAsset", property => property.objectReferenceValue = subTreeAsset);
                Assert.That(GetEditorModeInfo(subTree), Is.EqualTo("Tree: CombatSubTree"));

                var switchNode = CreateNode<SwitchNode>(nodes);
                ConfigureNode(switchNode, "_variableKey", property => property.stringValue = "CombatPhase");
                Assert.That(GetEditorModeInfo(switchNode), Is.EqualTo("Key: CombatPhase"));

                var parallel = CreateNode<ParallelAllNode>(nodes);
                ConfigureNode(parallel, "_successThreshold", property => property.intValue = 2);
                Assert.That(GetEditorModeInfo(parallel), Is.EqualTo("Need 2 success"));

                var utility = CreateNode<UtilitySelectorNode>(nodes);
                ConfigureNode(utility, "_scoreKeys", property =>
                {
                    property.arraySize = 2;
                    property.GetArrayElementAtIndex(0).stringValue = "AttackScore";
                    property.GetArrayElementAtIndex(1).stringValue = "RetreatScore";
                });
                Assert.That(GetEditorModeInfo(utility), Is.EqualTo("Utility (2 keys)"));
            }
            finally
            {
                for (int i = nodes.Count - 1; i >= 0; i--)
                {
                    UnityEngine.Object.DestroyImmediate(nodes[i]);
                }

                UnityEngine.Object.DestroyImmediate(subTreeAsset);
            }
        }

        [Test]
        public void NodeView_DoesNotCacheSerializedFieldReflection()
        {
            FieldInfo[] fieldInfoCaches = typeof(BTNodeView)
                .GetFields(BindingFlags.Static | BindingFlags.NonPublic)
                .Where(field => field.FieldType == typeof(FieldInfo))
                .ToArray();

            Assert.That(fieldInfoCaches, Is.Empty);
        }

        [Test]
        public void NodeSpecificInfo_PreservesThrottledRefreshSemantics()
        {
            var node = ScriptableObject.CreateInstance<DebugLogNode>();
            try
            {
                ConfigureNode(node, "_message", property => property.stringValue = "Initial");
                var view = new BTNodeView(node);
                Assert.That(InvokeInfoMethod(view, "GetNodeSpecificInfo"), Is.EqualTo("\"Initial\""));

                FieldInfo lastUpdateTime = typeof(BTNodeView).GetField(
                    "_lastInfoUpdateTime",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(lastUpdateTime, Is.Not.Null);
                lastUpdateTime.SetValue(view, double.PositiveInfinity);

                ConfigureNode(node, "_message", property => property.stringValue = "Changed");
                Assert.That(InvokeInfoMethod(view, "GetNodeSpecificInfo"), Is.EqualTo("\"Initial\""));

                lastUpdateTime.SetValue(view, double.NegativeInfinity);
                Assert.That(InvokeInfoMethod(view, "GetNodeSpecificInfo"), Is.EqualTo("\"Changed\""));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(node);
            }
        }

        private static T CreateNode<T>(List<BTNode> nodes) where T : BTNode
        {
            var node = ScriptableObject.CreateInstance<T>();
            nodes.Add(node);
            return node;
        }

        private static void ConfigureNode(BTNode node, string propertyName, Action<SerializedProperty> configure)
        {
            var serializedObject = new SerializedObject(node);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Missing serialized property '{propertyName}' on {node.GetType().Name}.");
            configure(property);
            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static string GetEditorModeInfo(BTNode node)
        {
            var view = new BTNodeView(node);
            return InvokeInfoMethod(view, "GetEditorModeInfo");
        }

        private static string InvokeInfoMethod(BTNodeView view, string methodName)
        {
            MethodInfo method = typeof(BTNodeView).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            return (string)method.Invoke(view, null);
        }
    }
}
