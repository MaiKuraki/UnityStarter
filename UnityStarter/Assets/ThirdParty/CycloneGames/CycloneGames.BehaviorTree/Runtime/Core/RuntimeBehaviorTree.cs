using System.Collections.Generic;
using System.Threading;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public class RuntimeBehaviorTree : IRuntimeBTContext
    {
        public RuntimeNode Root { get; private set; }
        public RuntimeBlackboard Blackboard { get; private set; }
        public RuntimeState State { get; private set; } = RuntimeState.NotEntered;
        public bool IsStopped { get; private set; }

        public RuntimeBTContext Context { get; private set; }

        public GameObject OwnerGameObject => Context?.OwnerGameObject;
        public IRuntimeBTServiceResolver ServiceResolver => Context?.ServiceResolver;

        public int TickInterval { get; set; } = 1;
        private int _tickCounter = 0;

        // Event-driven execution support
        private int _wakeUpRequested;
        private int _wakeUpTickBudget;
        public bool HasWakeUpRequest => Volatile.Read(ref _wakeUpRequested) != 0;
        public int WakeUpTickBudget => Volatile.Read(ref _wakeUpTickBudget);

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

            Root?.OnAwake();
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
        internal void RequestWakeUp(int boostedTicks = 1)
        {
            if (boostedTicks < 1)
            {
                boostedTicks = 1;
            }

            Volatile.Write(ref _wakeUpRequested, 1);
            SetMaxWakeUpTickBudget(boostedTicks);
        }

        /// <summary>
        /// Public wake-up entry for external systems such as perception, damage, or network events.
        /// boostedTicks keeps the tree at immediate cadence for a short burst.
        /// </summary>
        public void WakeUp(int boostedTicks = 1)
        {
            RequestWakeUp(boostedTicks);
        }

        /// <summary>
        /// Consumes the wake-up flag. Returns true if a wake-up was requested.
        /// </summary>
        public bool ConsumeWakeUp()
        {
            return Interlocked.Exchange(ref _wakeUpRequested, 0) != 0;
        }

        public bool ShouldTick()
        {
            if (IsStopped)
            {
                return false;
            }

            bool wakeUpRequested = ConsumeWakeUp();
            bool hasWakeUpBudget = ConsumeWakeUpBudget();
            if (wakeUpRequested || hasWakeUpBudget)
            {
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
            if (IsStopped)
            {
                return State;
            }

            if (Root == null) return RuntimeState.Failure;

            State = Root.Run(Blackboard);
            return State;
        }

        public void Stop()
        {
            if (IsStopped) return;

            if (Root != null && Root.IsStarted)
            {
                Root.Abort(Blackboard);
            }

            Interlocked.Exchange(ref _wakeUpRequested, 0);
            Interlocked.Exchange(ref _wakeUpTickBudget, 0);
            _tickCounter = 0;
            State = RuntimeState.NotEntered;
            IsStopped = true;
        }

        public void Play()
        {
            Interlocked.Exchange(ref _wakeUpRequested, 0);
            Interlocked.Exchange(ref _wakeUpTickBudget, 0);
            _tickCounter = 0;
            State = RuntimeState.NotEntered;
            IsStopped = false;
        }

        private void SetMaxWakeUpTickBudget(int boostedTicks)
        {
            while (true)
            {
                int current = Volatile.Read(ref _wakeUpTickBudget);
                if (boostedTicks <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _wakeUpTickBudget, boostedTicks, current) == current)
                {
                    return;
                }
            }
        }

        private bool ConsumeWakeUpBudget()
        {
            while (true)
            {
                int current = Volatile.Read(ref _wakeUpTickBudget);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _wakeUpTickBudget, current - 1, current) == current)
                {
                    return true;
                }
            }
        }

        public void Dispose()
        {
            Stop();
            Blackboard?.Dispose();
        }
    }
}
