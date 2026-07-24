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
using UnityEditor;
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
        public void BlackboardSchema_LegacyOpenAndStrictEmptyKeepDistinctWriteContracts()
        {
            Runtime.BehaviorTree legacyTree = CreateOneNodeTree(true);

            Assert.That(
                legacyTree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema legacySchema, out string legacyError),
                Is.True);
            Assert.That(legacyError, Is.Null);
            Assert.That(legacySchema, Is.Null);

            using RuntimeBehaviorTree legacyRuntime = BehaviorTreeCompiler.Compile(legacyTree);
            Assert.DoesNotThrow(() => legacyRuntime.Blackboard.SetInt("Undeclared", 17));
            Assert.That(legacyRuntime.Blackboard.GetInt("Undeclared"), Is.EqualTo(17));

            Runtime.BehaviorTree strictTree = CreateOneNodeTree(true);
            ConfigureBlackboardSchema(strictTree, contractVersion: 3);

            Assert.That(
                strictTree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema strictSchema, out string strictError),
                Is.True);
            Assert.That(strictError, Is.Null);
            Assert.That(strictSchema, Is.Not.Null);
            Assert.That(strictSchema.Count, Is.Zero);
            Assert.That(strictSchema.ContractVersion, Is.EqualTo(3));

            using RuntimeBehaviorTree strictRuntime = BehaviorTreeCompiler.Compile(strictTree);
            Assert.Throws<KeyNotFoundException>(() => strictRuntime.Blackboard.SetInt("Undeclared", 17));
        }

        [Test]
        public void BlackboardSchema_ConvertsAllValueTypesDefaultsSyncFlagsAndContractVersion()
        {
            Runtime.BehaviorTree tree = CreateOneNodeTree(true);
            ConfigureBlackboardSchema(
                tree,
                contractVersion: 7,
                new AuthoringKeySpec(
                    "IntKey",
                    RuntimeBlackboardValueType.Int,
                    RuntimeBlackboardSyncFlags.Snapshot,
                    hasDefaultValue: true,
                    defaultValue: 42),
                new AuthoringKeySpec(
                    "FloatKey",
                    RuntimeBlackboardValueType.Float,
                    RuntimeBlackboardSyncFlags.Delta,
                    hasDefaultValue: true,
                    defaultValue: 1.25f),
                new AuthoringKeySpec(
                    "BoolKey",
                    RuntimeBlackboardValueType.Bool,
                    RuntimeBlackboardSyncFlags.Networked,
                    hasDefaultValue: true,
                    defaultValue: true),
                new AuthoringKeySpec(
                    "VectorKey",
                    RuntimeBlackboardValueType.Vector3,
                    RuntimeBlackboardSyncFlags.LocalOnly,
                    hasDefaultValue: true,
                    defaultValue: new Vector3(1f, 2f, 3f)),
                new AuthoringKeySpec(
                    "ObjectKey",
                    RuntimeBlackboardValueType.Object,
                    RuntimeBlackboardSyncFlags.LocalOnly),
                new AuthoringKeySpec(
                    "LongKey",
                    RuntimeBlackboardValueType.Long,
                    RuntimeBlackboardSyncFlags.Snapshot,
                    hasDefaultValue: true,
                    defaultValue: 1234567890123L),
                new AuthoringKeySpec(
                    "Long2Key",
                    RuntimeBlackboardValueType.Long2,
                    RuntimeBlackboardSyncFlags.Delta,
                    hasDefaultValue: true,
                    defaultValue: new RuntimeBlackboardLong2(11L, 22L)),
                new AuthoringKeySpec(
                    "Long3Key",
                    RuntimeBlackboardValueType.Long3,
                    RuntimeBlackboardSyncFlags.Networked,
                    hasDefaultValue: true,
                    defaultValue: new RuntimeBlackboardLong3(31L, 32L, 33L)));

            Assert.That(
                tree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema schema, out string error),
                Is.True,
                error);
            Assert.That(schema.ContractVersion, Is.EqualTo(7));
            Assert.That(schema.Count, Is.EqualTo(8));

            RuntimeBlackboardKeyDefinition intDefinition = GetDefinition(schema, "IntKey");
            RuntimeBlackboardKeyDefinition floatDefinition = GetDefinition(schema, "FloatKey");
            RuntimeBlackboardKeyDefinition boolDefinition = GetDefinition(schema, "BoolKey");
            RuntimeBlackboardKeyDefinition vectorDefinition = GetDefinition(schema, "VectorKey");
            RuntimeBlackboardKeyDefinition objectDefinition = GetDefinition(schema, "ObjectKey");
            RuntimeBlackboardKeyDefinition longDefinition = GetDefinition(schema, "LongKey");
            RuntimeBlackboardKeyDefinition long2Definition = GetDefinition(schema, "Long2Key");
            RuntimeBlackboardKeyDefinition long3Definition = GetDefinition(schema, "Long3Key");

            AssertDefinition(intDefinition, RuntimeBlackboardValueType.Int, RuntimeBlackboardSyncFlags.Snapshot, true);
            AssertDefinition(floatDefinition, RuntimeBlackboardValueType.Float, RuntimeBlackboardSyncFlags.Delta, true);
            AssertDefinition(boolDefinition, RuntimeBlackboardValueType.Bool, RuntimeBlackboardSyncFlags.Networked, true);
            AssertDefinition(vectorDefinition, RuntimeBlackboardValueType.Vector3, RuntimeBlackboardSyncFlags.LocalOnly, true);
            AssertDefinition(objectDefinition, RuntimeBlackboardValueType.Object, RuntimeBlackboardSyncFlags.LocalOnly, false);
            AssertDefinition(longDefinition, RuntimeBlackboardValueType.Long, RuntimeBlackboardSyncFlags.Snapshot, true);
            AssertDefinition(long2Definition, RuntimeBlackboardValueType.Long2, RuntimeBlackboardSyncFlags.Delta, true);
            AssertDefinition(long3Definition, RuntimeBlackboardValueType.Long3, RuntimeBlackboardSyncFlags.Networked, true);

            Assert.That(intDefinition.DefaultValue.IntValue, Is.EqualTo(42));
            Assert.That(floatDefinition.DefaultValue.FloatValue, Is.EqualTo(1.25f));
            Assert.That(boolDefinition.DefaultValue.BoolValue, Is.True);
            Assert.That(vectorDefinition.DefaultValue.Vector3Value, Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(longDefinition.DefaultValue.LongValue, Is.EqualTo(1234567890123L));
            Assert.That(long2Definition.DefaultValue.Long2Value, Is.EqualTo(new RuntimeBlackboardLong2(11L, 22L)));
            Assert.That(long3Definition.DefaultValue.Long3Value, Is.EqualTo(new RuntimeBlackboardLong3(31L, 32L, 33L)));

            using RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(tree);
            Assert.That(runtimeTree.Blackboard.Schema.ContractVersion, Is.EqualTo(7));
            Assert.That(runtimeTree.Blackboard.GetInt("IntKey"), Is.EqualTo(42));
            Assert.That(runtimeTree.Blackboard.GetFloat("FloatKey"), Is.EqualTo(1.25f));
            Assert.That(runtimeTree.Blackboard.GetBool("BoolKey"), Is.True);
            Assert.That(runtimeTree.Blackboard.GetVector3("VectorKey"), Is.EqualTo(new Vector3(1f, 2f, 3f)));
            Assert.That(runtimeTree.Blackboard.GetLong("LongKey"), Is.EqualTo(1234567890123L));
            Assert.That(runtimeTree.Blackboard.GetLong2("Long2Key"), Is.EqualTo(new RuntimeBlackboardLong2(11L, 22L)));
            Assert.That(runtimeTree.Blackboard.GetLong3("Long3Key"), Is.EqualTo(new RuntimeBlackboardLong3(31L, 32L, 33L)));
            Assert.That(runtimeTree.Blackboard.HasKey("ObjectKey"), Is.False);
            var marker = new object();
            runtimeTree.Blackboard.SetObject("ObjectKey", marker);
            Assert.That(runtimeTree.Blackboard.GetObject<object>("ObjectKey"), Is.SameAs(marker));
        }

        [Test]
        public void BlackboardSchemaCache_ReusesInstanceUntilOnValidateInvalidatesIt()
        {
            Runtime.BehaviorTree tree = CreateOneNodeTree(true);
            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec("Original", RuntimeBlackboardValueType.Int));

            Assert.That(tree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema first, out string firstError), Is.True, firstError);
            Assert.That(tree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema second, out string secondError), Is.True, secondError);
            Assert.That(second, Is.SameAs(first));

            FieldInfo keysField = typeof(Runtime.BehaviorTree).GetField(
                "_blackboardKeys",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(keysField, Is.Not.Null);
            var keys = (System.Collections.IList)keysField.GetValue(tree);
            SetPrivateField(keys[0], "_name", "Renamed");

            Assert.That(tree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema stillCached, out string cachedError), Is.True, cachedError);
            Assert.That(stillCached, Is.SameAs(first));
            Assert.That(stillCached.IsKnownKey(RuntimeBlackboard.DefaultStringHashFunc("Original")), Is.True);

            tree.OnValidate();

            Assert.That(tree.TryGetRuntimeBlackboardSchema(out RuntimeBlackboardSchema rebuilt, out string rebuiltError), Is.True, rebuiltError);
            Assert.That(rebuilt, Is.Not.SameAs(first));
            Assert.That(rebuilt.IsKnownKey(RuntimeBlackboard.DefaultStringHashFunc("Original")), Is.False);
            Assert.That(rebuilt.IsKnownKey(RuntimeBlackboard.DefaultStringHashFunc("Renamed")), Is.True);
        }

        [Test]
        public void BlackboardSchema_RejectsMalformedAuthoringContracts()
        {
            Runtime.BehaviorTree tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();

            ConfigureBlackboardSchema(tree, contractVersion: 0);
            AssertSchemaError(tree, "contract version");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec(" Edge", RuntimeBlackboardValueType.Int));
            AssertSchemaError(tree, "start or end with whitespace");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec(new string('K', 257), RuntimeBlackboardValueType.Int));
            AssertSchemaError(tree, "hard limit is 256");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec("Duplicate", RuntimeBlackboardValueType.Int),
                new AuthoringKeySpec("Duplicate", RuntimeBlackboardValueType.Int));
            AssertSchemaError(tree, "duplicate key");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec("InvalidType", (RuntimeBlackboardValueType)99));
            AssertSchemaError(tree, "unsupported value type");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec(
                    "InvalidFlags",
                    RuntimeBlackboardValueType.Int,
                    (RuntimeBlackboardSyncFlags)0x80));
            AssertSchemaError(tree, "unsupported sync flags");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec(
                    "RemoteObject",
                    RuntimeBlackboardValueType.Object,
                    RuntimeBlackboardSyncFlags.Snapshot));
            AssertSchemaError(tree, "must be LocalOnly");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec(
                    "ObjectDefault",
                    RuntimeBlackboardValueType.Object,
                    hasDefaultValue: true));
            AssertSchemaError(tree, "cannot own an authoring default");

            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec(
                    "NotFinite",
                    RuntimeBlackboardValueType.Float,
                    hasDefaultValue: true,
                    defaultValue: float.NaN));
            AssertSchemaError(tree, "must be finite");

            var serializedTree = new SerializedObject(tree);
            serializedTree.FindProperty("_blackboardSchemaFormatVersion").intValue = 99;
            serializedTree.ApplyModifiedPropertiesWithoutUndo();
            tree.OnValidate();
            AssertSchemaError(tree, "unsupported");
        }

        [Test]
        public void BlackboardSchema_AssetRoundTripAndUndoPreserveTheContract()
        {
            string assetPath = $"Assets/__BehaviorTreeSchema_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                Undo.RegisterCompleteObjectUndo(tree, "Configure Behavior Tree Blackboard Schema");
                ConfigureBlackboardSchema(
                    tree,
                    contractVersion: 4,
                    new AuthoringKeySpec(
                        "Persisted",
                        RuntimeBlackboardValueType.Float,
                        RuntimeBlackboardSyncFlags.Delta,
                        hasDefaultValue: true,
                        defaultValue: 2.5f));
                EditorUtility.SetDirty(tree);
                Undo.FlushUndoRecordObjects();

                Assert.That(tree.BlackboardSchemaEnabled, Is.True);
                Undo.PerformUndo();
                tree.OnValidate();
                Assert.That(tree.BlackboardSchemaEnabled, Is.False);

                Undo.PerformRedo();
                tree.OnValidate();
                Assert.That(tree.BlackboardSchemaEnabled, Is.True);

                AssetDatabase.SaveAssetIfDirty(tree);
                AssetDatabase.ImportAsset(
                    assetPath,
                    ImportAssetOptions.ForceSynchronousImport | ImportAssetOptions.ForceUpdate);
                Runtime.BehaviorTree reloaded =
                    AssetDatabase.LoadAssetAtPath<Runtime.BehaviorTree>(assetPath);
                Assert.That(reloaded, Is.Not.Null);
                Assert.That(
                    reloaded.TryGetRuntimeBlackboardSchema(
                        out RuntimeBlackboardSchema schema,
                        out string error),
                    Is.True,
                    error);
                Assert.That(schema.ContractVersion, Is.EqualTo(4));
                RuntimeBlackboardKeyDefinition definition = GetDefinition(schema, "Persisted");
                AssertDefinition(
                    definition,
                    RuntimeBlackboardValueType.Float,
                    RuntimeBlackboardSyncFlags.Delta,
                    expectedDefault: true);
                Assert.That(definition.DefaultValue.FloatValue, Is.EqualTo(2.5f));
            }
            finally
            {
                Undo.ClearAll();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void Compiler_StrictSchemaRejectsUnknownAndWrongTypeBuiltInKeys()
        {
            var messagePass = ScriptableObject.CreateInstance<MessagePassNode>();
            SetPrivateField(messagePass, "_key", "MissingObject");
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = messagePass;
            tree.Root = root;
            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec("KnownInt", RuntimeBlackboardValueType.Int));

            List<string> unknownErrors = BehaviorTreeCompiler.Validate(tree);
            Assert.That(unknownErrors, Has.Some.Contains("is not declared by the active strict schema"));

            SetPrivateField(messagePass, "_key", "KnownInt");
            List<string> wrongTypeErrors = BehaviorTreeCompiler.Validate(tree);
            Assert.That(wrongTypeErrors, Has.Some.Contains("requires Object, but the schema declares Int"));
        }

        [Test]
        public void Compiler_BBExistenceCheckAcceptsAnyDeclaredValueType()
        {
            var comparison = ScriptableObject.CreateInstance<BBComparisonNode>();
            comparison.Child = ScriptableObject.CreateInstance<OnOffNode>();
            SetOnOff((OnOffNode)comparison.Child, true);
            SetPrivateField(comparison, "_key", "Health");
            SetPrivateField(comparison, "_valueType", BBValueType.Object);
            SetPrivateField(comparison, "_operator", BBComparisonOp.IsSet);
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            root.Child = comparison;
            tree.Root = root;
            ConfigureBlackboardSchema(
                tree,
                contractVersion: 1,
                new AuthoringKeySpec("Health", RuntimeBlackboardValueType.Int));

            Assert.That(BehaviorTreeCompiler.Validate(tree), Is.Empty);

            using RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(tree);
            Assert.That(runtimeTree.Tick(), Is.EqualTo(RuntimeState.Failure));
            runtimeTree.Blackboard.SetInt("Health", 100);
            runtimeTree.Play();
            Assert.That(runtimeTree.Tick(), Is.EqualTo(RuntimeState.Success));
        }

        [Test]
        public void Compiler_StrictSubTreeRequiresAnExactRootSchemaSubset()
        {
            Runtime.BehaviorTree subTree = CreateOneNodeTree(true);
            ConfigureBlackboardSchema(
                subTree,
                contractVersion: 2,
                new AuthoringKeySpec(
                    "Shared",
                    RuntimeBlackboardValueType.Int,
                    RuntimeBlackboardSyncFlags.Snapshot,
                    hasDefaultValue: true,
                    defaultValue: 4));

            var host = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var hostRoot = ScriptableObject.CreateInstance<BTRootNode>();
            var subTreeNode = ScriptableObject.CreateInstance<SubTreeNode>();
            SetSubTreeAsset(subTreeNode, subTree);
            hostRoot.Child = subTreeNode;
            host.Root = hostRoot;
            ConfigureBlackboardSchema(
                host,
                contractVersion: 9,
                new AuthoringKeySpec(
                    "Shared",
                    RuntimeBlackboardValueType.Int,
                    RuntimeBlackboardSyncFlags.Snapshot,
                    hasDefaultValue: true,
                    defaultValue: 4),
                new AuthoringKeySpec("HostOnly", RuntimeBlackboardValueType.Bool));

            Assert.That(BehaviorTreeCompiler.Validate(host), Is.Empty);
            using RuntimeBehaviorTree runtimeTree = BehaviorTreeCompiler.Compile(host);
            Assert.That(runtimeTree.Blackboard.GetInt("Shared"), Is.EqualTo(4));
            Assert.That(runtimeTree.Blackboard.Schema.ContractVersion, Is.EqualTo(9));

            ConfigureBlackboardSchema(
                subTree,
                contractVersion: 2,
                new AuthoringKeySpec(
                    "Shared",
                    RuntimeBlackboardValueType.Int,
                    RuntimeBlackboardSyncFlags.Delta,
                    hasDefaultValue: true,
                    defaultValue: 4));

            List<string> incompatibleErrors = BehaviorTreeCompiler.Validate(host);
            Assert.That(incompatibleErrors, Has.Some.Contains("does not exactly match the root definition"));
        }

        [Test]
        public void Compiler_RejectsStrictSubTreeUnderLegacyOpenRoot()
        {
            Runtime.BehaviorTree subTree = CreateOneNodeTree(true);
            ConfigureBlackboardSchema(
                subTree,
                contractVersion: 1,
                new AuthoringKeySpec("Shared", RuntimeBlackboardValueType.Int));

            var host = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var hostRoot = ScriptableObject.CreateInstance<BTRootNode>();
            var subTreeNode = ScriptableObject.CreateInstance<SubTreeNode>();
            SetSubTreeAsset(subTreeNode, subTree);
            hostRoot.Child = subTreeNode;
            host.Root = hostRoot;

            List<string> errors = BehaviorTreeCompiler.Validate(host);

            Assert.That(errors, Has.Some.Contains("cannot be embedded in a legacy-open root"));
        }

        [Test]
        public void Compiler_StrictSubTreeCannotBorrowAnUndeclaredRootOnlyKey()
        {
            var messageRemove = ScriptableObject.CreateInstance<MessageRemoveNode>();
            SetPrivateField(messageRemove, "_key", "HostOnly");
            var subTree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var subTreeRoot = ScriptableObject.CreateInstance<BTRootNode>();
            subTreeRoot.Child = messageRemove;
            subTree.Root = subTreeRoot;
            ConfigureBlackboardSchema(
                subTree,
                contractVersion: 1,
                new AuthoringKeySpec("Shared", RuntimeBlackboardValueType.Int));

            List<string> standaloneErrors = BehaviorTreeCompiler.Validate(subTree);
            Assert.That(standaloneErrors, Has.Some.Contains("HostOnly"));

            var host = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var hostRoot = ScriptableObject.CreateInstance<BTRootNode>();
            var subTreeNode = ScriptableObject.CreateInstance<SubTreeNode>();
            SetSubTreeAsset(subTreeNode, subTree);
            hostRoot.Child = subTreeNode;
            host.Root = hostRoot;
            ConfigureBlackboardSchema(
                host,
                contractVersion: 1,
                new AuthoringKeySpec("Shared", RuntimeBlackboardValueType.Int),
                new AuthoringKeySpec("HostOnly", RuntimeBlackboardValueType.Int));

            List<string> embeddedErrors = BehaviorTreeCompiler.Validate(host);
            Assert.That(embeddedErrors, Has.Some.Contains("HostOnly"));
            Assert.That(embeddedErrors, Has.Some.Contains("is not declared by the active strict schema"));
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

        private static void ConfigureBlackboardSchema(
            Runtime.BehaviorTree tree,
            int contractVersion,
            params AuthoringKeySpec[] keys)
        {
            var serializedTree = new SerializedObject(tree);
            serializedTree.Update();
            serializedTree.FindProperty("_blackboardSchemaEnabled").boolValue = true;
            serializedTree.FindProperty("_blackboardSchemaFormatVersion").intValue =
                Runtime.BehaviorTree.CurrentBlackboardSchemaFormatVersion;
            serializedTree.FindProperty("_blackboardContractVersion").intValue = contractVersion;
            SerializedProperty keyArray = serializedTree.FindProperty("_blackboardKeys");
            keyArray.arraySize = keys.Length;

            for (int i = 0; i < keys.Length; i++)
            {
                AuthoringKeySpec spec = keys[i];
                SerializedProperty element = keyArray.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("_name").stringValue = spec.Name;
                element.FindPropertyRelative("_valueType").intValue = (int)spec.ValueType;
                element.FindPropertyRelative("_syncFlags").intValue = (int)spec.SyncFlags;
                element.FindPropertyRelative("_hasDefaultValue").boolValue = spec.HasDefaultValue;

                if (!spec.HasDefaultValue)
                {
                    continue;
                }

                switch (spec.ValueType)
                {
                    case RuntimeBlackboardValueType.Int:
                        element.FindPropertyRelative("_intDefaultValue").intValue = (int)spec.DefaultValue;
                        break;
                    case RuntimeBlackboardValueType.Float:
                        element.FindPropertyRelative("_floatDefaultValue").floatValue = (float)spec.DefaultValue;
                        break;
                    case RuntimeBlackboardValueType.Bool:
                        element.FindPropertyRelative("_boolDefaultValue").boolValue = (bool)spec.DefaultValue;
                        break;
                    case RuntimeBlackboardValueType.Vector3:
                        element.FindPropertyRelative("_vector3DefaultValue").vector3Value = (Vector3)spec.DefaultValue;
                        break;
                    case RuntimeBlackboardValueType.Long:
                        element.FindPropertyRelative("_longDefaultValue").longValue = (long)spec.DefaultValue;
                        break;
                    case RuntimeBlackboardValueType.Long2:
                        RuntimeBlackboardLong2 long2 = (RuntimeBlackboardLong2)spec.DefaultValue;
                        element.FindPropertyRelative("_long2X").longValue = long2.X;
                        element.FindPropertyRelative("_long2Y").longValue = long2.Y;
                        break;
                    case RuntimeBlackboardValueType.Long3:
                        RuntimeBlackboardLong3 long3 = (RuntimeBlackboardLong3)spec.DefaultValue;
                        element.FindPropertyRelative("_long3X").longValue = long3.X;
                        element.FindPropertyRelative("_long3Y").longValue = long3.Y;
                        element.FindPropertyRelative("_long3Z").longValue = long3.Z;
                        break;
                }
            }

            serializedTree.ApplyModifiedPropertiesWithoutUndo();
            tree.OnValidate();
        }

        private static RuntimeBlackboardKeyDefinition GetDefinition(
            RuntimeBlackboardSchema schema,
            string key)
        {
            int keyHash = RuntimeBlackboard.DefaultStringHashFunc(key);
            Assert.That(schema.TryGetDefinition(keyHash, out RuntimeBlackboardKeyDefinition definition), Is.True);
            Assert.That(definition.Name, Is.EqualTo(key));
            return definition;
        }

        private static void AssertSchemaError(Runtime.BehaviorTree tree, string expectedMessage)
        {
            Assert.That(
                tree.TryGetRuntimeBlackboardSchema(out _, out string error),
                Is.False);
            Assert.That(error, Does.Contain(expectedMessage).IgnoreCase);
        }

        private static void AssertDefinition(
            RuntimeBlackboardKeyDefinition definition,
            RuntimeBlackboardValueType expectedType,
            RuntimeBlackboardSyncFlags expectedSyncFlags,
            bool expectedDefault)
        {
            Assert.That(definition.ValueType, Is.EqualTo(expectedType));
            Assert.That(definition.SyncFlags, Is.EqualTo(expectedSyncFlags));
            Assert.That(definition.HasDefaultValue, Is.EqualTo(expectedDefault));
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

        private readonly struct AuthoringKeySpec
        {
            public AuthoringKeySpec(
                string name,
                RuntimeBlackboardValueType valueType,
                RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.LocalOnly,
                bool hasDefaultValue = false,
                object defaultValue = null)
            {
                Name = name;
                ValueType = valueType;
                SyncFlags = syncFlags;
                HasDefaultValue = hasDefaultValue;
                DefaultValue = defaultValue;
            }

            public string Name { get; }
            public RuntimeBlackboardValueType ValueType { get; }
            public RuntimeBlackboardSyncFlags SyncFlags { get; }
            public bool HasDefaultValue { get; }
            public object DefaultValue { get; }
        }
    }
}
