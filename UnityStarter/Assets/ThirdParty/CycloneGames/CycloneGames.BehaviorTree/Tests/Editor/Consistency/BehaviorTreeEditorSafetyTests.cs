using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using CycloneGames.BehaviorTree.Editor;
using CycloneGames.BehaviorTree.Editor.CustomEditors;
using CycloneGames.BehaviorTree.Runtime.Components;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Actions;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.PerformanceTest;
using NUnit.Framework;
using UnityEditor;
using UnityEditor.Experimental.GraphView;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.BehaviorTree.Tests.Editor.Consistency
{
    public class BehaviorTreeEditorSafetyTests
    {
        [Test]
        public void PositionChange_DoesNotRunWholeTreeValidation()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var node = ScriptableObject.CreateInstance<ValidationCountingNode>();
            try
            {
                node.Tree = tree;
                tree.Nodes.Add(node);
                node.OnValidate();

                node.Position = new Vector2(20f, 40f);

                Assert.That(node.ValidationCount, Is.EqualTo(1));
            }
            finally
            {
                UnityEngine.Object.DestroyImmediate(node);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void PopulateView_RootlessTree_DoesNotMutateAsset()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                view.PopulateView(tree);

                Assert.That(tree.Root, Is.Null);
                Assert.That(tree.Nodes, Is.Empty);
            }
            finally
            {
                view.ClearView();
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void RepairMissingRoot_ChangesOnlyAfterExplicitRequest()
        {
            string assetPath = $"Assets/__BehaviorTreeRootRepair_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            BehaviorTreeView view = null;
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                view = new BehaviorTreeView();
                view.PopulateView(tree);
                Assert.That(tree.Root, Is.Null);

                MethodInfo method = typeof(BehaviorTreeView).GetMethod(
                    "TryRepairMissingRoot",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(method, Is.Not.Null);
                var arguments = new object[] { null };
                bool repaired = (bool)method.Invoke(view, arguments);
                string message = (string)arguments[0];

                Assert.That(repaired, Is.True, message);
                Assert.That(tree.Root, Is.TypeOf<BTRootNode>());
                Assert.That(tree.Nodes, Does.Contain(tree.Root));
            }
            finally
            {
                view?.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void ValidationReport_IncludesCanonicalCompilerDiagnostics()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var view = new BehaviorTreeView();
            try
            {
                root.Tree = tree;
                root.GUID = Guid.NewGuid().ToString();
                tree.Root = root;
                tree.Nodes.Add(root);
                view.PopulateView(tree);

                string report = view.GetValidationReport();

                Assert.That(report, Does.Contain("Compiler:"));
                Assert.That(report, Does.Contain("root child is null"));
            }
            finally
            {
                view.ClearView();
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void CompatiblePorts_RejectConnectionThatWouldCreateCycle()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var parent = ScriptableObject.CreateInstance<SequencerNode>();
            var child = ScriptableObject.CreateInstance<SequencerNode>();
            var view = new BehaviorTreeView();
            try
            {
                parent.Tree = tree;
                child.Tree = tree;
                parent.GUID = Guid.NewGuid().ToString();
                child.GUID = Guid.NewGuid().ToString();
                parent.Children.Add(child);
                tree.Nodes.Add(parent);
                tree.Nodes.Add(child);
                view.PopulateView(tree);

                var parentView = view.GetNodeByGuid(parent.GUID) as BTNodeView;
                var childView = view.GetNodeByGuid(child.GUID) as BTNodeView;
                var compatiblePorts = view.GetCompatiblePorts(childView.OutputPort, null);

                bool foundParentInput = false;
                for (int i = 0; i < compatiblePorts.Count; i++)
                {
                    if (ReferenceEquals(compatiblePorts[i], parentView.InputPort))
                    {
                        foundParentInput = true;
                        break;
                    }
                }

                Assert.That(foundParentInput, Is.False);
            }
            finally
            {
                view.ClearView();
                UnityEngine.Object.DestroyImmediate(child);
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void ReadOnlyMode_BlocksPersistentGraphOperationsButKeepsSelection()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var child = ScriptableObject.CreateInstance<WaitNode>();
            var view = new BehaviorTreeView();
            try
            {
                root.Tree = tree;
                root.GUID = Guid.NewGuid().ToString();
                root.Position = new Vector2(300f, 100f);
                child.Tree = tree;
                child.GUID = Guid.NewGuid().ToString();
                child.Position = new Vector2(40f, 500f);
                root.Child = child;
                tree.Root = root;
                tree.Nodes.Add(root);
                tree.Nodes.Add(child);

                view.PopulateView(tree);
                BTNodeView childView = view.GetNodeByGuid(child.GUID) as BTNodeView;
                Assert.That(childView, Is.Not.Null);
                view.AddToSelection(childView);
                InvokePrivate(view, "CopySelectedNodes", Vector2.zero);

                MethodInfo setReadOnly = typeof(BehaviorTreeView).GetMethod(
                    "SetAuthoringReadOnly",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(setReadOnly, Is.Not.Null);
                setReadOnly.Invoke(view, new object[] { true });

                Assert.That((childView.capabilities & Capabilities.Selectable) != 0, Is.True);
                Assert.That((childView.capabilities & Capabilities.Movable) != 0, Is.False);
                Assert.That((childView.capabilities & Capabilities.Deletable) != 0, Is.False);
                Assert.That(childView.InputPort.enabledSelf, Is.False);
                Assert.That(view.GetCompatiblePorts(childView.InputPort, null), Is.Empty);

                int nodeCount = tree.Nodes.Count;
                Vector2 rootPosition = root.Position;
                Vector2 childPosition = child.Position;

                view.DeleteSelection();
                InvokePrivate(view, "PasteCopiedNodes", new Vector2(120f, 80f));
                view.SortNodes();

                Assert.That(tree.Nodes.Count, Is.EqualTo(nodeCount));
                Assert.That(root.Position, Is.EqualTo(rootPosition));
                Assert.That(child.Position, Is.EqualTo(childPosition));

                var rootless = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
                try
                {
                    view.PopulateView(rootless);
                    var arguments = new object[] { null };
                    MethodInfo repair = typeof(BehaviorTreeView).GetMethod(
                        "TryRepairMissingRoot",
                        BindingFlags.Instance | BindingFlags.NonPublic);
                    Assert.That(repair, Is.Not.Null);
                    bool repaired = (bool)repair.Invoke(view, arguments);
                    Assert.That(repaired, Is.False);
                    Assert.That((string)arguments[0], Does.Contain("read-only"));
                    Assert.That(rootless.Root, Is.Null);
                    Assert.That(rootless.Nodes, Is.Empty);
                }
                finally
                {
                    UnityEngine.Object.DestroyImmediate(rootless);
                }
            }
            finally
            {
                view.ClearView();
                UnityEngine.Object.DestroyImmediate(child);
                UnityEngine.Object.DestroyImmediate(root);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void CompositeNormalization_IsExplicitDeterministicAndPreservesSidecars()
        {
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var parent = ScriptableObject.CreateInstance<ProbabilityBranch>();
            var childC = ScriptableObject.CreateInstance<WaitNode>();
            var childA = ScriptableObject.CreateInstance<WaitNode>();
            var childB = ScriptableObject.CreateInstance<WaitNode>();
            try
            {
                parent.Tree = tree;
                childC.GUID = "c";
                childA.GUID = "a";
                childB.GUID = "b";
                childC.Position = new Vector2(100f, 0f);
                childA.Position = new Vector2(100f, 0f);
                childB.Position = new Vector2(100f, 0f);
                parent.Children.Add(childC);
                parent.Children.Add(childA);
                parent.Children.Add(childB);
                SetFloatList(parent, "_probabilities", 0.7f, 0.1f, 0.2f);

                parent.OnValidate();

                Assert.That(parent.Children, Is.EqualTo(new[] { childC, childA, childB }));
                Assert.That(parent.Probabilities, Is.EqualTo(new[] { 0.7f, 0.1f, 0.2f }));

                tree.NormalizeCompositeChildren(parent);

                Assert.That(parent.Children, Is.EqualTo(new[] { childA, childB, childC }));
                Assert.That(parent.Probabilities, Is.EqualTo(new[] { 0.1f, 0.2f, 0.7f }));

                parent.Children = null;
                parent.OnValidate();
                Assert.That(parent.Children, Is.Null);

                tree.NormalizeCompositeChildren(parent);
                Assert.That(parent.Children, Is.Not.Null.And.Empty);
                Assert.That(parent.Probabilities, Is.Not.Null.And.Empty);
            }
            finally
            {
                Undo.ClearAll();
                UnityEngine.Object.DestroyImmediate(childB);
                UnityEngine.Object.DestroyImmediate(childA);
                UnityEngine.Object.DestroyImmediate(childC);
                UnityEngine.Object.DestroyImmediate(parent);
                UnityEngine.Object.DestroyImmediate(tree);
            }
        }

        [Test]
        public void MiddleChildRemoval_PreservesProbabilityAndUtilityMappingsWithUndo()
        {
            string assetPath = $"Assets/__BehaviorTreeSidecarRemoval_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                var probability = tree.CreateNode(typeof(ProbabilityBranch)) as ProbabilityBranch;
                var utility = tree.CreateNode(typeof(UtilitySelectorNode)) as UtilitySelectorNode;
                var childA = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                var childB = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                var childC = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                childA.GUID = "a";
                childB.GUID = "b";
                childC.GUID = "c";
                childA.Position = new Vector2(0f, 0f);
                childB.Position = new Vector2(100f, 0f);
                childC.Position = new Vector2(200f, 0f);
                probability.Children.AddRange(new BTNode[] { childA, childB, childC });
                utility.Children.AddRange(new BTNode[] { childA, childB, childC });
                SetFloatList(probability, "_probabilities", 0.1f, 0.2f, 0.7f);
                SetStringList(utility, "_scoreKeys", "ScoreA", "ScoreB", "ScoreC");
                AssetDatabase.SaveAssetIfDirty(tree);
                Undo.ClearAll();

                tree.RemoveChild(probability, childB);
                tree.RemoveChild(utility, childB);

                Assert.That(probability.Children, Is.EqualTo(new[] { childA, childC }));
                Assert.That(probability.Probabilities, Is.EqualTo(new[] { 0.1f, 0.7f }));
                Assert.That(utility.Children, Is.EqualTo(new[] { childA, childC }));
                Assert.That(utility.ScoreKeys, Is.EqualTo(new[] { "ScoreA", "ScoreC" }));

                Undo.PerformUndo();
                Assert.That(probability.Children, Is.EqualTo(new[] { childA, childB, childC }));
                Assert.That(probability.Probabilities, Is.EqualTo(new[] { 0.1f, 0.2f, 0.7f }));
                Assert.That(utility.Children, Is.EqualTo(new[] { childA, childB, childC }));
                Assert.That(utility.ScoreKeys, Is.EqualTo(new[] { "ScoreA", "ScoreB", "ScoreC" }));

                Undo.PerformRedo();
                Assert.That(probability.Probabilities, Is.EqualTo(new[] { 0.1f, 0.7f }));
                Assert.That(utility.ScoreKeys, Is.EqualTo(new[] { "ScoreA", "ScoreC" }));
            }
            finally
            {
                Undo.ClearAll();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void Paste_PreservesSerializedConfigurationAndSanitizesExternalLinks()
        {
            string assetPath = $"Assets/__BehaviorTreeEditorSafety_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            BehaviorTreeView view = null;
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                var sequence = tree.CreateNode(typeof(SequencerNode)) as SequencerNode;
                var externalChild = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                sequence.AbortType = ConditionalAbortType.SELF;
                sequence.Children.Add(externalChild);
                EditorUtility.SetDirty(sequence);

                view = new BehaviorTreeView();
                view.PopulateView(tree);
                var sequenceView = view.GetNodeByGuid(sequence.GUID) as BTNodeView;
                view.AddToSelection(sequenceView);

                InvokePrivate(view, "CopySelectedNodes", Vector2.zero);
                InvokePrivate(view, "PasteCopiedNodes", new Vector2(100f, 80f));

                SequencerNode[] sequences = tree.Nodes.OfType<SequencerNode>().ToArray();
                Assert.That(sequences, Has.Length.EqualTo(2));
                SequencerNode pasted = sequences.Single(node => node != sequence);
                Assert.That(pasted.GUID, Is.Not.EqualTo(sequence.GUID));
                Assert.That(pasted.Tree, Is.SameAs(tree));
                Assert.That(pasted.AbortType, Is.EqualTo(sequence.AbortType));
                Assert.That(pasted.Children, Is.Empty);
            }
            finally
            {
                view?.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void DeleteNode_DetachesEveryInboundReferenceAndSupportsUndoRedo()
        {
            string assetPath = $"Assets/__BehaviorTreeAtomicDelete_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                var root = tree.CreateNode(typeof(BTRootNode)) as BTRootNode;
                var decorator = tree.CreateNode(typeof(WaitSuccessNode)) as WaitSuccessNode;
                var probability = tree.CreateNode(typeof(ProbabilityBranch)) as ProbabilityBranch;
                var childA = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                var deletedChild = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                var childC = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                childA.GUID = "a";
                deletedChild.GUID = "deleted";
                childC.GUID = "c";
                childA.Position = new Vector2(0f, 0f);
                deletedChild.Position = new Vector2(100f, 0f);
                childC.Position = new Vector2(200f, 0f);
                tree.Root = root;
                root.Child = deletedChild;
                decorator.Child = deletedChild;
                probability.Children.AddRange(new BTNode[] { childA, deletedChild, childC });
                SetFloatList(probability, "_probabilities", 0.1f, 0.2f, 0.7f);
                EditorUtility.SetDirty(tree);
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(decorator);
                EditorUtility.SetDirty(probability);
                AssetDatabase.SaveAssetIfDirty(tree);
                Undo.ClearAll();

                tree.DeleteNode(deletedChild);

                Assert.That(tree.Nodes.Any(node => node != null && node.GUID == "deleted"), Is.False);
                Assert.That(root.Child, Is.Null);
                Assert.That(decorator.Child, Is.Null);
                Assert.That(probability.Children, Is.EqualTo(new[] { childA, childC }));
                Assert.That(probability.Probabilities, Is.EqualTo(new[] { 0.1f, 0.7f }));

                Undo.PerformUndo();
                BTNode restoredChild = tree.Nodes.Single(node => node != null && node.GUID == "deleted");
                Assert.That(root.Child, Is.SameAs(restoredChild));
                Assert.That(decorator.Child, Is.SameAs(restoredChild));
                Assert.That(probability.Children, Is.EqualTo(new[] { childA, restoredChild, childC }));
                Assert.That(probability.Probabilities, Is.EqualTo(new[] { 0.1f, 0.2f, 0.7f }));

                Undo.PerformRedo();
                Assert.That(tree.Nodes.Any(node => node != null && node.GUID == "deleted"), Is.False);
                Assert.That(root.Child, Is.Null);
                Assert.That(decorator.Child, Is.Null);
                Assert.That(probability.Probabilities, Is.EqualTo(new[] { 0.1f, 0.7f }));
            }
            finally
            {
                Undo.ClearAll();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void RepairAsset_RecoversNullNodeRegistryThroughOneUndoableOperation()
        {
            string assetPath = $"Assets/__BehaviorTreeNullListRepair_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var composite = ScriptableObject.CreateInstance<SequencerNode>();
            var wait = ScriptableObject.CreateInstance<WaitNode>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                root.GUID = "root";
                composite.GUID = "composite";
                wait.GUID = "wait";
                AssetDatabase.AddObjectToAsset(root, tree);
                AssetDatabase.AddObjectToAsset(composite, tree);
                AssetDatabase.AddObjectToAsset(wait, tree);
                tree.Root = root;
                root.Child = composite;
                composite.Children = new List<BTNode> { wait };
                tree.Nodes = null;
                EditorUtility.SetDirty(tree);
                EditorUtility.SetDirty(root);
                EditorUtility.SetDirty(composite);
                EditorUtility.SetDirty(wait);
                AssetDatabase.SaveAssetIfDirty(tree);
                Undo.ClearAll();

                Assert.DoesNotThrow(() => view.PopulateView(tree));
                Assert.That(composite.Children, Is.EqualTo(new[] { wait }));

                MethodInfo repair = typeof(BehaviorTreeView).GetMethod(
                    "TryRepairAuthoringData",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(repair, Is.Not.Null);
                var arguments = new object[] { null };
                bool repaired = (bool)repair.Invoke(view, arguments);

                Assert.That(repaired, Is.True, (string)arguments[0]);
                Assert.That(tree.Nodes, Does.Contain(root));
                Assert.That(tree.Nodes, Does.Contain(composite));
                Assert.That(tree.Nodes, Does.Contain(wait));
                Assert.That(composite.Children, Is.EqualTo(new[] { wait }));
                Assert.That(root.Tree, Is.SameAs(tree));
                Assert.That(composite.Tree, Is.SameAs(tree));
                Assert.That(wait.Tree, Is.SameAs(tree));

                Undo.PerformUndo();
                Assert.That(tree.Nodes, Is.Not.Null.And.Empty);
                Assert.That(composite.Children, Is.EqualTo(new[] { wait }));

                Undo.PerformRedo();
                Assert.That(tree.Nodes, Does.Contain(root));
                Assert.That(tree.Nodes, Does.Contain(composite));
                Assert.That(tree.Nodes, Does.Contain(wait));
                Assert.That(composite.Children, Is.EqualTo(new[] { wait }));
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void PersistentValidationAndRepair_RejectForeignRootWithoutMutation()
        {
            string targetPath = $"Assets/__BehaviorTreeForeignRootTarget_{Guid.NewGuid():N}.asset";
            string foreignPath = $"Assets/__BehaviorTreeForeignRootSource_{Guid.NewGuid():N}.asset";
            var targetTree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var foreignTree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(targetTree, targetPath);
                AssetDatabase.CreateAsset(foreignTree, foreignPath);
                var foreignRoot = foreignTree.CreateNode(typeof(BTRootNode)) as BTRootNode;
                var foreignChild = foreignTree.CreateNode(typeof(WaitNode)) as WaitNode;
                foreignRoot.Child = foreignChild;
                foreignTree.Root = foreignRoot;
                EditorUtility.SetDirty(foreignTree);
                EditorUtility.SetDirty(foreignRoot);

                targetTree.Root = foreignRoot;
                EditorUtility.SetDirty(targetTree);
                view.PopulateView(targetTree);

                string report = view.GetValidationReport();
                Assert.That(report, Does.Contain("not registered"));
                Assert.That(report, Does.Contain("foreign asset"));

                bool repaired = TryRepairAuthoringData(view, out string message);

                Assert.That(repaired, Is.False);
                Assert.That(message, Does.Contain("foreign node"));
                Assert.That(targetTree.Root, Is.SameAs(foreignRoot));
                Assert.That(targetTree.Nodes, Is.Empty);
                Assert.That(AssetDatabase.GetAssetPath(foreignRoot), Is.EqualTo(foreignPath));
            }
            finally
            {
                view.ClearView();
                AssetDatabase.DeleteAsset(targetPath);
                AssetDatabase.DeleteAsset(foreignPath);
            }
        }

        [Test]
        public void PersistentValidationAndRepair_RejectForeignReachableChildWithoutMutation()
        {
            string targetPath = $"Assets/__BehaviorTreeForeignChildTarget_{Guid.NewGuid():N}.asset";
            string foreignPath = $"Assets/__BehaviorTreeForeignChildSource_{Guid.NewGuid():N}.asset";
            var targetTree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var foreignTree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(targetTree, targetPath);
                AssetDatabase.CreateAsset(foreignTree, foreignPath);
                var targetRoot = targetTree.CreateNode(typeof(BTRootNode)) as BTRootNode;
                var foreignChild = foreignTree.CreateNode(typeof(WaitNode)) as WaitNode;
                targetTree.Root = targetRoot;
                targetRoot.Child = foreignChild;
                EditorUtility.SetDirty(targetTree);
                EditorUtility.SetDirty(targetRoot);
                view.PopulateView(targetTree);

                string report = view.GetValidationReport();
                Assert.That(report, Does.Contain("not registered"));
                Assert.That(report, Does.Contain("foreign asset"));
                int registeredCount = targetTree.Nodes.Count;

                bool repaired = TryRepairAuthoringData(view, out string message);

                Assert.That(repaired, Is.False);
                Assert.That(message, Does.Contain("foreign node"));
                Assert.That(targetRoot.Child, Is.SameAs(foreignChild));
                Assert.That(targetTree.Nodes, Has.Count.EqualTo(registeredCount));
                Assert.That(AssetDatabase.GetAssetPath(foreignChild), Is.EqualTo(foreignPath));
            }
            finally
            {
                view.ClearView();
                AssetDatabase.DeleteAsset(targetPath);
                AssetDatabase.DeleteAsset(foreignPath);
            }
        }

        [Test]
        public void RepairAsset_RegistersReachableSameAssetNodeAndPassesValidation()
        {
            string assetPath = $"Assets/__BehaviorTreeUnregisteredReachable_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                var root = tree.CreateNode(typeof(BTRootNode)) as BTRootNode;
                var child = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                tree.Root = root;
                root.Child = child;
                tree.Nodes.Remove(child);
                EditorUtility.SetDirty(tree);
                EditorUtility.SetDirty(root);
                view.PopulateView(tree);

                Assert.That(view.GetValidationReport(), Does.Contain("not registered"));

                bool repaired = TryRepairAuthoringData(view, out string message);

                Assert.That(repaired, Is.True, message);
                Assert.That(tree.Nodes, Does.Contain(child));
                Assert.That(child.Tree, Is.SameAs(tree));
                Assert.That(view.GetValidationReport(), Does.Not.Contain("not registered"));
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void RepairAsset_AttachesSafeTransientReachableNodesAsSubAssets()
        {
            string assetPath = $"Assets/__BehaviorTreeTransientRepair_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var child = ScriptableObject.CreateInstance<WaitNode>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                root.name = "TransientRoot";
                root.GUID = "transient-root";
                child.name = "TransientChild";
                child.GUID = "transient-child";
                root.Child = child;
                tree.Root = root;
                tree.Nodes = null;
                view.PopulateView(tree);

                Assert.That(view.GetValidationReport(), Does.Contain("transient"));

                bool repaired = TryRepairAuthoringData(view, out string message);

                Assert.That(repaired, Is.True, message);
                Assert.That(tree.Nodes, Is.EqualTo(new BTNode[] { root, child }));
                Assert.That(root.Tree, Is.SameAs(tree));
                Assert.That(child.Tree, Is.SameAs(tree));
                Assert.That(AssetDatabase.GetAssetPath(root), Is.EqualTo(assetPath));
                Assert.That(AssetDatabase.GetAssetPath(child), Is.EqualTo(assetPath));
                Assert.That(AssetDatabase.IsSubAsset(root), Is.True);
                Assert.That(AssetDatabase.IsSubAsset(child), Is.True);
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
                if (root != null)
                {
                    UnityEngine.Object.DestroyImmediate(root);
                }

                if (child != null)
                {
                    UnityEngine.Object.DestroyImmediate(child);
                }
            }
        }

        [Test]
        public void RepairAsset_CompilerFailureRollsBackAllAuthoringChanges()
        {
            string assetPath = $"Assets/__BehaviorTreeRepairRollback_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var root = ScriptableObject.CreateInstance<BTRootNode>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                root.name = "InvalidRoot";
                root.GUID = "invalid-root";
                AssetDatabase.AddObjectToAsset(root, tree);
                tree.Root = root;
                tree.Nodes = null;
                EditorUtility.SetDirty(tree);
                AssetDatabase.SaveAssetIfDirty(tree);
                Undo.ClearAll();
                view.PopulateView(tree);

                bool repaired = TryRepairAuthoringData(view, out string message);

                Assert.That(repaired, Is.False);
                Assert.That(message, Does.Contain("root child is null"));
                Assert.That(tree.Nodes, Is.Null);
                Assert.That(root.Tree, Is.Null);
                Assert.That(AssetDatabase.GetAssetPath(root), Is.EqualTo(assetPath));
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void PasteTransaction_DuplicateClipboardEntryRollsBackNodesAndSubAssets()
        {
            string assetPath = $"Assets/__BehaviorTreePasteRollback_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                var source = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                view.PopulateView(tree);
                FieldInfo copiedNodesField = typeof(BehaviorTreeView).GetField(
                    "_copiedNodes",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(copiedNodesField, Is.Not.Null);
                var copiedNodes = (List<BTNode>)copiedNodesField.GetValue(view);
                copiedNodes.Add(source);
                copiedNodes.Add(source);
                int registeredCount = tree.Nodes.Count;
                int subAssetCount = AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<BTNode>().Count();
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"\[BehaviorTree\] Paste transaction was rolled back:"));

                InvokePrivate(view, "PasteCopiedNodes", new Vector2(100f, 100f));

                Assert.That(tree.Nodes, Has.Count.EqualTo(registeredCount));
                Assert.That(tree.Nodes, Does.Contain(source));
                Assert.That(
                    AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<BTNode>().Count(),
                    Is.EqualTo(subAssetCount));
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void CreateTransaction_InvalidNodeTypeLeavesAssetUnchanged()
        {
            string assetPath = $"Assets/__BehaviorTreeCreateRollback_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                view.PopulateView(tree);
                LogAssert.Expect(
                    LogType.Error,
                    new Regex(@"\[BehaviorTree\] Create transaction was rolled back:"));
                MethodInfo create = typeof(BehaviorTreeView).GetMethod(
                    "CreateNode",
                    BindingFlags.Instance | BindingFlags.NonPublic);
                Assert.That(create, Is.Not.Null);

                create.Invoke(view, new object[] { typeof(ScriptableObject), Vector2.zero });

                Assert.That(tree.Nodes, Is.Empty);
                Assert.That(AssetDatabase.LoadAllAssetsAtPath(assetPath).OfType<BTNode>(), Is.Empty);
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void PartialPaste_PreservesSelectedChildSidecarsAndSupportsUndoRedo()
        {
            string assetPath = $"Assets/__BehaviorTreePartialPaste_{Guid.NewGuid():N}.asset";
            var tree = ScriptableObject.CreateInstance<Runtime.BehaviorTree>();
            var view = new BehaviorTreeView();
            try
            {
                AssetDatabase.CreateAsset(tree, assetPath);
                var probability = tree.CreateNode(typeof(ProbabilityBranch)) as ProbabilityBranch;
                var utility = tree.CreateNode(typeof(UtilitySelectorNode)) as UtilitySelectorNode;
                var probabilityChildren = CreateNamedChildren(tree, "P");
                var utilityChildren = CreateNamedChildren(tree, "U");
                probability.Children.AddRange(probabilityChildren);
                utility.Children.AddRange(utilityChildren);
                SetFloatList(probability, "_probabilities", 0.1f, 0.2f, 0.7f);
                SetStringList(utility, "_scoreKeys", "ScoreA", "ScoreB", "ScoreC");
                EditorUtility.SetDirty(probability);
                EditorUtility.SetDirty(utility);
                AssetDatabase.SaveAssetIfDirty(tree);

                view.PopulateView(tree);
                view.AddToSelection(view.GetNodeByGuid(probability.GUID));
                view.AddToSelection(view.GetNodeByGuid(probabilityChildren[2].GUID));
                view.AddToSelection(view.GetNodeByGuid(utility.GUID));
                view.AddToSelection(view.GetNodeByGuid(utilityChildren[2].GUID));
                InvokePrivate(view, "CopySelectedNodes", Vector2.zero);
                Undo.ClearAll();

                InvokePrivate(view, "PasteCopiedNodes", new Vector2(300f, 120f));

                ProbabilityBranch pastedProbability = tree.Nodes
                    .OfType<ProbabilityBranch>()
                    .Single(node => node != probability);
                UtilitySelectorNode pastedUtility = tree.Nodes
                    .OfType<UtilitySelectorNode>()
                    .Single(node => node != utility);
                Assert.That(pastedProbability.Children, Has.Count.EqualTo(1));
                Assert.That(pastedProbability.Probabilities, Is.EqualTo(new[] { 0.7f }));
                Assert.That(pastedUtility.Children, Has.Count.EqualTo(1));
                Assert.That(pastedUtility.ScoreKeys, Is.EqualTo(new[] { "ScoreC" }));

                Undo.PerformUndo();
                Assert.That(tree.Nodes.OfType<ProbabilityBranch>().Count(), Is.EqualTo(1));
                Assert.That(tree.Nodes.OfType<UtilitySelectorNode>().Count(), Is.EqualTo(1));

                Undo.PerformRedo();
                pastedProbability = tree.Nodes.OfType<ProbabilityBranch>().Single(node => node != probability);
                pastedUtility = tree.Nodes.OfType<UtilitySelectorNode>().Single(node => node != utility);
                Assert.That(pastedProbability.Probabilities, Is.EqualTo(new[] { 0.7f }));
                Assert.That(pastedUtility.ScoreKeys, Is.EqualTo(new[] { "ScoreC" }));
            }
            finally
            {
                Undo.ClearAll();
                view.ClearView();
                AssetDatabase.DeleteAsset(assetPath);
            }
        }

        [Test]
        public void BenchmarkValidation_RejectsDerivedWorkloadBeyondHardLimit()
        {
            var config = new BehaviorTreeBenchmarkConfig
            {
                AgentCount = 100000,
                LeafNodesPerTree = 512,
                DecoratorLayersPerLeaf = 64,
                SimulatedWorkIterationsPerLeaf = 100000,
                MeasurementFrames = 1000000,
                TicksPerFrame = 64
            };

            MethodInfo method = typeof(BehaviorTreeBenchmarkWindow).GetMethod(
                "TryValidateRequest",
                BindingFlags.Static | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            var arguments = new object[] { config, null };
            bool valid = (bool)method.Invoke(null, arguments);

            Assert.That(valid, Is.False);
            Assert.That(arguments[1], Is.TypeOf<string>());
            Assert.That((string)arguments[1], Is.Not.Empty);
        }

        [Test]
        public void EditorSelection_RunnerWithoutTree_DoesNotThrow()
        {
            var gameObject = new GameObject("BehaviorTreeEditorSelectionTest");
            var window = ScriptableObject.CreateInstance<BehaviorTreeEditor>();
            try
            {
                gameObject.AddComponent<BTRunnerComponent>();
                Selection.activeObject = gameObject;
                MethodInfo method = typeof(BehaviorTreeEditor).GetMethod(
                    "OnSelectionChange",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(method, Is.Not.Null);
                Assert.DoesNotThrow(() => method.Invoke(window, null));
            }
            finally
            {
                Selection.activeObject = null;
                UnityEngine.Object.DestroyImmediate(window);
                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void StateMachineInspector_ExcludesFieldsDrawnByItsCustomSection()
        {
            var gameObject = new GameObject("BehaviorTreeStateMachineInspectorTest");
            UnityEditor.Editor inspector = null;
            try
            {
                var component = gameObject.AddComponent<BTStateMachineComponent>();
                inspector = UnityEditor.Editor.CreateEditor(component, typeof(BTStateMachineComponentEditor));
                MethodInfo method = typeof(BTStateMachineComponentEditor).GetMethod(
                    "IsPropertyDrawnExplicitly",
                    BindingFlags.Instance | BindingFlags.NonPublic);

                Assert.That(method, Is.Not.Null);
                Assert.That((bool)method.Invoke(inspector, new object[] { "_initialState" }), Is.True);
                Assert.That((bool)method.Invoke(inspector, new object[] { "_states" }), Is.True);
                Assert.That((bool)method.Invoke(inspector, new object[] { "_futureDerivedField" }), Is.False);
            }
            finally
            {
                if (inspector != null)
                {
                    UnityEngine.Object.DestroyImmediate(inspector);
                }

                UnityEngine.Object.DestroyImmediate(gameObject);
            }
        }

        private static void InvokePrivate(BehaviorTreeView view, string methodName, Vector2 position)
        {
            MethodInfo method = typeof(BehaviorTreeView).GetMethod(
                methodName,
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(method, Is.Not.Null);
            method.Invoke(view, new object[] { position });
        }

        private static bool TryRepairAuthoringData(BehaviorTreeView view, out string message)
        {
            MethodInfo repair = typeof(BehaviorTreeView).GetMethod(
                "TryRepairAuthoringData",
                BindingFlags.Instance | BindingFlags.NonPublic);
            Assert.That(repair, Is.Not.Null);
            var arguments = new object[] { null };
            bool repaired = (bool)repair.Invoke(view, arguments);
            message = (string)arguments[0];
            return repaired;
        }

        private static WaitNode[] CreateNamedChildren(Runtime.BehaviorTree tree, string prefix)
        {
            var children = new WaitNode[3];
            for (int i = 0; i < children.Length; i++)
            {
                children[i] = tree.CreateNode(typeof(WaitNode)) as WaitNode;
                children[i].name = $"{prefix}{(char)('A' + i)}";
                children[i].GUID = $"{prefix.ToLowerInvariant()}-{i}";
                children[i].Position = new Vector2(i * 100f, 0f);
                EditorUtility.SetDirty(children[i]);
            }

            return children;
        }

        private static void SetFloatList(UnityEngine.Object target, string propertyName, params float[] values)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Missing serialized property {propertyName}.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).floatValue = values[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private static void SetStringList(UnityEngine.Object target, string propertyName, params string[] values)
        {
            var serializedObject = new SerializedObject(target);
            SerializedProperty property = serializedObject.FindProperty(propertyName);
            Assert.That(property, Is.Not.Null, $"Missing serialized property {propertyName}.");
            property.arraySize = values.Length;
            for (int i = 0; i < values.Length; i++)
            {
                property.GetArrayElementAtIndex(i).stringValue = values[i];
            }

            serializedObject.ApplyModifiedPropertiesWithoutUndo();
        }

        private sealed class ValidationCountingNode : BTNode
        {
            public int ValidationCount { get; private set; }

            protected override void CheckIntegrity()
            {
                ValidationCount++;
            }
        }
    }
}
