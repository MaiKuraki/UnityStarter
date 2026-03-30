using Unity.Collections;
using Unity.Mathematics;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Action request lifecycle for Burst-compatible leaf nodes.
    /// BT writes Requested → external system picks up → sets Running →
    /// external system completes → sets Success/Failed → BT reads result next tick.
    /// </summary>
    public enum ActionRequestStatus : byte
    {
        Idle,
        Requested,
        Running,
        Success,
        Failed
    }

    /// <summary>
    /// Per-agent mutable execution state. Completely separated from the
    /// shared FlatBehaviorTree definition (flyweight pattern).
    ///
    /// Arrays are indexed by node index matching FlatBehaviorTree.Nodes.
    /// Blackboard arrays are indexed by slot index (user-defined mapping).
    ///
    /// Memory layout is SoA (Structure of Arrays) for cache-friendly
    /// Burst iteration across many agents.
    /// </summary>
    public struct BTAgentState : System.IDisposable
    {
        /// <summary>Current RuntimeState per node (stored as byte for Burst).</summary>
        public NativeArray<byte> NodeStates;

        /// <summary>Auxiliary int per node (composite: current child index; repeater: count; etc.)</summary>
        public NativeArray<int> AuxInt;

        /// <summary>Auxiliary float per node (timeout elapsed, cooldown remaining, etc.)</summary>
        public NativeArray<float> AuxFloat;

        // Flat blackboard channels (indexed by slot, not by hash key)
        public NativeArray<int> BBInts;
        public NativeArray<float> BBFloats;
        public NativeArray<byte> BBBools;
        public NativeArray<float3> BBFloat3s;
        public NativeArray<ulong> BBStamps;

        /// <summary>External action request slots (indexed by action ID).</summary>
        public NativeArray<ActionRequestStatus> ActionSlots;

        public ulong SequenceId;
        public int TickInterval;
        public int TickCountdown;

        public int NodeCount;
        public int BBSlotCount;
        public int ActionSlotCount;

        public bool IsCreated => NodeStates.IsCreated;

        public static BTAgentState Create(int nodeCount, int bbSlots, int actionSlots, Allocator allocator)
        {
            return new BTAgentState
            {
                NodeCount = nodeCount,
                BBSlotCount = bbSlots,
                ActionSlotCount = actionSlots,
                NodeStates = new NativeArray<byte>(nodeCount, allocator),
                AuxInt = new NativeArray<int>(nodeCount, allocator),
                AuxFloat = new NativeArray<float>(nodeCount, allocator),
                BBInts = new NativeArray<int>(bbSlots, allocator),
                BBFloats = new NativeArray<float>(bbSlots, allocator),
                BBBools = new NativeArray<byte>(bbSlots, allocator),
                BBFloat3s = new NativeArray<float3>(bbSlots, allocator),
                BBStamps = new NativeArray<ulong>(bbSlots, allocator),
                ActionSlots = new NativeArray<ActionRequestStatus>(actionSlots, allocator),
            };
        }

        public void Reset()
        {
            for (int i = 0; i < NodeCount; i++)
            {
                NodeStates[i] = (byte)RuntimeState.NotEntered;
                AuxInt[i] = 0;
                AuxFloat[i] = 0f;
            }
            for (int i = 0; i < ActionSlotCount; i++)
                ActionSlots[i] = ActionRequestStatus.Idle;
            SequenceId = 0;
            TickCountdown = 0;
        }

        /// <summary>
        /// FNV-1a hash of execution state for fast network desync detection.
        /// </summary>
        public uint ComputeStateHash()
        {
            const uint FNV_OFFSET = 2166136261u;
            const uint FNV_PRIME = 16777619u;
            uint hash = FNV_OFFSET;

            for (int i = 0; i < NodeCount; i++)
            {
                hash = (hash ^ NodeStates[i]) * FNV_PRIME;
                hash = (hash ^ (uint)AuxInt[i]) * FNV_PRIME;
            }
            for (int i = 0; i < BBSlotCount; i++)
            {
                hash = (hash ^ (uint)BBInts[i]) * FNV_PRIME;
            }
            return hash;
        }

        public void Dispose()
        {
            if (NodeStates.IsCreated) NodeStates.Dispose();
            if (AuxInt.IsCreated) AuxInt.Dispose();
            if (AuxFloat.IsCreated) AuxFloat.Dispose();
            if (BBInts.IsCreated) BBInts.Dispose();
            if (BBFloats.IsCreated) BBFloats.Dispose();
            if (BBBools.IsCreated) BBBools.Dispose();
            if (BBFloat3s.IsCreated) BBFloat3s.Dispose();
            if (BBStamps.IsCreated) BBStamps.Dispose();
            if (ActionSlots.IsCreated) ActionSlots.Dispose();
        }
    }
}
