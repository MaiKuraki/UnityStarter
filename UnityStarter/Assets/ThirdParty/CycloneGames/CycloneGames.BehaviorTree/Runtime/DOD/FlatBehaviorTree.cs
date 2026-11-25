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
        Sequence,
        Selector,
        Parallel,
        ReactiveSequence,
        ReactiveSelector,

        Inverter,
        Repeater,
        Succeeder,
        ForceFailure,
        Retry,
        Timeout,
        Delay,
        RunOnce,
        CoolDown,

        ActionSlot,         // References an external action by ID
        BlackboardCondition,// Check a BB key against a constant
        WaitTicks,          // Idle for N ticks

        // Root sentinel appended last; node-type ordinals are part of the wire format.
        Root,
    }

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
    /// Fixed-size (28-byte) node definition stored inline in NativeArrays for Burst dispatch.
    /// Node 0 is the root; composite/decorator children are contiguous in ChildIndices.
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
    /// Shared, read-only flat NativeArray definition created once per template. This type is the
    /// sole owner of its native storage; schedulers hold a lease so storage cannot be released
    /// while a job can still read it.
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
