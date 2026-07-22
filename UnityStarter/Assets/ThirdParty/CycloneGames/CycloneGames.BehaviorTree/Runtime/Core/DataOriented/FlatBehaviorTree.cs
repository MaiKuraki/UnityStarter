using System;
using Unity.Collections;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Node type enum for Burst-compatible dispatch.
    /// Each type maps to a fixed execution pattern — no virtual calls.
    /// </summary>
    public enum FlatNodeType : byte
    {
        // Composites
        Sequence,
        Selector,
        Parallel,
        ReactiveSequence,
        ReactiveSelector,

        // Decorators
        Inverter,
        Repeater,
        Succeeder,
        ForceFailure,
        Retry,
        Timeout,
        Delay,
        RunOnce,
        CoolDown,

        // Leaf nodes
        ActionSlot,         // References an external action by ID
        BlackboardCondition,// Check a BB key against a constant
        WaitTicks,          // Idle for N ticks

        // Root identity node. Appended to preserve the numeric values of existing node types.
        Root,
    }

    /// <summary>
    /// Comparison operator for BlackboardCondition nodes.
    /// </summary>
    public enum CompareOp : byte
    {
        Equal,
        NotEqual,
        Less,
        LessEqual,
        Greater,
        GreaterEqual
    }

    /// <summary>
    /// Compact node definition (28 bytes). Fixed-size for NativeArray storage.
    /// All data needed for Burst-compiled dispatch is stored inline.
    ///
    /// Architecture: FlatBehaviorTree stores an array of these structs.
    /// Node 0 is always the root. Children of composite/decorator nodes
    /// are stored contiguously in a separate ChildIndices array.
    /// </summary>
    public struct FlatNodeDef
    {
        public const byte PARALLEL_EMPTY_IS_FAILURE = 1 << 7;

        public FlatNodeType Type;
        public byte ChildCount;
        /// <summary>Packed flags. Bit0-1: AbortType (None/Self/LowerPriority/Both).</summary>
        public byte Flags;
        public CompareOp Compare;
        /// <summary>Start index into FlatBehaviorTree.ChildIndices for this node's children.</summary>
        public int ChildStartIndex;
        /// <summary>Multi-purpose int: repeat count, action ID, wait tick count, etc.</summary>
        public int ParamInt;
        /// <summary>Blackboard key hash for condition/action nodes.</summary>
        public int BBKey;
        /// <summary>Multi-purpose float: timeout seconds, cooldown, delay.</summary>
        public float ParamFloat;
        /// <summary>Constant to compare against for BlackboardCondition.</summary>
        public int CompareValue;
        /// <summary>For Parallel: how many children must succeed.</summary>
        public short SuccessThreshold;
        /// <summary>For Parallel: how many children must fail to return failure.</summary>
        public short FailureThreshold;
    }

    /// <summary>
    /// Shared, read-only BehaviorTree definition stored as flat NativeArrays.
    /// Created once per BT template; shared across all agents using that template.
    ///
    /// Flyweight pattern:
    ///   FlatBehaviorTree = shared immutable structure (tree topology + parameters)
    ///   BTTickScheduler  = per-agent mutable execution state (node states + blackboard)
    ///
    /// This reference type is the sole owner of its native storage. Schedulers acquire
    /// an internal lease so the storage cannot be released while a job can still read it.
    /// </summary>
    public sealed class FlatBehaviorTree : IDisposable
    {
        /// <summary>
        /// Allocation-free read-only projection over native storage. The projection does not
        /// own the storage and must not outlive its <see cref="FlatBehaviorTree"/> owner.
        /// </summary>
        public sealed class ReadOnlyBuffer<T>
            where T : struct
        {
            private readonly FlatBehaviorTree _owner;
            private readonly NativeArray<T> _storage;

            internal ReadOnlyBuffer(FlatBehaviorTree owner, NativeArray<T> storage)
            {
                _owner = owner;
                _storage = storage;
            }

            public int Length
            {
                get
                {
                    _owner.EnsureOwnerAccess();
                    return _storage.Length;
                }
            }

            public T this[int index]
            {
                get
                {
                    _owner.EnsureOwnerAccess();
                    return _storage[index];
                }
            }
        }

        private NativeArray<FlatNodeDef> _nodes;
        private NativeArray<int> _childIndices;
        private readonly ReadOnlyBuffer<FlatNodeDef> _nodeView;
        private readonly ReadOnlyBuffer<int> _childIndexView;
        private readonly int _ownerThreadId;
        private int _schedulerLeaseCount;
        private bool _disposed;

        /// <summary>Per-node definitions. Index 0 is the root.</summary>
        public ReadOnlyBuffer<FlatNodeDef> Nodes
        {
            get
            {
                EnsureOwnerAccess();
                return _nodeView;
            }
        }

        /// <summary>
        /// Flattened child indices. Composite node i's children are:
        /// ChildIndices[Nodes[i].ChildStartIndex .. + Nodes[i].ChildCount]
        /// </summary>
        public ReadOnlyBuffer<int> ChildIndices
        {
            get
            {
                EnsureOwnerAccess();
                return _childIndexView;
            }
        }

        public int NodeCount { get; }

        public bool IsCreated
        {
            get
            {
                EnsureOwnerThread();
                return !_disposed && _nodes.IsCreated && _childIndices.IsCreated;
            }
        }

        /// <summary>
        /// Creates an immutable, persistent native definition by copying managed source buffers.
        /// The owner must be disposed after every scheduler lease has been released.
        /// </summary>
        public FlatBehaviorTree(
            FlatNodeDef[] nodes,
            int[] childIndices)
        {
            if (nodes == null)
            {
                throw new ArgumentNullException(nameof(nodes));
            }

            if (nodes.Length == 0)
            {
                throw new ArgumentException("A flat tree must contain at least one node.", nameof(nodes));
            }

            if (childIndices == null)
            {
                throw new ArgumentNullException(nameof(childIndices));
            }

            _ownerThreadId = Environment.CurrentManagedThreadId;
            NodeCount = nodes.Length;
            _nodes = default;
            _childIndices = default;
            _nodeView = null;
            _childIndexView = null;
            _schedulerLeaseCount = 0;
            _disposed = false;

            try
            {
                _nodes = new NativeArray<FlatNodeDef>(nodes, Allocator.Persistent);
                _childIndices = new NativeArray<int>(childIndices, Allocator.Persistent);
                _nodeView = new ReadOnlyBuffer<FlatNodeDef>(this, _nodes);
                _childIndexView = new ReadOnlyBuffer<int>(this, _childIndices);
            }
            catch
            {
                DisposeStorage();
                _disposed = true;
                throw;
            }
        }

        internal NativeArray<FlatNodeDef> NodeStorage
        {
            get
            {
                EnsureOwnerAccess();
                return _nodes;
            }
        }

        internal NativeArray<int> ChildIndexStorage
        {
            get
            {
                EnsureOwnerAccess();
                return _childIndices;
            }
        }

        internal void AcquireSchedulerLease()
        {
            EnsureOwnerAccess();
            _schedulerLeaseCount = checked(_schedulerLeaseCount + 1);
        }

        internal void ReleaseSchedulerLease()
        {
            EnsureOwnerThread();
            if (_schedulerLeaseCount <= 0)
            {
                throw new InvalidOperationException("Flat tree scheduler lease accounting is unbalanced.");
            }

            _schedulerLeaseCount--;
        }

        public void Dispose()
        {
            EnsureOwnerThread();
            if (_disposed)
            {
                return;
            }

            if (_schedulerLeaseCount != 0)
            {
                throw new InvalidOperationException(
                    "Cannot dispose a flat tree while one or more schedulers still use it.");
            }

            DisposeStorage();
            _disposed = true;
        }

        private void EnsureOwnerAccess()
        {
            EnsureOwnerThread();
            if (_disposed || !_nodes.IsCreated || !_childIndices.IsCreated)
            {
                throw new ObjectDisposedException(nameof(FlatBehaviorTree));
            }
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"FlatBehaviorTree must be accessed from owner thread {_ownerThreadId}.");
            }
        }

        private void DisposeStorage()
        {
            if (_nodes.IsCreated)
            {
                _nodes.Dispose();
            }

            if (_childIndices.IsCreated)
            {
                _childIndices.Dispose();
            }
        }
    }
}
