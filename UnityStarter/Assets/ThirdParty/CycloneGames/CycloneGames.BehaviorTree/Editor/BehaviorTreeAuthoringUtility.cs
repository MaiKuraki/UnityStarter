using System;
using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEditor;
using BehaviorTreeAsset = CycloneGames.BehaviorTree.Runtime.BehaviorTree;

namespace CycloneGames.BehaviorTree.Editor
{
    internal static class BehaviorTreeAuthoringUtility
    {
        internal static int GetChildCount(BTNode node)
        {
            if (node is BTRootNode root)
            {
                return root.Child != null ? 1 : 0;
            }

            if (node is DecoratorNode decorator)
            {
                return decorator.Child != null ? 1 : 0;
            }

            if (node is CompositeNode composite)
            {
                return composite.Children != null ? composite.Children.Count : 0;
            }

            return 0;
        }

        internal static BTNode GetChildAt(BTNode node, int index)
        {
            if (index < 0)
            {
                return null;
            }

            if (node is BTRootNode root)
            {
                return index == 0 ? root.Child : null;
            }

            if (node is DecoratorNode decorator)
            {
                return index == 0 ? decorator.Child : null;
            }

            if (node is CompositeNode composite &&
                composite.Children != null &&
                index < composite.Children.Count)
            {
                return composite.Children[index];
            }

            return null;
        }

        internal static void CollectReachableNodes(
            BehaviorTreeAsset tree,
            HashSet<BTNode> destination,
            List<BTNode> traversalOrder = null)
        {
            if (tree == null || tree.Root == null || destination == null)
            {
                return;
            }

            var stack = new Stack<BTNode>();
            stack.Push(tree.Root);
            while (stack.Count > 0)
            {
                BTNode node = stack.Pop();
                if (node == null || !destination.Add(node))
                {
                    continue;
                }

                traversalOrder?.Add(node);
                int childCount = GetChildCount(node);
                for (int i = childCount - 1; i >= 0; i--)
                {
                    BTNode child = GetChildAt(node, i);
                    if (child != null)
                    {
                        stack.Push(child);
                    }
                }
            }
        }

        internal static List<string> ValidatePersistentAsset(BehaviorTreeAsset tree)
        {
            var issues = new List<string>();
            if (tree == null || !EditorUtility.IsPersistent(tree))
            {
                return issues;
            }

            string treePath = AssetDatabase.GetAssetPath(tree);
            if (string.IsNullOrEmpty(treePath))
            {
                issues.Add("The persistent tree has no AssetDatabase path.");
                return issues;
            }

            var registeredNodes = new HashSet<BTNode>();
            if (tree.Nodes != null)
            {
                for (int i = 0; i < tree.Nodes.Count; i++)
                {
                    BTNode node = tree.Nodes[i];
                    if (node == null)
                    {
                        continue;
                    }

                    registeredNodes.Add(node);
                    ValidateOwnedNode(tree, treePath, node, $"Nodes[{i}]", issues);
                }
            }

            var reachableNodes = new HashSet<BTNode>();
            var traversalOrder = new List<BTNode>();
            CollectReachableNodes(tree, reachableNodes, traversalOrder);
            for (int i = 0; i < traversalOrder.Count; i++)
            {
                BTNode node = traversalOrder[i];
                if (!registeredNodes.Contains(node))
                {
                    issues.Add($"Reachable node '{Describe(node)}' is not registered in BehaviorTree.Nodes.");
                }

                if (!registeredNodes.Contains(node))
                {
                    ValidateOwnedNode(tree, treePath, node, "Reachable node", issues);
                }
            }

            UnityEngine.Object[] assets = AssetDatabase.LoadAllAssetsAtPath(treePath);
            for (int i = 0; i < assets.Length; i++)
            {
                if (assets[i] is BTNode node &&
                    !registeredNodes.Contains(node) &&
                    !reachableNodes.Contains(node))
                {
                    issues.Add($"Behavior tree sub-asset '{Describe(node)}' is not registered in BehaviorTree.Nodes.");
                }
            }

            return issues;
        }

        internal static bool SynchronizePersistentAsset(BehaviorTreeAsset tree)
        {
            if (tree == null || !EditorUtility.IsPersistent(tree))
            {
                return false;
            }

            string assetPath = AssetDatabase.GetAssetPath(tree);
            if (string.IsNullOrEmpty(assetPath))
            {
                throw new InvalidOperationException("The persistent tree has no AssetDatabase path.");
            }

            EditorUtility.SetDirty(tree);
            AssetDatabase.SaveAssetIfDirty(tree);
            AssetDatabase.ImportAsset(assetPath, ImportAssetOptions.ForceSynchronousImport);
            return true;
        }

        private static void ValidateOwnedNode(
            BehaviorTreeAsset tree,
            string treePath,
            BTNode node,
            string location,
            List<string> issues)
        {
            if (node.Tree != tree)
            {
                issues.Add($"{location} '{Describe(node)}' has an invalid owner tree reference.");
            }

            string nodePath = AssetDatabase.GetAssetPath(node);
            if (string.IsNullOrEmpty(nodePath))
            {
                issues.Add($"{location} '{Describe(node)}' is transient and is not stored in the tree asset.");
                return;
            }

            if (!string.Equals(nodePath, treePath, StringComparison.Ordinal))
            {
                issues.Add(
                    $"{location} '{Describe(node)}' belongs to foreign asset '{nodePath}', expected '{treePath}'.");
                return;
            }

            if (!AssetDatabase.IsSubAsset(node))
            {
                issues.Add($"{location} '{Describe(node)}' is not a sub-asset of its behavior tree.");
            }
        }

        private static string Describe(BTNode node)
        {
            if (node == null)
            {
                return "<null>";
            }

            return string.IsNullOrEmpty(node.name) ? node.GetType().Name : node.name;
        }
    }
}
