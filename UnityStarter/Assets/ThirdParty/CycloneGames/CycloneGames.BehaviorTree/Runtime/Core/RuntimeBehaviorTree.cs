using System.Collections.Generic;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public class RuntimeBehaviorTree
    {
        public RuntimeNode Root { get; private set; }
        public RuntimeBlackboard Blackboard { get; private set; }
        public RuntimeState State { get; private set; } = RuntimeState.NotEntered;
        
        // Optional: Support for binding to a Unity GameObject if needed (kept generic as object)
        public object Owner { get; private set; }

        public RuntimeBehaviorTree(RuntimeNode root, RuntimeBlackboard blackboard, object owner = null)
        {
            Root = root;
            Blackboard = blackboard;
            Owner = owner;
#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BuildNodeMap(Root);
#endif
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly Dictionary<string, RuntimeNode> _nodeMap = new Dictionary<string, RuntimeNode>();
        
        public RuntimeNode GetNodeByGUID(string guid)
        {
            if (string.IsNullOrEmpty(guid)) return null;
            _nodeMap.TryGetValue(guid, out var node);
            return node;
        }

        private void BuildNodeMap(RuntimeNode node)
        {
            if (node == null) return;
            if (!string.IsNullOrEmpty(node.GUID))
            {
                _nodeMap[node.GUID] = node;
            }

            // Traverse Composite children
            if (node is RuntimeCompositeNode composite)
            {
                foreach (var child in composite.Children)
                {
                    BuildNodeMap(child);
                }
            }
            
            // Traverse Decorator child
            if (node is Nodes.Decorators.RuntimeDecoratorNode decorator)
            {
                BuildNodeMap(decorator.Child);
            }
            
            // Traverse RootNode child
            if (node is Nodes.RuntimeRootNode root)
            {
                BuildNodeMap(root.Child);
            }
        }
#endif

        public RuntimeState Tick()
        {
            if (Root == null) return RuntimeState.Failure;

            State = Root.Run(Blackboard);
            return State;
        }
        
        public void Stop()
        {
            if (Root != null && Root.IsStarted)
            {
                Root.Abort(Blackboard);
            }
            State = RuntimeState.NotEntered;
        }
    }
}
