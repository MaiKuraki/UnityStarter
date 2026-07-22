using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.BehaviorTree.Runtime;
using CycloneGames.BehaviorTree.Runtime.Compilation;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions.BlackBoards;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Conditions;
using CycloneGames.BehaviorTree.Runtime.Conditions.BlackBoards;
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

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);
            BehaviorTreeCompileException exception = Assert.Throws<BehaviorTreeCompileException>(
                () => BehaviorTreeCompiler.Compile(tree));

            Assert.That(artifact.IsValid, Is.False);
            Assert.That(artifact.Errors, Has.Some.Contains("no exact runtime emitter"));
            Assert.That(exception.Message, Does.Contain("no exact runtime emitter"));
        }

        [Test]
        public void Compiler_RequiresExactEmitterForDerivedAuthoringType()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = ScriptableObject.CreateInstance<DerivedWaitNode>();
            tree.Root = root;

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(artifact.IsValid, Is.False);
            Assert.That(artifact.Errors, Has.Some.Contains("no exact runtime emitter"));
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
                    Emitters = registry
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

            first.MaxDepth = 17;

            Assert.That(second.MaxDepth, Is.EqualTo(BehaviorTreeCompileOptions.DefaultMaxDepth));
            Assert.That(second, Is.Not.SameAs(first));
        }

        [Test]
        public void Compiler_NullOptionsUsesDefaultLimitsAndEmitters()
        {
            Runtime.BehaviorTree tree = CreateOneNodeTree(true);

            RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(tree, null, null);

            Assert.That(runtimeTree.Tick(), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void CompileArtifact_ErrorsAreReadOnly()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(artifact.IsValid, Is.False);
            Assert.Throws<NotSupportedException>(() => ((IList<string>)artifact.Errors).Add("mutated"));
        }

        [Test]
        public void CompileArtifact_ConstructionCannotBypassCompilerValidation()
        {
            Assert.That(
                typeof(BehaviorTreeCompileArtifact).GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public),
                Is.Empty);
            Assert.That(
                typeof(BehaviorTreeEmitContext).GetConstructors(
                    BindingFlags.Instance | BindingFlags.Public),
                Is.Empty);
        }

        [Test]
        public void CompileArtifact_RevalidatesMutableSourceBeforeEmission()
        {
            Runtime.BehaviorTree tree = CreateOneNodeTree(true);
            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);
            var cycle = ScriptableObject.CreateInstance<BlackBoardNode>();
            ((BTRootNode)tree.Root).Child = cycle;
            cycle.Child = cycle;

            BehaviorTreeCompileException exception = Assert.Throws<BehaviorTreeCompileException>(
                () => artifact.EmitRuntimeRoot());

            Assert.That(exception.Message, Does.Contain("cycle detected"));
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
        public void Compiler_ExpandsSameSubTreeAssetForEachOccurrence()
        {
            Runtime.BehaviorTree subTree = CreateOneNodeTree(true);
            subTree.Root.GUID = "shared-root";
            ((BTRootNode)subTree.Root).Child.GUID = "shared-condition";

            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var selector = ScriptableObject.CreateInstance<SelectorNode>();
            var firstOccurrence = ScriptableObject.CreateInstance<SubTreeNode>();
            var secondOccurrence = ScriptableObject.CreateInstance<SubTreeNode>();
            root.GUID = "host-root";
            selector.GUID = "host-selector";
            firstOccurrence.GUID = "first-subtree";
            secondOccurrence.GUID = "second-subtree";
            SetSubTreeAsset(firstOccurrence, subTree);
            SetSubTreeAsset(secondOccurrence, subTree);
            selector.Children.Add(firstOccurrence);
            selector.Children.Add(secondOccurrence);
            root.Child = selector;
            tree.Root = root;

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);
            using RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(tree);

            Assert.That(artifact.IsValid, Is.True);
            Assert.That(artifact.NodeCount, Is.EqualTo(8));
            var runtimeRoot = (RuntimeRootNode)runtimeTree.Root;
            var runtimeSelector = (RuntimeSelector)runtimeRoot.Child;
            var firstRuntime = (RuntimeSubTreeNode)runtimeSelector.Children[0];
            var secondRuntime = (RuntimeSubTreeNode)runtimeSelector.Children[1];
            Assert.That(firstRuntime.Child, Is.Not.SameAs(secondRuntime.Child));
            Assert.That(firstRuntime.Child.GUID, Is.Not.EqualTo(secondRuntime.Child.GUID));
        }

        [Test]
        public void Compiler_RejectsRecursiveSubTreeAssetCycle()
        {
            var firstAsset = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var secondAsset = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var firstRoot = ScriptableObject.CreateInstance<BTRootNode>();
            var secondRoot = ScriptableObject.CreateInstance<BTRootNode>();
            var firstReference = ScriptableObject.CreateInstance<SubTreeNode>();
            var secondReference = ScriptableObject.CreateInstance<SubTreeNode>();
            SetSubTreeAsset(firstReference, secondAsset);
            SetSubTreeAsset(secondReference, firstAsset);
            firstRoot.Child = firstReference;
            secondRoot.Child = secondReference;
            firstAsset.Root = firstRoot;
            secondAsset.Root = secondRoot;

            List<string> errors = BehaviorTreeCompiler.Validate(firstAsset);

            Assert.That(errors, Has.Some.Contains("recursive subtree asset cycle"));
        }

        [Test]
        public void Compiler_AnalyzeReturnsFreshArtifactAndValidatedNodeCount()
        {
            var tree = CreateOneNodeTree(true);

            BehaviorTreeCompileArtifact first = BehaviorTreeCompiler.Analyze(tree);
            BehaviorTreeCompileArtifact second = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(first.IsValid, Is.True);
            Assert.That(first.NodeCount, Is.EqualTo(2));
            Assert.That(second.IsValid, Is.True);
            Assert.That(second.NodeCount, Is.EqualTo(2));
            Assert.That(second, Is.Not.SameAs(first));
        }

        [Test]
        public void Compiler_BuiltInEmitterUsesReadOnlyAuthoringConfiguration()
        {
            var tree = CreateOneNodeTree(false);
            var condition = (OnOffNode)((BTRootNode)tree.Root).Child;

            RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(tree);

            Assert.That(condition.IsOn, Is.False);
            Assert.That(typeof(OnOffNode).GetProperty(nameof(OnOffNode.IsOn))?.CanWrite, Is.False);
            Assert.That(runtimeTree.Tick(), Is.EqualTo(RuntimeState.Failure));
        }

        [Test]
        public void Compiler_RejectsCyclesDuringBoundedAnalysis()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var decorator = ScriptableObject.CreateInstance<BlackBoardNode>();
            root.Child = decorator;
            decorator.Child = decorator;
            tree.Root = root;

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(artifact.IsValid, Is.False);
            Assert.That(artifact.Errors, Has.Some.Contains("cycle detected"));
        }

        [Test]
        public void Compiler_RejectsSharedNodeOwnership()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var selector = ScriptableObject.CreateInstance<SelectorNode>();
            var shared = ScriptableObject.CreateInstance<OnOffNode>();
            selector.Children.Add(shared);
            selector.Children.Add(shared);
            root.Child = selector;
            tree.Root = root;

            List<string> errors = BehaviorTreeCompiler.Validate(tree);

            Assert.That(errors, Has.Some.Contains("more than one parent"));
        }

        [Test]
        public void Compiler_ReportsInvalidRandomChanceBeforeRuntimeEmission()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var random = ScriptableObject.CreateInstance<RandomChanceNode>();
            typeof(RandomChanceNode).GetField("_chance", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(random, 2f);
            typeof(RandomChanceNode).GetField("_outOf", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(random, 1f);
            root.Child = random;
            tree.Root = root;

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(tree);

            Assert.That(artifact.IsValid, Is.False);
            Assert.That(artifact.Errors, Has.Some.Contains("RandomChance"));
        }

        [Test]
        public void Compiler_ReportsParallelThresholdsThatCannotTerminate()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var parallel = ScriptableObject.CreateInstance<ParallelAllNode>();
            parallel.Children.Add(ScriptableObject.CreateInstance<OnOffNode>());
            parallel.Children.Add(ScriptableObject.CreateInstance<OnOffNode>());
            parallel.Children.Add(ScriptableObject.CreateInstance<OnOffNode>());
            typeof(ParallelAllNode).GetField("_successThreshold", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(parallel, 3);
            typeof(ParallelAllNode).GetField("_failureThreshold", BindingFlags.Instance | BindingFlags.NonPublic)?.SetValue(parallel, 3);
            root.Child = parallel;
            tree.Root = root;

            List<string> errors = BehaviorTreeCompiler.Validate(tree);

            Assert.That(errors, Has.Some.Contains("without a terminal result"));
        }

        [Test]
        public void Compiler_RejectsMalformedTemporalConfigurationBeforeEmission()
        {
            var wait = ScriptableObject.CreateInstance<WaitNode>();
            wait.Duration = float.NaN;
            Assert.That(ValidateSingleNode(wait), Has.Some.Contains("Wait duration"));

            var waitRange = ScriptableObject.CreateInstance<WaitNode>();
            SetPrivateField(waitRange, "_useRandomBetweenTwoConstants", true);
            SetPrivateField(waitRange, "_range", new Vector2(2f, 1f));
            Assert.That(ValidateSingleNode(waitRange), Has.Some.Contains("Wait range"));

            var waitSuccess = ScriptableObject.CreateInstance<WaitSuccessNode>();
            SetPrivateField(waitSuccess, "_waitTime", -1f);
            Assert.That(ValidateSingleNode(waitSuccess), Has.Some.Contains("WaitSuccess wait time"));

            var delay = ScriptableObject.CreateInstance<DelayNode>();
            SetPrivateField(delay, "_delaySeconds", float.PositiveInfinity);
            Assert.That(ValidateSingleNode(delay), Has.Some.Contains("Delay seconds"));

            var timeout = ScriptableObject.CreateInstance<TimeoutNode>();
            SetPrivateField(timeout, "_timeoutSeconds", -0.01f);
            Assert.That(ValidateSingleNode(timeout), Has.Some.Contains("Timeout seconds"));

            var cooldown = ScriptableObject.CreateInstance<CoolDownNode>();
            SetPrivateField(cooldown, "_coolDown", float.NaN);
            Assert.That(ValidateSingleNode(cooldown), Has.Some.Contains("Cooldown"));

            var service = ScriptableObject.CreateInstance<ServiceNode>();
            SetPrivateField(service, "_interval", float.MaxValue);
            SetPrivateField(service, "_randomDeviation", float.MaxValue);
            Assert.That(ValidateSingleNode(service), Has.Some.Contains("finite sampling range"));
        }

        [Test]
        public void Compiler_RejectsMalformedRepeatRetryAndEnumConfiguration()
        {
            var retry = ScriptableObject.CreateInstance<RetryNode>();
            SetPrivateField(retry, "_maxAttempts", 0);
            Assert.That(ValidateSingleNode(retry), Has.Some.Contains("MaxAttempts"));

            var repeat = ScriptableObject.CreateInstance<RepeatNode>();
            SetPrivateField(repeat, "_repeatForever", false);
            SetPrivateField(repeat, "_useRandomRepeatCount", true);
            SetPrivateField(repeat, "_randomRepeatCountRange", new Vector2(1.5f, 3f));
            Assert.That(ValidateSingleNode(repeat), Has.Some.Contains("whole counts"));

            var parallel = ScriptableObject.CreateInstance<ParallelNode>();
            SetPrivateField(parallel, "_mode", 99);
            Assert.That(ValidateSingleNode(parallel), Has.Some.Contains("Parallel mode"));

            var selector = ScriptableObject.CreateInstance<SelectorNode>();
            selector.AbortType = (ConditionalAbortType)99;
            Assert.That(ValidateSingleNode(selector), Has.Some.Contains("conditional abort"));
        }

        [Test]
        public void Compiler_RejectsMalformedBlackboardAuthoringConfiguration()
        {
            var remove = ScriptableObject.CreateInstance<MessageRemoveNode>();
            Assert.That(ValidateSingleNode(remove), Has.Some.Contains("MessageRemove requires"));

            var receive = ScriptableObject.CreateInstance<MessageReceiveNode>();
            SetPrivateField(receive, "_key", " ");
            Assert.That(ValidateSingleNode(receive), Has.Some.Contains("MessageReceive requires"));

            var switchNode = ScriptableObject.CreateInstance<SwitchNode>();
            Assert.That(ValidateSingleNode(switchNode), Has.Some.Contains("Switch requires"));

            var utility = ScriptableObject.CreateInstance<UtilitySelectorNode>();
            Assert.That(ValidateSingleNode(utility), Has.Some.Contains("score-key count"));

            var comparison = ScriptableObject.CreateInstance<BBComparisonNode>();
            SetPrivateField(comparison, "_key", "Target");
            SetPrivateField(comparison, "_valueType", BBValueType.Object);
            SetPrivateField(comparison, "_operator", BBComparisonOp.Equal);
            Assert.That(ValidateSingleNode(comparison), Has.Some.Contains("object values only support"));
        }

        [Test]
        public void Compiler_AlwaysAppliesDepthLimit()
        {
            var tree = CreateOneNodeTree(true);
            var root = (BTRootNode)tree.Root;
            var outer = ScriptableObject.CreateInstance<BlackBoardNode>();
            outer.Child = root.Child;
            root.Child = outer;

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(
                tree,
                new BehaviorTreeCompileOptions
                {
                    MaxDepth = 2
                });

            Assert.That(artifact.IsValid, Is.False);
            Assert.That(artifact.Errors, Has.Some.Contains("MaxDepth"));
        }

        [Test]
        public void Compiler_RejectsLimitsAboveHardSafetyCeilings()
        {
            Runtime.BehaviorTree tree = CreateOneNodeTree(true);

            BehaviorTreeCompileArtifact nodeArtifact = BehaviorTreeCompiler.Analyze(
                tree,
                new BehaviorTreeCompileOptions
                {
                    MaxNodeCount = RuntimeBehaviorTreeLimits.HARD_MAX_NODE_COUNT + 1
                });
            BehaviorTreeCompileArtifact depthArtifact = BehaviorTreeCompiler.Analyze(
                tree,
                new BehaviorTreeCompileOptions
                {
                    MaxDepth = RuntimeBehaviorTreeLimits.HARD_MAX_DEPTH + 1
                });

            Assert.That(nodeArtifact.Errors, Has.Some.Contains("hard safety limit"));
            Assert.That(depthArtifact.Errors, Has.Some.Contains("hard safety limit"));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RuntimeBehaviorTreeLimits(
                    RuntimeBehaviorTreeLimits.HARD_MAX_NODE_COUNT + 1,
                    RuntimeBehaviorTreeLimits.DEFAULT_MAX_DEPTH));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new RuntimeBehaviorTreeLimits(
                    RuntimeBehaviorTreeLimits.DEFAULT_MAX_NODE_COUNT,
                    RuntimeBehaviorTreeLimits.HARD_MAX_DEPTH + 1));
        }

        [Test]
        public void Compiler_RejectsRuntimeGuidCollisionAcrossSubTreeOccurrences()
        {
            Runtime.BehaviorTree nested = CreateOneNodeTree(true);
            nested.Root.GUID = "nested-root";

            var host = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var hostRoot = ScriptableObject.CreateInstance<BTRootNode>();
            var subTreeNode = ScriptableObject.CreateInstance<SubTreeNode>();
            hostRoot.GUID = "bt-subtree-1/nested-root";
            hostRoot.Child = subTreeNode;
            SetSubTreeAsset(subTreeNode, nested);
            host.Root = hostRoot;

            BehaviorTreeCompileArtifact artifact = BehaviorTreeCompiler.Analyze(host);
            BehaviorTreeCompileException exception = Assert.Throws<BehaviorTreeCompileException>(
                () => BehaviorTreeCompiler.Compile(host));

            Assert.That(artifact.IsValid, Is.False);
            Assert.That(artifact.Errors, Has.Some.Contains("collides with another occurrence"));
            Assert.That(exception.Message, Does.Contain("collides with another occurrence"));
        }

        [Test]
        public void StableBlackboardHash_DistinguishesFullUtf16CodeUnits()
        {
            Assert.That(BTHash.FNV1A("A\u0100"), Is.Not.EqualTo(BTHash.FNV1A("A\0")));
            Assert.That(BTHash.FNV1ACaseInsensitive("Health"), Is.EqualTo(BTHash.FNV1ACaseInsensitive("HEALTH")));
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

        private static List<string> ValidateSingleNode(BTNode node)
        {
            if (node is DecoratorNode decorator && decorator.Child == null)
            {
                decorator.Child = ScriptableObject.CreateInstance<OnOffNode>();
            }

            if (node is CompositeNode composite && composite.Children.Count == 0)
            {
                composite.Children.Add(ScriptableObject.CreateInstance<OnOffNode>());
            }

            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = node;
            tree.Root = root;
            return BehaviorTreeCompiler.Validate(tree);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(
                fieldName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(field, Is.Not.Null, $"Missing serialized field {fieldName}.");
            object fieldValue = field.FieldType.IsEnum && value is int integerValue
                ? Enum.ToObject(field.FieldType, integerValue)
                : value;
            field.SetValue(target, fieldValue);
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

        private sealed class DerivedWaitNode : WaitNode
        {
        }
    }
}
