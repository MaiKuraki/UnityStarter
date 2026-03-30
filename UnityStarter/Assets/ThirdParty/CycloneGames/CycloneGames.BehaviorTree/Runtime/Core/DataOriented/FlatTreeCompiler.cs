using System.Collections.Generic;
using Unity.Collections;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Compositors;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Compiles a managed RuntimeBehaviorTree into a FlatBehaviorTree for Burst/Jobs execution.
    ///
    /// The compiler flattens the tree hierarchy into contiguous arrays:
    ///   Node 0 = root
    ///   Composite children stored in ChildIndices array
    ///   All parameters packed into FlatNodeDef fields
    ///
    /// Usage:
    ///   var runtimeTree = behaviorTreeAsset.Compile();
    ///   var flatTree = FlatTreeCompiler.Compile(runtimeTree, Allocator.Persistent);
    ///   var scheduler = new BTTickScheduler(flatTree, bbSlots, actionSlots);
    ///   // ... use scheduler for 10000+ agents ...
    ///   flatTree.Dispose();
    /// </summary>
    public static class FlatTreeCompiler
    {
        /// <summary>
        /// Compile a managed RuntimeBehaviorTree into a flat, Burst-compatible representation.
        /// </summary>
        public static FlatBehaviorTree Compile(RuntimeBehaviorTree tree, Allocator allocator)
        {
            if (tree?.Root == null)
                return default;

            // Phase 1: collect all nodes via BFS and assign indices
            var nodeList = new List<RuntimeNode>(64);
            var childLinks = new List<int>(128);
            var nodeIndexMap = new Dictionary<RuntimeNode, int>(64);

            CollectNodes(tree.Root, nodeList, nodeIndexMap);

            int nodeCount = nodeList.Count;

            // Phase 2: build FlatNodeDef array and ChildIndices
            var nodeDefs = new FlatNodeDef[nodeCount];
            var childIndicesList = new List<int>(128);

            for (int i = 0; i < nodeCount; i++)
            {
                nodeDefs[i] = BuildNodeDef(nodeList[i], nodeIndexMap, childIndicesList);
            }

            // Phase 3: copy into NativeArrays
            var flatTree = new FlatBehaviorTree(nodeCount, childIndicesList.Count, allocator);

            for (int i = 0; i < nodeCount; i++)
                flatTree.Nodes[i] = nodeDefs[i];

            for (int i = 0; i < childIndicesList.Count; i++)
                flatTree.ChildIndices[i] = childIndicesList[i];

            return flatTree;
        }

        private static void CollectNodes(RuntimeNode node, List<RuntimeNode> list,
            Dictionary<RuntimeNode, int> indexMap)
        {
            if (node == null || indexMap.ContainsKey(node)) return;

            int idx = list.Count;
            list.Add(node);
            indexMap[node] = idx;

            // Unwrap root node
            if (node is RuntimeRootNode rootNode)
            {
                CollectNodes(rootNode.Child, list, indexMap);
                return;
            }

            if (node is RuntimeCompositeNode composite && composite.Children != null)
            {
                for (int i = 0; i < composite.Children.Length; i++)
                    CollectNodes(composite.Children[i], list, indexMap);
            }
            else if (node is RuntimeDecoratorNode decorator)
            {
                CollectNodes(decorator.Child, list, indexMap);
            }
        }

        private static FlatNodeDef BuildNodeDef(RuntimeNode node,
            Dictionary<RuntimeNode, int> indexMap, List<int> childIndices)
        {
            var def = new FlatNodeDef();

            // Handle RootNode as pass-through to its child
            if (node is RuntimeRootNode rootNode)
            {
                // Root acts as a single-child pass-through (like Succeeder but identity)
                def.Type = FlatNodeType.Succeeder;
                if (rootNode.Child != null && indexMap.TryGetValue(rootNode.Child, out int rootChildIdx))
                {
                    def.ChildStartIndex = childIndices.Count;
                    def.ChildCount = 1;
                    childIndices.Add(rootChildIdx);
                }
                return def;
            }

            if (node is RuntimeCompositeNode composite)
            {
                def.Type = MapCompositeType(composite);
                def.Flags = (byte)composite.AbortType;

                if (composite.Children != null && composite.Children.Length > 0)
                {
                    def.ChildStartIndex = childIndices.Count;
                    def.ChildCount = (byte)composite.Children.Length;
                    for (int i = 0; i < composite.Children.Length; i++)
                    {
                        if (indexMap.TryGetValue(composite.Children[i], out int ci))
                            childIndices.Add(ci);
                    }
                }

                // Parallel thresholds
                if (composite is RuntimeParallelAllNode parallelAll)
                {
                    def.SuccessThreshold = (short)parallelAll.SuccessThreshold;
                    def.FailureThreshold = (short)parallelAll.FailureThreshold;
                }

                return def;
            }

            if (node is RuntimeDecoratorNode decorator)
            {
                def.Type = MapDecoratorType(decorator);

                if (decorator.Child != null && indexMap.TryGetValue(decorator.Child, out int di))
                {
                    def.ChildStartIndex = childIndices.Count;
                    def.ChildCount = 1;
                    childIndices.Add(di);
                }

                // Pack decorator parameters
                if (decorator is RuntimeRepeatNode repeat)
                    def.ParamInt = repeat.RepeatCount;
                else if (decorator is RuntimeRetryNode retry)
                    def.ParamInt = retry.MaxAttempts;
                else if (decorator is RuntimeTimeoutNode timeout)
                    def.ParamFloat = timeout.TimeoutSeconds;
                else if (decorator is RuntimeDelayNode delay)
                    def.ParamFloat = delay.DelaySeconds;
                else if (decorator is RuntimeCoolDownNode coolDown)
                    def.ParamFloat = coolDown.CoolDown;

                return def;
            }

            // Leaf nodes default to ActionSlot
            def.Type = FlatNodeType.ActionSlot;
            def.ParamInt = 0; // Default action ID 0; override via custom mapping
            return def;
        }

        private static FlatNodeType MapCompositeType(RuntimeCompositeNode composite)
        {
            // Order matters: check more specific types before base types
            if (composite is RuntimeReactiveSequence)  return FlatNodeType.ReactiveSequence;
            if (composite is RuntimeReactiveFallback)   return FlatNodeType.ReactiveSelector;
            if (composite is RuntimeSequenceWithMemory) return FlatNodeType.Sequence;
            if (composite is RuntimeSequencer)          return FlatNodeType.Sequence;
            if (composite is RuntimeSelector)           return FlatNodeType.Selector;
            if (composite is RuntimeParallelAllNode)    return FlatNodeType.Parallel;
            if (composite is RuntimeParallelNode)       return FlatNodeType.Parallel;
            return FlatNodeType.Sequence;
        }

        private static FlatNodeType MapDecoratorType(RuntimeDecoratorNode decorator)
        {
            if (decorator is RuntimeInverterNode)      return FlatNodeType.Inverter;
            if (decorator is RuntimeRepeatNode)        return FlatNodeType.Repeater;
            if (decorator is RuntimeSucceederNode)     return FlatNodeType.Succeeder;
            if (decorator is RuntimeForceFailureNode)  return FlatNodeType.ForceFailure;
            if (decorator is RuntimeRetryNode)         return FlatNodeType.Retry;
            if (decorator is RuntimeTimeoutNode)       return FlatNodeType.Timeout;
            if (decorator is RuntimeDelayNode)         return FlatNodeType.Delay;
            if (decorator is RuntimeRunOnceNode)       return FlatNodeType.RunOnce;
            if (decorator is RuntimeCoolDownNode)      return FlatNodeType.CoolDown;
            return FlatNodeType.Succeeder;
        }
    }
}
