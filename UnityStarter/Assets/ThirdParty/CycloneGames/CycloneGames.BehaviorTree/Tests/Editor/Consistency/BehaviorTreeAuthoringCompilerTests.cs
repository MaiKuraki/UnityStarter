using System;
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
        public void Compiler_RejectsUnregisteredAuthoringNodeEmitter()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = ScriptableObject.CreateInstance<UnregisteredAuthoringNode>();
            tree.Root = root;

            BehaviorTreeCompileException exception = Assert.Throws<BehaviorTreeCompileException>(
                () => BehaviorTreeCompiler.Compile(tree));

            Assert.That(exception.InnerException, Is.Not.Null);
            Assert.That(exception.InnerException.Message, Does.Contain("No behavior tree runtime emitter is registered"));
        }

        [Test]
        public void Compiler_UsesCustomEmitterRegistry()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = ScriptableObject.CreateInstance<UnregisteredAuthoringNode>();
            tree.Root = root;

            var registry = BehaviorTreeNodeEmitterRegistry.CreateWithBuiltInFallback();
            registry.Register<UnregisteredAuthoringNode>((source, context) => context.WithGuid(source, new CountingNode()));

            RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(
                tree,
                null,
                new BehaviorTreeCompileOptions
                {
                    Emitters = registry,
                    UseCache = false
                });

            Assert.That(runtimeTree.Tick(), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void BuiltInEmitterRegistry_IsReadOnly()
        {
            InvalidOperationException exception = Assert.Throws<InvalidOperationException>(
                () => BehaviorTreeNodeEmitterRegistry.BuiltIn.Register<UnregisteredAuthoringNode>(
                    (source, context) => context.WithGuid(source, new CountingNode())));

            Assert.That(exception.Message, Does.Contain("read-only"));
        }

        [Test]
        public void CompileOptions_DefaultIsNotSharedMutableState()
        {
            BehaviorTreeCompileOptions first = BehaviorTreeCompileOptions.Default;
            BehaviorTreeCompileOptions second = BehaviorTreeCompileOptions.Default;

            first.UseCache = false;

            Assert.That(second.UseCache, Is.True);
            Assert.That(second, Is.Not.SameAs(first));
        }

        [Test]
        public void CompileArtifact_ErrorsAreReadOnly()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree, new BehaviorTreeCompileOptions
            {
                UseCache = false
            });

            Assert.That(artifact.IsValid, Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)artifact.Errors).Add("mutated"));
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
        public void Compiler_AnalyzeUsesCacheForUnchangedFingerprint()
        {
            BehaviorTreeCompileCache.Shared.Clear();
            var tree = CreateOneNodeTree(true);

            BehaviorTreeCompileArtifact first = BehaviorTreeCompiler.Analyze(tree);
            BehaviorTreeCompileArtifact second = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(first.IsValid, Is.True);
            Assert.That(second, Is.SameAs(first));
        }

        [Test]
        public void Compiler_FingerprintChangesWhenSerializedNodeConfigurationChanges()
        {
            BehaviorTreeCompileCache.Shared.Clear();
            var tree = CreateOneNodeTree(true);
            var condition = ((BTRootNode)tree.Root).Child as OnOffNode;

            BehaviorTreeCompileArtifact first = BehaviorTreeCompiler.Analyze(tree);
            SetOnOff(condition, false);
            BehaviorTreeCompileArtifact second = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(first.Fingerprint, Is.Not.EqualTo(second.Fingerprint));
            Assert.That(second.IsValid, Is.True);
        }

        [Test]
        public void Compiler_FingerprintIncludesSubTreeAssetContent()
        {
            BehaviorTreeCompileCache.Shared.Clear();
            var subTree = CreateOneNodeTree(true);
            var subTreeCondition = ((BTRootNode)subTree.Root).Child as OnOffNode;
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var subTreeNode = ScriptableObject.CreateInstance<SubTreeNode>();
            SetSubTreeAsset(subTreeNode, subTree);
            root.Child = subTreeNode;
            tree.Root = root;

            BehaviorTreeCompileArtifact first = BehaviorTreeCompiler.Analyze(tree);
            SetOnOff(subTreeCondition, false);
            BehaviorTreeCompileArtifact second = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(first.Fingerprint, Is.Not.EqualTo(second.Fingerprint));
            Assert.That(second.IsValid, Is.True);
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

        private sealed class UnregisteredAuthoringNode : BTNode
        {
        }
    }
}
