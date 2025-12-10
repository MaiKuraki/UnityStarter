using System.Collections.Generic;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using CycloneGames.BehaviorTree.Runtime.Pooling;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime
{
    /// <summary>
    /// Extension methods for BehaviorTree with object pooling optimizations.
    /// </summary>
    public static class BehaviorTreeOptimizations
    {
        /// <summary>
        /// Clones the behavior tree using object pooling for optimal performance.
        /// </summary>
        public static BehaviorTree CloneTreeOptimized(this BehaviorTree source, GameObject owner)
        {
            if (source == null || source.Root == null)
            {
                Debug.LogError("[BehaviorTree] Cannot clone tree: Source or Root is null.");
                return null;
            }

            var tree = UnityEngine.Object.Instantiate(source);
            tree.Owner = owner;

            tree.Root = source.Root.CloneOptimized();
            if (tree.Root == null)
            {
                Debug.LogError("[BehaviorTree] Failed to clone root node.");
                UnityEngine.Object.Destroy(tree);
                return null;
            }

            tree.Nodes.Clear();
            tree._isCloned = true;

            TraverseOptimized(tree.Root, tree);

            return tree;
        }

        private static void TraverseOptimized(BTNode node, BehaviorTree tree)
        {
            if (node == null) return;

            var traverseStack = new Stack<BTNode>(16);
            traverseStack.Push(node);

            while (traverseStack.Count > 0)
            {
                var current = traverseStack.Pop();
                if (current == null) continue;

                current.Tree = tree;
                tree.Nodes.Add(current);

                var children = tree.GetChildren(current);
                for (int i = children.Count - 1; i >= 0; i--)
                {
                    var child = children[i];
                    if (child != null)
                    {
                        traverseStack.Push(child);
                    }
                }
            }
        }
    }

    /// <summary>
    /// Extension methods for BTNode with object pooling optimizations.
    /// </summary>
    public static class BTNodeOptimizations
    {
        /// <summary>
        /// Clones the node using object pooling when available.
        /// Recursively clones children using the same optimization.
        /// </summary>
        public static BTNode CloneOptimized(this BTNode source)
        {
            if (source == null) return null;

            BTNode cloned;

            if (Application.isPlaying)
            {
                cloned = BTNodePool.Get(source);
                if (cloned == null)
                {
                    cloned = source.Clone();
                }
                else
                {
                    cloned.Position = source.Position;
                    cloned.GUID = source.GUID;
                }
            }
            else
            {
                cloned = source.Clone();
            }

            if (cloned is CompositeNode compositeClone && source is CompositeNode compositeSource)
            {
                compositeClone.Children.Clear();
                for (int i = 0; i < compositeSource.Children.Count; i++)
                {
                    if (compositeSource.Children[i] != null)
                    {
                        compositeClone.Children.Add(compositeSource.Children[i].CloneOptimized());
                    }
                }
            }
            else if (cloned is DecoratorNode decoratorClone && source is DecoratorNode decoratorSource)
            {
                if (decoratorSource.Child != null)
                {
                    decoratorClone.Child = decoratorSource.Child.CloneOptimized();
                }
            }
            else if (cloned is BTRootNode rootClone && source is BTRootNode rootSource)
            {
                if (rootSource.Child != null)
                {
                    rootClone.Child = rootSource.Child.CloneOptimized();
                }
            }

            return cloned;
        }

        /// <summary>
        /// Returns the node to the object pool for reuse.
        /// </summary>
        public static void ReturnToPool(this BTNode node)
        {
            if (node != null && Application.isPlaying)
            {
                BTNodePool.Return(node);
            }
        }
    }
}
