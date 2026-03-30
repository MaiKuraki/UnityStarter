using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using UnityEngine;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// High-level scheduler that manages thousands of agents sharing the same
    /// FlatBehaviorTree template. Handles NativeArray lifecycle, job scheduling,
    /// and provides a clean API for adding/removing agents.
    ///
    /// Usage:
    ///   var scheduler = new BTTickScheduler(flatTree, bbSlots: 16, actionSlots: 8);
    ///   int id = scheduler.AddAgent(tickInterval: 3);
    ///   scheduler.SetBBInt(id, slotIndex, value);       // write sensor data
    ///   scheduler.ScheduleTick(Time.deltaTime);          // Burst parallel tick
    ///   var status = scheduler.GetActionStatus(id, 0);   // read action decisions
    ///   scheduler.Dispose();
    /// </summary>
    public class BTTickScheduler : System.IDisposable
    {
        private readonly FlatBehaviorTree _tree;
        private readonly int _bbSlotCount;
        private readonly int _actionSlotCount;

        // Per-agent interleaved state (SoA layout)
        private NativeArray<byte> _nodeStates;
        private NativeArray<int> _auxInts;
        private NativeArray<float> _auxFloats;
        private NativeArray<ActionRequestStatus> _actionSlots;
        private NativeArray<int> _bbInts;
        private NativeArray<float> _bbFloats;
        private NativeArray<byte> _bbBools;
        private NativeArray<int> _tickCountdowns;
        private NativeArray<int> _tickIntervals;
        private NativeArray<byte> _activeFlags;

        private int _agentCount;
        private int _agentCapacity;

        // Free list for O(1) recycle
        private NativeArray<int> _freeList;
        private int _freeCount;

        private JobHandle _lastJobHandle;

        public int AgentCount => _agentCount;
        public int ActiveAgentCount
        {
            get
            {
                int count = 0;
                for (int i = 0; i < _agentCount; i++)
                    if (_activeFlags[i] != 0) count++;
                return count;
            }
        }

        public BTTickScheduler(FlatBehaviorTree tree, int bbSlotCount, int actionSlotCount,
            int initialCapacity = 256)
        {
            _tree = tree;
            _bbSlotCount = bbSlotCount;
            _actionSlotCount = actionSlotCount;
            _agentCapacity = initialCapacity;

            AllocateArrays(initialCapacity);
        }

        private void AllocateArrays(int capacity)
        {
            int nc = _tree.NodeCount;
            _nodeStates = new NativeArray<byte>(capacity * nc, Allocator.Persistent);
            _auxInts = new NativeArray<int>(capacity * nc, Allocator.Persistent);
            _auxFloats = new NativeArray<float>(capacity * nc, Allocator.Persistent);
            _actionSlots = new NativeArray<ActionRequestStatus>(capacity * _actionSlotCount, Allocator.Persistent);
            _bbInts = new NativeArray<int>(capacity * _bbSlotCount, Allocator.Persistent);
            _bbFloats = new NativeArray<float>(capacity * _bbSlotCount, Allocator.Persistent);
            _bbBools = new NativeArray<byte>(capacity * _bbSlotCount, Allocator.Persistent);
            _tickCountdowns = new NativeArray<int>(capacity, Allocator.Persistent);
            _tickIntervals = new NativeArray<int>(capacity, Allocator.Persistent);
            _activeFlags = new NativeArray<byte>(capacity, Allocator.Persistent);
            _freeList = new NativeArray<int>(capacity, Allocator.Persistent);
        }

        /// <summary>
        /// Add a new agent. Returns agent ID (index).
        /// </summary>
        public int AddAgent(int tickInterval = 1)
        {
            _lastJobHandle.Complete();

            int id;
            if (_freeCount > 0)
            {
                id = _freeList[--_freeCount];
            }
            else
            {
                if (_agentCount >= _agentCapacity)
                    Grow(_agentCapacity * 2);
                id = _agentCount++;
            }

            _tickIntervals[id] = math.max(1, tickInterval);
            _tickCountdowns[id] = 0;
            _activeFlags[id] = 1;

            // Reset node states
            int nc = _tree.NodeCount;
            int nOff = id * nc;
            for (int i = 0; i < nc; i++)
            {
                _nodeStates[nOff + i] = (byte)RuntimeState.NotEntered;
                _auxInts[nOff + i] = 0;
                _auxFloats[nOff + i] = 0f;
            }

            // Reset action slots
            int aOff = id * _actionSlotCount;
            for (int i = 0; i < _actionSlotCount; i++)
                _actionSlots[aOff + i] = ActionRequestStatus.Idle;

            return id;
        }

        /// <summary>
        /// Remove agent (O(1) — marks inactive and adds to free list).
        /// </summary>
        public void RemoveAgent(int agentId)
        {
            _lastJobHandle.Complete();

            if (agentId < 0 || agentId >= _agentCount) return;
            _activeFlags[agentId] = 0;
            _freeList[_freeCount++] = agentId;
        }

        /// <summary>
        /// Schedule a Burst-compiled parallel tick for all active agents.
        /// Call once per frame. Returns the JobHandle for dependency chaining.
        /// </summary>
        public JobHandle ScheduleTick(float deltaTime, int batchSize = 64, JobHandle dependency = default)
        {
            _lastJobHandle.Complete();

            var job = new BTTickJob
            {
                TreeNodes = _tree.Nodes,
                ChildIndices = _tree.ChildIndices,
                NodeCount = _tree.NodeCount,
                DeltaTime = deltaTime,
                AllNodeStates = _nodeStates,
                AllAuxInts = _auxInts,
                AllAuxFloats = _auxFloats,
                AllActionSlots = _actionSlots,
                ActionSlotCount = _actionSlotCount,
                AllBBInts = _bbInts,
                AllBBFloats = _bbFloats,
                AllBBBools = _bbBools,
                BBSlotCount = _bbSlotCount,
                TickCountdowns = _tickCountdowns,
                TickIntervals = _tickIntervals,
                ActiveFlags = _activeFlags,
            };

            _lastJobHandle = job.Schedule(_agentCount, batchSize, dependency);
            return _lastJobHandle;
        }

        /// <summary>Complete outstanding job. Must be called before reading results.</summary>
        public void CompleteTick()
        {
            _lastJobHandle.Complete();
        }

        #region Blackboard Accessors

        public void SetBBInt(int agentId, int slotIndex, int value)
        {
            _bbInts[agentId * _bbSlotCount + slotIndex] = value;
        }

        public int GetBBInt(int agentId, int slotIndex)
        {
            return _bbInts[agentId * _bbSlotCount + slotIndex];
        }

        public void SetBBFloat(int agentId, int slotIndex, float value)
        {
            _bbFloats[agentId * _bbSlotCount + slotIndex] = value;
        }

        public float GetBBFloat(int agentId, int slotIndex)
        {
            return _bbFloats[agentId * _bbSlotCount + slotIndex];
        }

        public void SetBBBool(int agentId, int slotIndex, bool value)
        {
            _bbBools[agentId * _bbSlotCount + slotIndex] = value ? (byte)1 : (byte)0;
        }

        public bool GetBBBool(int agentId, int slotIndex)
        {
            return _bbBools[agentId * _bbSlotCount + slotIndex] != 0;
        }

        #endregion

        #region Action Slot Accessors

        public ActionRequestStatus GetActionStatus(int agentId, int actionId)
        {
            return _actionSlots[agentId * _actionSlotCount + actionId];
        }

        /// <summary>
        /// External system sets action completion status.
        /// Typically called after action execution finishes.
        /// </summary>
        public void SetActionStatus(int agentId, int actionId, ActionRequestStatus status)
        {
            _actionSlots[agentId * _actionSlotCount + actionId] = status;
        }

        #endregion

        #region State Query

        public RuntimeState GetNodeState(int agentId, int nodeIndex)
        {
            return (RuntimeState)_nodeStates[agentId * _tree.NodeCount + nodeIndex];
        }

        public RuntimeState GetRootState(int agentId)
        {
            return GetNodeState(agentId, 0);
        }

        /// <summary>
        /// FNV-1a hash of an agent's execution state for network desync detection.
        /// </summary>
        public uint ComputeAgentStateHash(int agentId)
        {
            const uint FNV_OFFSET = 2166136261u;
            const uint FNV_PRIME = 16777619u;
            uint hash = FNV_OFFSET;

            int nc = _tree.NodeCount;
            int nOff = agentId * nc;
            for (int i = 0; i < nc; i++)
            {
                hash = (hash ^ _nodeStates[nOff + i]) * FNV_PRIME;
                hash = (hash ^ (uint)_auxInts[nOff + i]) * FNV_PRIME;
            }
            int bbOff = agentId * _bbSlotCount;
            for (int i = 0; i < _bbSlotCount; i++)
            {
                hash = (hash ^ (uint)_bbInts[bbOff + i]) * FNV_PRIME;
            }
            return hash;
        }

        #endregion

        private void Grow(int newCapacity)
        {
            int nc = _tree.NodeCount;

            var newNodeStates = new NativeArray<byte>(newCapacity * nc, Allocator.Persistent);
            NativeArray<byte>.Copy(_nodeStates, newNodeStates, _agentCount * nc);
            _nodeStates.Dispose(); _nodeStates = newNodeStates;

            var newAuxInts = new NativeArray<int>(newCapacity * nc, Allocator.Persistent);
            NativeArray<int>.Copy(_auxInts, newAuxInts, _agentCount * nc);
            _auxInts.Dispose(); _auxInts = newAuxInts;

            var newAuxFloats = new NativeArray<float>(newCapacity * nc, Allocator.Persistent);
            NativeArray<float>.Copy(_auxFloats, newAuxFloats, _agentCount * nc);
            _auxFloats.Dispose(); _auxFloats = newAuxFloats;

            var newActionSlots = new NativeArray<ActionRequestStatus>(newCapacity * _actionSlotCount, Allocator.Persistent);
            NativeArray<ActionRequestStatus>.Copy(_actionSlots, newActionSlots, _agentCount * _actionSlotCount);
            _actionSlots.Dispose(); _actionSlots = newActionSlots;

            var newBBInts = new NativeArray<int>(newCapacity * _bbSlotCount, Allocator.Persistent);
            NativeArray<int>.Copy(_bbInts, newBBInts, _agentCount * _bbSlotCount);
            _bbInts.Dispose(); _bbInts = newBBInts;

            var newBBFloats = new NativeArray<float>(newCapacity * _bbSlotCount, Allocator.Persistent);
            NativeArray<float>.Copy(_bbFloats, newBBFloats, _agentCount * _bbSlotCount);
            _bbFloats.Dispose(); _bbFloats = newBBFloats;

            var newBBBools = new NativeArray<byte>(newCapacity * _bbSlotCount, Allocator.Persistent);
            NativeArray<byte>.Copy(_bbBools, newBBBools, _agentCount * _bbSlotCount);
            _bbBools.Dispose(); _bbBools = newBBBools;

            var newCountdowns = new NativeArray<int>(newCapacity, Allocator.Persistent);
            NativeArray<int>.Copy(_tickCountdowns, newCountdowns, _agentCount);
            _tickCountdowns.Dispose(); _tickCountdowns = newCountdowns;

            var newIntervals = new NativeArray<int>(newCapacity, Allocator.Persistent);
            NativeArray<int>.Copy(_tickIntervals, newIntervals, _agentCount);
            _tickIntervals.Dispose(); _tickIntervals = newIntervals;

            var newActive = new NativeArray<byte>(newCapacity, Allocator.Persistent);
            NativeArray<byte>.Copy(_activeFlags, newActive, _agentCount);
            _activeFlags.Dispose(); _activeFlags = newActive;

            var newFree = new NativeArray<int>(newCapacity, Allocator.Persistent);
            NativeArray<int>.Copy(_freeList, newFree, _freeCount);
            _freeList.Dispose(); _freeList = newFree;

            _agentCapacity = newCapacity;
        }

        public void Dispose()
        {
            _lastJobHandle.Complete();

            if (_nodeStates.IsCreated) _nodeStates.Dispose();
            if (_auxInts.IsCreated) _auxInts.Dispose();
            if (_auxFloats.IsCreated) _auxFloats.Dispose();
            if (_actionSlots.IsCreated) _actionSlots.Dispose();
            if (_bbInts.IsCreated) _bbInts.Dispose();
            if (_bbFloats.IsCreated) _bbFloats.Dispose();
            if (_bbBools.IsCreated) _bbBools.Dispose();
            if (_tickCountdowns.IsCreated) _tickCountdowns.Dispose();
            if (_tickIntervals.IsCreated) _tickIntervals.Dispose();
            if (_activeFlags.IsCreated) _activeFlags.Dispose();
            if (_freeList.IsCreated) _freeList.Dispose();
        }
    }
}
