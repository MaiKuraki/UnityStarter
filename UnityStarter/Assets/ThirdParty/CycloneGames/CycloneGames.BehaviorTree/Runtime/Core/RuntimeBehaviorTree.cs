using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes;
using CycloneGames.BehaviorTree.Runtime.Core.Nodes.Decorators;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public readonly struct RuntimeBehaviorTreeLimits
    {
        /// <summary>Default maximum runtime nodes accepted during cold-path graph validation.</summary>
        public const int DEFAULT_MAX_NODE_COUNT = 4096;
        /// <summary>Default maximum runtime graph depth.</summary>
        public const int DEFAULT_MAX_DEPTH = 256;
        /// <summary>Implementation safety ceiling for runtime node count.</summary>
        public const int HARD_MAX_NODE_COUNT = 65536;
        /// <summary>Implementation safety ceiling for recursive node lifecycle depth.</summary>
        public const int HARD_MAX_DEPTH = 256;

        public RuntimeBehaviorTreeLimits(int maxNodeCount, int maxDepth)
        {
            if (maxNodeCount <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxNodeCount));
            }

            if (maxNodeCount > HARD_MAX_NODE_COUNT)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxNodeCount),
                    maxNodeCount,
                    $"Runtime node count cannot exceed the hard safety limit of {HARD_MAX_NODE_COUNT}.");
            }

            if (maxDepth <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxDepth));
            }

            if (maxDepth > HARD_MAX_DEPTH)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(maxDepth),
                    maxDepth,
                    $"Runtime depth cannot exceed the hard safety limit of {HARD_MAX_DEPTH}.");
            }

            MaxNodeCount = maxNodeCount;
            MaxDepth = maxDepth;
        }

        public int MaxNodeCount { get; }
        public int MaxDepth { get; }

        public static RuntimeBehaviorTreeLimits Default =>
            new RuntimeBehaviorTreeLimits(DEFAULT_MAX_NODE_COUNT, DEFAULT_MAX_DEPTH);

        internal RuntimeBehaviorTreeLimits Normalize()
        {
            return MaxNodeCount > 0 && MaxDepth > 0 ? this : Default;
        }
    }

    /// <summary>
    /// Owns one managed behavior-tree instance and its blackboard.
    /// Lifecycle operations are single-owner-thread operations. WakeUp is the explicit
    /// thread-safe producer entry point for external callbacks.
    /// </summary>
    public class RuntimeBehaviorTree : IRuntimeBTContext, IDisposable
    {
        private const int DISPOSED_WAKE_STATE = -1;

        private readonly int _ownerThreadId;
        private int _tickInterval = 1;
        private int _tickCounter;
        private int _wakeUpState;
        private int _disposeState;
        private bool _terminationPublished;
        private bool _lifecycleOperationActive;

        public RuntimeNode Root { get; }
        public RuntimeBlackboard Blackboard { get; }
        public RuntimeState State { get; private set; } = RuntimeState.NotEntered;
        public bool IsStopped { get; private set; }
        public bool IsDisposed => Volatile.Read(ref _disposeState) != 0;
        public RuntimeBTContext Context { get; private set; }
        public RuntimeBehaviorTreeLimits Limits { get; }

        public IRuntimeBTServiceResolver ServiceResolver => Context?.ServiceResolver;

        public int TickInterval
        {
            get => _tickInterval;
            set
            {
                EnsureOwnerThread();
                ThrowIfDisposed();
                ThrowIfLifecycleOperationActive(nameof(TickInterval));
                if (value < 1)
                {
                    throw new ArgumentOutOfRangeException(nameof(value), value, "Tick interval must be at least one.");
                }

                _tickInterval = value;
                _tickCounter = 0;
            }
        }

        public bool HasWakeUpRequest => Volatile.Read(ref _wakeUpState) > 0;
        public int WakeUpTickBudget
        {
            get
            {
                int state = Volatile.Read(ref _wakeUpState);
                return state > 0 ? state : 0;
            }
        }

        /// <summary>
        /// Published exactly once when the current activation completes, faults, or is explicitly stopped.
        /// Success and Failure are terminal results; NotEntered represents an explicit Stop.
        /// Play starts a new activation and resets the notification gate.
        /// </summary>
        public event Action<RuntimeState> Terminated;
        internal event Action<RuntimeBehaviorTree> WakeUpRequested;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        private readonly Dictionary<string, RuntimeNode> _nodeMap = new Dictionary<string, RuntimeNode>();

        public BTStatusLogger StatusLogger { get; set; }
#endif

        public RuntimeBehaviorTree(
            RuntimeNode root,
            RuntimeBlackboard blackboard,
            RuntimeBTContext context = null,
            RuntimeBehaviorTreeLimits limits = default)
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;
            Root = root ?? throw new ArgumentNullException(nameof(root));
            bool ownsBlackboard = blackboard == null;
            Blackboard = blackboard ?? new RuntimeBlackboard();
            Limits = limits.Normalize();

            if (Blackboard.IsDisposed)
            {
                throw new ObjectDisposedException(nameof(blackboard));
            }

            IRuntimeBTContext previousBlackboardContext = Blackboard.Context;
            List<RuntimeNode> ownedNodes = null;
            bool awakeAttempted = false;
            try
            {
                ownedNodes = ValidateNodeGraph(root, Limits);
                CommitNodeGraph(ownedNodes);
                SetContextCore(context ?? new RuntimeBTContext());
                awakeAttempted = true;
                root?.OnAwake();
            }
            catch (Exception initializationException)
            {
                Exception cleanupException = null;

                if (awakeAttempted && ownedNodes != null)
                {
                    for (int i = 0; i < ownedNodes.Count; i++)
                    {
                        try
                        {
                            ownedNodes[i].DisposeNode(Blackboard);
                        }
                        catch (Exception exception)
                        {
                            cleanupException = CombineExceptions(cleanupException, exception);
                        }
                    }
                }

                if (ownedNodes != null)
                {
                    for (int i = 0; i < ownedNodes.Count; i++)
                    {
                        ownedNodes[i].OwnerTree = null;
                    }
                }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                _nodeMap.Clear();
#endif

                Context = null;
                if (ownsBlackboard)
                {
                    try
                    {
                        Blackboard.Dispose();
                    }
                    catch (Exception exception)
                    {
                        cleanupException = CombineExceptions(cleanupException, exception);
                    }
                }
                else if (!Blackboard.IsDisposed)
                {
                    try
                    {
                        Blackboard.Context = previousBlackboardContext;
                    }
                    catch (Exception exception)
                    {
                        cleanupException = CombineExceptions(cleanupException, exception);
                    }
                }

                if (cleanupException != null)
                {
                    throw new AggregateException(initializationException, cleanupException);
                }

                ExceptionDispatchInfo.Capture(initializationException).Throw();
                throw;
            }
        }

        public T GetOwner<T>() where T : class
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return Context?.GetOwner<T>();
        }

        public T GetService<T>() where T : class
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return Context?.GetService<T>();
        }

        public void SetContext(RuntimeBTContext context)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            ThrowIfLifecycleOperationActive(nameof(SetContext));
            if (Root != null && Root.IsStarted)
            {
                throw new InvalidOperationException("Context cannot change while the tree has an active node stack.");
            }
            SetContextCore(context ?? new RuntimeBTContext());
        }

        /// <summary>
        /// Requests an immediate tick from any producer thread.
        /// boostedTicks keeps the tree at immediate cadence for a bounded number of owner-thread ticks.
        /// </summary>
        public void WakeUp(int boostedTicks = 1)
        {
            RequestWakeUp(boostedTicks);
        }

        public bool ConsumeWakeUp()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            return ConsumeWakeUpTick();
        }

        public bool ShouldTick()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            ThrowIfLifecycleOperationActive(nameof(ShouldTick));

            if (IsStopped)
            {
                return false;
            }

            if (ConsumeWakeUpTick())
            {
                _tickCounter = 0;
                return true;
            }

            if (_tickInterval <= 1)
            {
                return true;
            }

            _tickCounter++;
            if (_tickCounter < _tickInterval)
            {
                return false;
            }

            _tickCounter = 0;
            return true;
        }

        public RuntimeState Tick()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnterLifecycleOperation(nameof(Tick));

            try
            {
                return TickCore();
            }
            finally
            {
                _lifecycleOperationActive = false;
            }
        }

        private RuntimeState TickCore()
        {

            if (IsStopped)
            {
                return State;
            }

            try
            {
                State = Root != null ? Root.Run(Blackboard) : RuntimeState.Failure;
            }
            catch (Exception executionException)
            {
                State = RuntimeState.Failure;
                IsStopped = true;
                ResetSchedulingState();

                try
                {
                    PublishTerminated(State);
                }
                catch (Exception notificationException)
                {
                    throw new AggregateException(executionException, notificationException);
                }

                ExceptionDispatchInfo.Capture(executionException).Throw();
                throw;
            }

            if (State == RuntimeState.Success || State == RuntimeState.Failure)
            {
                IsStopped = true;
                ResetSchedulingState();
                PublishTerminated(State);
            }

            return State;
        }

        public void Stop()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnterLifecycleOperation(nameof(Stop));

            try
            {
                StopCore();
            }
            finally
            {
                _lifecycleOperationActive = false;
            }
        }

        private void StopCore()
        {
            if (IsStopped)
            {
                return;
            }

            try
            {
                if (Root != null && Root.IsStarted)
                {
                    Root.Abort(Blackboard);
                }
            }
            catch (Exception abortException)
            {
                State = RuntimeState.Failure;
                IsStopped = true;
                ResetSchedulingState();

                try
                {
                    PublishTerminated(State);
                }
                catch (Exception notificationException)
                {
                    throw new AggregateException(abortException, notificationException);
                }

                ExceptionDispatchInfo.Capture(abortException).Throw();
                throw;
            }

            State = RuntimeState.NotEntered;
            IsStopped = true;
            ResetSchedulingState();
            PublishTerminated(State);
        }

        public void Play()
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            EnterLifecycleOperation(nameof(Play));

            try
            {
                PlayCore();
            }
            finally
            {
                _lifecycleOperationActive = false;
            }
        }

        private void PlayCore()
        {
            if (!IsStopped)
            {
                return;
            }

            _terminationPublished = false;
            try
            {
                Root?.Reset(Blackboard);
            }
            catch
            {
                State = RuntimeState.Failure;
                IsStopped = true;
                ResetSchedulingState();
                PublishTerminated(State);
                throw;
            }

            State = RuntimeState.NotEntered;
            IsStopped = false;
            ResetSchedulingState();
        }

        public void Dispose()
        {
            if (IsDisposed)
            {
                return;
            }

            EnsureOwnerThread();
            EnterLifecycleOperation(nameof(Dispose));
            Exception pendingException = null;

            try
            {
                Interlocked.Exchange(ref _wakeUpState, DISPOSED_WAKE_STATE);

                if (!IsStopped)
                {
                    try
                    {
                        StopCore();
                    }
                    catch (Exception exception)
                    {
                        pendingException = exception;
                    }
                }

                Interlocked.Exchange(ref _disposeState, 1);

                try
                {
                    Root?.DisposeNode(Blackboard);
                }
                catch (Exception exception)
                {
                    pendingException = pendingException == null
                        ? exception
                        : new AggregateException(pendingException, exception);
                }

                try
                {
                    Blackboard.Dispose();
                }
                catch (Exception exception)
                {
                    pendingException = pendingException == null
                        ? exception
                        : new AggregateException(pendingException, exception);
                }

                Terminated = null;
                WakeUpRequested = null;
                ResetSchedulingState();

                if (pendingException != null)
                {
                    ExceptionDispatchInfo.Capture(pendingException).Throw();
                }
            }
            finally
            {
                _lifecycleOperationActive = false;
            }
        }

        internal void RequestWakeUp(int boostedTicks = 1)
        {
            if (boostedTicks < 1)
            {
                boostedTicks = 1;
            }

            while (true)
            {
                int current = Volatile.Read(ref _wakeUpState);
                if (current == DISPOSED_WAKE_STATE || IsDisposed || boostedTicks <= current)
                {
                    return;
                }

                if (Interlocked.CompareExchange(ref _wakeUpState, boostedTicks, current) != current)
                {
                    continue;
                }

                if (current == 0)
                {
                    WakeUpRequested?.Invoke(this);
                }

                return;
            }
        }

