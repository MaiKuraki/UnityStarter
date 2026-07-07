using System.Collections.Generic;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Compilation;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public class BehaviorTreeAuthoringCompilerTests
    {
        [Test]
        public void Compiler_RejectsRootWithoutChild()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            tree.Root = root;

            List<string> errors = BehaviorTreeCompiler.Validate(tree);

            Assert.That(errors, Has.Some.Contains("root child is null"));
        }

        [Test]
        public void Compiler_RejectsDecoratorWithoutChild()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var inverter = ScriptableObject.CreateInstance<InvertNode>();
            root.Child = inverter;
            tree.Root = root;

            List<string> errors = BehaviorTreeCompiler.Validate(tree);

            Assert.That(errors, Has.Some.Contains("decorator child is null"));
        }

        [Test]
        public void Compiler_RejectsDuplicateGuids()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var selector = ScriptableObject.CreateInstance<BlackBoardNode>();
            var left = ScriptableObject.CreateInstance<OnOffNode>();
            var duplicateGuid = "duplicate-guid";
            selector.GUID = "decorator";
            left.GUID = duplicateGuid;
            root.GUID = duplicateGuid;
            root.Child = selector;
            selector.Child = left;
            tree.Root = root;

            List<string> errors = BehaviorTreeCompiler.Validate(tree);

            Assert.That(errors, Has.Some.Contains("duplicate node GUID"));
        }

        [Test]
        public void Compiler_WrapsNullRuntimeNodeCreationFailure()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = ScriptableObject.CreateInstance<NullRuntimeNodeAuthoringNode>();
            tree.Root = root;

            BehaviorTreeCompileException exception = Assert.Throws<BehaviorTreeCompileException>(
                () => BehaviorTreeCompiler.Compile(tree));

            Assert.That(exception.InnerException, Is.Not.Null);
            Assert.That(exception.InnerException.Message, Does.Contain("returned null runtime node"));
        }

        [Test]
        public void Compiler_AllowsSubTreeAssetRootWithoutInlineChild()
        {
            var subTree = CreateOneNodeTree(true);
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var subTreeNode = ScriptableObject.CreateInstance<SubTreeNode>();
            SetSubTreeAsset(subTreeNode, subTree);
            root.Child = subTreeNode;
            tree.Root = root;

            RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(tree);

            Assert.That(runtimeTree, Is.Not.Null);
            Assert.That(runtimeTree.Tick(), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void RuntimeBehaviorTree_ConstructorAwakesRootOnce()
        {
            var child = new CountingNode();
            var root = new RuntimeRootNode
            {
                Child = child
            };

            _ = new RuntimeBehaviorTree(root, new RuntimeBlackboard(), new RuntimeBTContext());

            Assert.That(child.AwakeCount, Is.EqualTo(1));
        }

        private static Runtime.BehaviorTree CreateOneNodeTree(bool on)
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var condition = ScriptableObject.CreateInstance<OnOffNode>();
            SetOnOff(condition, on);
            root.Child = condition;
            tree.Root = root;
            return tree;
        }

        private static void SetSubTreeAsset(SubTreeNode node, Runtime.BehaviorTree subTree)
        {
            typeof(SubTreeNode)
                .GetField("_subTreeAsset", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(node, subTree);
        }

        private static void SetOnOff(OnOffNode node, bool value)
        {
            typeof(OnOffNode)
                .GetField("_on", BindingFlags.Instance | BindingFlags.NonPublic)
                ?.SetValue(node, value);
        }

        private sealed class CountingNode : RuntimeNode
        {
            public int AwakeCount { get; private set; }

            public override void OnAwake()
            {
                AwakeCount++;
            }

            protected override RuntimeState OnRun(RuntimeBlackboard blackboard)
            {
                return RuntimeState.Success;
            }
        }

        private sealed class NullRuntimeNodeAuthoringNode : BTNode
        {
            public override RuntimeNode CreateRuntimeNode()
            {
                return null;
            }
        }
    }
}
