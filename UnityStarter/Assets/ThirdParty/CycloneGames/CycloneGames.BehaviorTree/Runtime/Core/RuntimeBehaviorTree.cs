using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public class RuntimeBehaviorTree : IRuntimeBTContext
    {
        public RuntimeNode Root { get; private set; }
        public RuntimeBlackboard Blackboard { get; private set; }
        public RuntimeState State { get; private set; } = RuntimeState.NotEntered;

        public RuntimeBTContext Context { get; private set; }

        public GameObject OwnerGameObject => Context?.OwnerGameObject;
        public IRuntimeBTServiceResolver ServiceResolver => Context?.ServiceResolver;

        public int TickInterval { get; set; } = 1;
        private int _tickCounter = 0;

        // Event-driven execution support
        private volatile bool _wakeUpRequested;
        public bool HasWakeUpRequest => _wakeUpRequested;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public BTStatusLogger StatusLogger { get; set; }
#endif

        public RuntimeBehaviorTree(RuntimeNode root, RuntimeBlackboard blackboard, RuntimeBTContext context = null)
        {
            Root = root;
            Blackboard = blackboard;
            Context = context ?? new RuntimeBTContext();

            if (Blackboard != null)
            {
                Blackboard.Context = Context;
            }

            PropagateOwnerTree(Root);

#if UNITY_EDITOR || DEVELOPMENT_BUILD
            BuildNodeMap(Root);
#endif
        }

        // Propagate owner tree reference to all nodes for wake-up signaling
        private void PropagateOwnerTree(RuntimeNode node)
        {
            if (node == null) return;
            node.OwnerTree = this;

            if (node is RuntimeCompositeNode composite && composite.Children != null)
            {
                for (int i = 0; i < composite.Children.Length; i++)
                    PropagateOwnerTree(composite.Children[i]);
            }
            else if (node is Nodes.Decorators.RuntimeDecoratorNode decorator)
            {
                PropagateOwnerTree(decorator.Child);
            }
            else if (node is Nodes.RuntimeRootNode rootNode)
            {
                PropagateOwnerTree(rootNode.Child);
            }
            else if (node is Nodes.Decorators.RuntimeSubTreeNode subTree)
            {
                PropagateOwnerTree(subTree.Child);
            }
        }

        public T GetOwner<T>() where T : class
        {
            return Context != null ? Context.GetOwner<T>() : null;
        }

        public T GetService<T>() where T : class
        {
            return Context != null ? Context.GetService<T>() : null;
        }

        /// <summary>
        /// Called by nodes via EmitWakeUpSignal() to request the next tick.
        /// Thread-safe (volatile flag).
        /// </summary>
        internal void RequestWakeUp()
        {
            _wakeUpRequested = true;
        }

        /// <summary>
        /// Consumes the wake-up flag. Returns true if a wake-up was requested.
        /// </summary>
        public bool ConsumeWakeUp()
        {
            if (!_wakeUpRequested) return false;
            _wakeUpRequested = false;
            return true;
        }

        public bool ShouldTick()
        {
            if (_wakeUpRequested)
            {
                _wakeUpRequested = false;
                _tickCounter = 0;
                return true;
            }

            if (TickInterval <= 1) return true;
            _tickCounter++;
            if (_tickCounter >= TickInterval)
            {
                _tickCounter = 0;
                return true;
            }
            return false;
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

            if (node is RuntimeCompositeNode composite)
            {
                var children = composite.Children;
                if (children != null)
                {
                    for (int i = 0; i < children.Length; i++)
                    {
                        BuildNodeMap(children[i]);
                    }
                }
            }

            if (node is Nodes.Decorators.RuntimeDecoratorNode decorator)
            {
                BuildNodeMap(decorator.Child);
            }

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
            Blackboard?.Dispose();
            State = RuntimeState.NotEntered;
        }
    }
}