#if UNITY_EDITOR || DEVELOPMENT_BUILD
        public RuntimeNode GetNodeByGUID(string guid)
        {
            EnsureOwnerThread();
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(guid))
            {
                return null;
            }

            _nodeMap.TryGetValue(guid, out RuntimeNode node);
            return node;
        }
#endif

        private static List<RuntimeNode> ValidateNodeGraph(
            RuntimeNode root,
            RuntimeBehaviorTreeLimits limits)
        {
            var orderedNodes = new List<RuntimeNode>();
            if (root == null)
            {
                return orderedNodes;
            }

            var visited = new HashSet<RuntimeNode>(RuntimeNodeReferenceComparer.Instance);
            var runtimeGuids = new HashSet<string>(StringComparer.Ordinal);
            var pending = new Stack<RuntimeNodeTraversalFrame>();
            pending.Push(new RuntimeNodeTraversalFrame(root, 1));

            while (pending.Count > 0)
            {
                RuntimeNodeTraversalFrame frame = pending.Pop();
                RuntimeNode node = frame.Node;
                if (frame.Depth > limits.MaxDepth)
                {
                    throw new InvalidOperationException(
                        $"Runtime node graph depth exceeds the configured limit of {limits.MaxDepth}.");
                }

                if (orderedNodes.Count >= limits.MaxNodeCount)
                {
                    throw new InvalidOperationException(
                        $"Runtime node graph exceeds the configured node limit of {limits.MaxNodeCount}.");
                }

                if (!visited.Add(node))
                {
                    throw new InvalidOperationException(
                        "Runtime node graphs must be acyclic trees with a single parent for every node.");
                }

                if (node.IsDisposed)
                {
                    throw new ObjectDisposedException(
                        node.GetType().FullName,
                        "A disposed runtime node cannot be attached to another tree.");
                }

                if (node.OwnerTree != null)
                {
                    throw new InvalidOperationException("A runtime node cannot be owned by more than one tree.");
                }

                if (!string.IsNullOrEmpty(node.GUID) && !runtimeGuids.Add(node.GUID))
                {
                    throw new InvalidOperationException(
                        $"Runtime node GUID '{node.GUID}' is used by more than one node.");
                }

                node.ValidateSetupNode();
                orderedNodes.Add(node);

                if (node is RuntimeCompositeNode composite)
                {
                    IReadOnlyList<RuntimeNode> children = composite.Children;
                    for (int i = children.Count - 1; i >= 0; i--)
                    {
                        RuntimeNode child = children[i];
                        if (child == null)
                        {
                            throw new InvalidOperationException(
                                "Runtime composite children cannot contain null nodes.");
                        }

                        pending.Push(new RuntimeNodeTraversalFrame(child, frame.Depth + 1));
                    }
                }
                else if (node is RuntimeDecoratorNode decorator)
                {
                    if (decorator.Child != null)
                    {
                        pending.Push(new RuntimeNodeTraversalFrame(decorator.Child, frame.Depth + 1));
                    }
                }
                else if (node is RuntimeRootNode rootNode && rootNode.Child != null)
                {
                    pending.Push(new RuntimeNodeTraversalFrame(rootNode.Child, frame.Depth + 1));
                }
            }

            return orderedNodes;
        }

        private void CommitNodeGraph(List<RuntimeNode> orderedNodes)
        {
            for (int i = 0; i < orderedNodes.Count; i++)
            {
                if (orderedNodes[i] is RuntimeCompositeNode composite && !composite.IsSealed)
                {
                    composite.Seal();
                }
            }

            for (int i = 0; i < orderedNodes.Count; i++)
            {
                RuntimeNode node = orderedNodes[i];
                node.OwnerTree = this;

#if UNITY_EDITOR || DEVELOPMENT_BUILD
                if (!string.IsNullOrEmpty(node.GUID))
                {
                    _nodeMap[node.GUID] = node;
                }
#endif
            }
        }

        private void SetContextCore(RuntimeBTContext context)
        {
            Context = context;
            Blackboard.Context = context;
        }

        private void PublishTerminated(RuntimeState terminalState)
        {
            if (_terminationPublished)
            {
                return;
            }

            _terminationPublished = true;
            Terminated?.Invoke(terminalState);
        }

        private void ResetSchedulingState()
        {
            while (true)
            {
                int current = Volatile.Read(ref _wakeUpState);
                if (current == DISPOSED_WAKE_STATE ||
                    Interlocked.CompareExchange(ref _wakeUpState, 0, current) == current)
                {
                    break;
                }
            }

            _tickCounter = 0;
        }

        private bool ConsumeWakeUpTick()
        {
            while (true)
            {
                int current = Volatile.Read(ref _wakeUpState);
                if (current <= 0)
                {
                    return false;
                }

                if (Interlocked.CompareExchange(ref _wakeUpState, current - 1, current) == current)
                {
                    return true;
                }
            }
        }

        private static Exception CombineExceptions(Exception current, Exception next)
        {
            return current == null ? next : new AggregateException(current, next);
        }

        private void EnsureOwnerThread([CallerMemberName] string operation = null)
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"RuntimeBehaviorTree.{operation} must run on owner thread {_ownerThreadId}.");
            }
        }

        private void EnterLifecycleOperation(string operation)
        {
            if (_lifecycleOperationActive)
            {
                throw new InvalidOperationException(
                    $"RuntimeBehaviorTree.{operation} cannot run reentrantly during another lifecycle operation.");
            }

            _lifecycleOperationActive = true;
        }

        private void ThrowIfLifecycleOperationActive(string operation)
        {
            if (_lifecycleOperationActive)
            {
                throw new InvalidOperationException(
                    $"RuntimeBehaviorTree.{operation} cannot run reentrantly during another lifecycle operation.");
            }
        }

        private void ThrowIfDisposed()
        {
            if (IsDisposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeBehaviorTree));
            }
        }

        private sealed class RuntimeNodeReferenceComparer : IEqualityComparer<RuntimeNode>
        {
            public static readonly RuntimeNodeReferenceComparer Instance = new RuntimeNodeReferenceComparer();

            public bool Equals(RuntimeNode x, RuntimeNode y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(RuntimeNode obj)
            {
                return RuntimeHelpers.GetHashCode(obj);
            }
        }

        private readonly struct RuntimeNodeTraversalFrame
        {
            public RuntimeNodeTraversalFrame(RuntimeNode node, int depth)
            {
                Node = node;
                Depth = depth;
            }

            public RuntimeNode Node { get; }
            public int Depth { get; }
        }
    }
}
