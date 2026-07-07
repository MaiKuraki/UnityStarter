using System;
using System.Collections.Generic;
using System.Text;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Nodes;
using CycloneGames.BehaviorTree.Runtime.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Nodes.Decorators;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Compilation
{
    public static class BehaviorTreeCompiler
    {
        public static RuntimeBehaviorTree Compile(BehaviorTree source, RuntimeBTContext context = null)
        {
            if (source == null)
            {
                throw new BehaviorTreeCompileException("Behavior tree asset is null.");
            }

            List<string> errors = Validate(source);
            if (errors.Count > 0)
            {
                throw new BehaviorTreeCompileException(FormatErrors(source, errors));
            }

            context ??= new RuntimeBTContext();

            var blackboard = new RuntimeBlackboard
            {
                Context = context
            };

            RuntimeNode runtimeRoot;
            try
            {
                runtimeRoot = source.Root.CreateRuntimeNode();
            }
            catch (Exception exception)
            {
                throw new BehaviorTreeCompileException(
                    $"Behavior tree runtime graph creation failed for '{source.name}': {exception.Message}",
                    exception);
            }

            if (runtimeRoot == null)
            {
                throw new BehaviorTreeCompileException($"Root node {source.Root.GetType().Name} returned null runtime node.");
            }

            return new RuntimeBehaviorTree(runtimeRoot, blackboard, context);
        }

        public static List<string> Validate(BehaviorTree source)
        {
            var errors = new List<string>(4);
            if (source == null)
            {
                errors.Add("Behavior tree asset is null.");
                return errors;
            }

            if (source.Root == null)
            {
                errors.Add("Root is null.");
                return errors;
            }

            var visited = new HashSet<BTNode>();
            var visiting = new HashSet<BTNode>();
            var guids = new HashSet<string>();
            ValidateNode(source.Root, "Root", visited, visiting, guids, errors);
            return errors;
        }

        private static void ValidateNode(
            BTNode node,
            string path,
            HashSet<BTNode> visited,
            HashSet<BTNode> visiting,
            HashSet<string> guids,
            List<string> errors)
        {
            if (node == null)
            {
                errors.Add($"{path}: null node reference.");
                return;
            }

            if (!string.IsNullOrEmpty(node.GUID) && !guids.Add(node.GUID))
            {
                errors.Add($"{path}: duplicate node GUID '{node.GUID}'.");
            }

            if (visited.Contains(node))
            {
                return;
            }

            if (!visiting.Add(node))
            {
                errors.Add($"{path}: cycle detected.");
                return;
            }

            ValidateChildren(node, path, visited, visiting, guids, errors);
            visiting.Remove(node);
            visited.Add(node);
        }

        private static void ValidateChildren(
            BTNode node,
            string path,
            HashSet<BTNode> visited,
            HashSet<BTNode> visiting,
            HashSet<string> guids,
            List<string> errors)
        {
            if (node is BTRootNode root)
            {
                if (root.Child == null)
                {
                    errors.Add($"{path}: root child is null.");
                    return;
                }

                ValidateNode(root.Child, $"{path}/{root.Child.GetType().Name}", visited, visiting, guids, errors);
                return;
            }

            if (node is DecoratorNode decorator)
            {
                if (node is SubTreeNode subTree)
                {
                    ValidateSubTreeNode(subTree, path, visited, visiting, guids, errors);
                    return;
                }

                if (decorator.Child == null)
                {
                    errors.Add($"{path}: decorator child is null.");
                    return;
                }

                ValidateNode(decorator.Child, $"{path}/{decorator.Child.GetType().Name}", visited, visiting, guids, errors);
                return;
            }

            if (node is CompositeNode composite)
            {
                for (int i = 0; i < composite.Children.Count; i++)
                {
                    BTNode child = composite.Children[i];
                    if (child == null)
                    {
                        errors.Add($"{path}: child[{i}] is null.");
                        continue;
                    }

                    ValidateNode(child, $"{path}/{child.GetType().Name}[{i}]", visited, visiting, guids, errors);
                }
            }
        }

        private static void ValidateSubTreeNode(
            SubTreeNode subTree,
            string path,
            HashSet<BTNode> visited,
            HashSet<BTNode> visiting,
            HashSet<string> guids,
            List<string> errors)
        {
            if (subTree.Child != null)
            {
                ValidateNode(subTree.Child, $"{path}/{subTree.Child.GetType().Name}", visited, visiting, guids, errors);
                return;
            }

            BehaviorTree subTreeAsset = subTree.SubTreeAsset;
            if (subTreeAsset == null || subTreeAsset.Root == null)
            {
                errors.Add($"{path}: subtree node has neither child nor subtree asset root.");
                return;
            }

            ValidateNode(subTreeAsset.Root, $"{path}/SubTreeAsset/{subTreeAsset.Root.GetType().Name}", visited, visiting, guids, errors);
        }

        private static string FormatErrors(BehaviorTree source, List<string> errors)
        {
            var builder = new StringBuilder(128 + errors.Count * 48);
            builder.Append("Behavior tree compile failed for '");
            builder.Append(source != null ? source.name : "<null>");
            builder.AppendLine("':");
            for (int i = 0; i < errors.Count; i++)
            {
                builder.Append("- ");
                builder.AppendLine(errors[i]);
            }

            return builder.ToString();
        }
    }
}
