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
    ///   BTAgentState     = per-agent mutable execution state (node states + blackboard)
    ///
    /// For 10,000+ agents processing, schedule BTTickJob with this definition
    /// and a batch of BTAgentState arrays.
    /// </summary>
    public struct FlatBehaviorTree : System.IDisposable
    {
        /// <summary>Per-node definition. Index 0 = root.</summary>
        public NativeArray<FlatNodeDef> Nodes;

        /// <summary>
        /// Flattened child indices. Composite node i's children are:
        /// ChildIndices[Nodes[i].ChildStartIndex .. + Nodes[i].ChildCount]
        /// </summary>
        public NativeArray<int> ChildIndices;

        public int NodeCount;

        public bool IsCreated => Nodes.IsCreated;

        public FlatBehaviorTree(int nodeCount, int totalChildLinks, Allocator allocator)
        {
            NodeCount = nodeCount;
            Nodes = new NativeArray<FlatNodeDef>(nodeCount, allocator);
            ChildIndices = new NativeArray<int>(totalChildLinks, allocator);
        }

        public void Dispose()
        {
            if (Nodes.IsCreated) Nodes.Dispose();
            if (ChildIndices.IsCreated) ChildIndices.Dispose();
        }
    }
}
