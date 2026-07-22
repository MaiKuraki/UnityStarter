using System;
using Unity.Collections;
using Unity.Jobs;
using Unity.Mathematics;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Stable agent identity. A recycled scheduler slot always receives a new generation.
    /// </summary>
    public readonly struct BTAgentHandle : IEquatable<BTAgentHandle>
    {
        public BTAgentHandle(int index, uint generation)
        {
            Index = index;
            Generation = generation;
        }

        public int Index { get; }
        public uint Generation { get; }
        public bool IsValid => Index >= 0 && Generation != 0u;

        public bool Equals(BTAgentHandle other)
        {
            return Index == other.Index && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is BTAgentHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked((Index * 397) ^ (int)Generation);
        }

        public static bool operator ==(BTAgentHandle left, BTAgentHandle right) => left.Equals(right);
        public static bool operator !=(BTAgentHandle left, BTAgentHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Identifies one action request activation. Completion from an older activation is rejected.
    /// </summary>
    public readonly struct BTActionRequestHandle : IEquatable<BTActionRequestHandle>
    {
        public BTActionRequestHandle(BTAgentHandle agent, int actionId, uint generation)
        {
            Agent = agent;
            ActionId = actionId;
            Generation = generation;
        }

        public BTAgentHandle Agent { get; }
        public int ActionId { get; }
        public uint Generation { get; }
        public bool IsValid => Agent.IsValid && ActionId >= 0 && Generation != 0u;

        public bool Equals(BTActionRequestHandle other)
        {
            return Agent.Equals(other.Agent) && ActionId == other.ActionId && Generation == other.Generation;
        }

        public override bool Equals(object obj)
        {
            return obj is BTActionRequestHandle other && Equals(other);
        }

        public override int GetHashCode()
        {
            return unchecked(((Agent.GetHashCode() * 397) ^ ActionId) * 397 ^ (int)Generation);
        }

        public static bool operator ==(BTActionRequestHandle left, BTActionRequestHandle right) => left.Equals(right);
        public static bool operator !=(BTActionRequestHandle left, BTActionRequestHandle right) => !left.Equals(right);
    }

    /// <summary>
    /// Single-owner scheduler for agents that share one immutable flat tree definition.
    /// Public reads and writes complete the outstanding job before accessing NativeArrays.
    /// The caller owns the FlatBehaviorTree and must keep it alive until this scheduler is disposed.
    /// </summary>
    public sealed class BTTickScheduler : IDisposable
    {
        public const int MAX_SUPPORTED_TREE_DEPTH = 128;

        private readonly FlatBehaviorTree _tree;
        private readonly int _bbSlotCount;
        private readonly int _actionSlotCount;
        private readonly int _ownerThreadId;

        private NativeArray<byte> _nodeStates;
        private NativeArray<int> _auxInts;
        private NativeArray<float> _auxFloats;
        private NativeArray<ActionRequestStatus> _actionSlots;
        private NativeArray<uint> _actionGenerations;
        private NativeArray<int> _bbInts;
        private NativeArray<float> _bbFloats;
        private NativeArray<byte> _bbBools;
        private NativeArray<int> _tickCountdowns;
        private NativeArray<int> _tickIntervals;
        private NativeArray<float> _accumulatedDeltaTimes;
        private NativeArray<byte> _activeFlags;
        private NativeArray<uint> _agentGenerations;

        private int _agentCount;
        private int _agentCapacity;

        private NativeArray<int> _freeList;
        private int _freeCount;

        private JobHandle _lastJobHandle;
        private bool _treeLeaseHeld;
        private bool _disposed;

        /// <summary>High-water slot count. Use ActiveAgentCount for currently live agents.</summary>
        public int AgentCount
        {
            get
            {
                EnsureOwnerThread();
                EnsureNotDisposed();
                return _agentCount;
            }
        }

        public int ActiveAgentCount
        {
            get
            {
                CompleteForAccess();
                int count = 0;
                for (int i = 0; i < _agentCount; i++)
                {
                    if (_activeFlags[i] != 0)
                    {
                        count++;
                    }
                }

                return count;
            }
        }

        public BTTickScheduler(
            FlatBehaviorTree tree,
            int bbSlotCount,
            int actionSlotCount,
            int initialCapacity = 256)
        {
            _ownerThreadId = Environment.CurrentManagedThreadId;
            if (tree == null || !tree.IsCreated || tree.NodeCount <= 0)
            {
                throw new ArgumentException("A created flat tree with at least one node is required.", nameof(tree));
            }

            if (bbSlotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(bbSlotCount));
            }

            if (actionSlotCount < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(actionSlotCount));
            }

            if (initialCapacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(initialCapacity));
            }

            ValidateTreeDefinition(tree, bbSlotCount, actionSlotCount);

            _tree = tree;
            _bbSlotCount = bbSlotCount;
            _actionSlotCount = actionSlotCount;
            _agentCapacity = initialCapacity;
            tree.AcquireSchedulerLease();
            _treeLeaseHeld = true;
            try
            {
                AllocateArrays(initialCapacity);
            }
            catch
            {
                tree.ReleaseSchedulerLease();
                _treeLeaseHeld = false;
                throw;
            }
        }

        public BTAgentHandle AddAgent(int tickInterval = 1)
        {
            CompleteForAccess();

            int id;
            if (_freeCount > 0)
            {
                id = _freeList[--_freeCount];
            }
            else
            {
                if (_agentCount >= _agentCapacity)
                {
                    Grow(checked(_agentCapacity * 2));
                }

                id = _agentCount++;
            }

            uint generation = NextGeneration(_agentGenerations[id]);
            _agentGenerations[id] = generation;
            _tickIntervals[id] = math.max(1, tickInterval);
            _tickCountdowns[id] = 0;
            _activeFlags[id] = 1;
            ResetAgentStorage(id, invalidateActions: true);
            return new BTAgentHandle(id, generation);
        }

        /// <summary>
        /// Removes an active agent. Returns false for stale, invalid, or already removed handles.
        /// </summary>
        public bool RemoveAgent(BTAgentHandle agent)
        {
            CompleteForAccess();
            if (!IsActiveHandle(agent))
            {
                return false;
            }

            _activeFlags[agent.Index] = 0;
            ResetAgentStorage(agent.Index, invalidateActions: true);
            _tickCountdowns[agent.Index] = 0;
            _tickIntervals[agent.Index] = 0;
            _accumulatedDeltaTimes[agent.Index] = 0f;
            _freeList[_freeCount++] = agent.Index;
            return true;
        }

        /// <summary>
        /// Starts a new activation for a terminal or running agent and invalidates outstanding actions.
        /// Blackboard slots are preserved unless clearBlackboard is true.
        /// </summary>
        public void ResetAgent(BTAgentHandle agent, bool clearBlackboard = false)
        {
            CompleteForAccess();
            ValidateActiveHandle(agent);
            ResetAgentExecution(agent.Index, invalidateActions: true);
            if (clearBlackboard)
            {
                ClearBlackboardStorage(agent.Index);
            }
        }

        /// <summary>
        /// Schedules one ordered tick. Successive calls depend on the prior tick without blocking the main thread.
        /// </summary>
        public JobHandle ScheduleTick(float deltaTime, int batchSize = 64, JobHandle dependency = default)
        {
            EnsureOwnerThread();
            EnsureNotDisposed();
            if (!math.isfinite(deltaTime) || deltaTime < 0f)
            {
                throw new ArgumentOutOfRangeException(nameof(deltaTime));
            }

            if (batchSize <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(batchSize));
            }

            if (!_tree.IsCreated)
            {
                throw new InvalidOperationException("The flat tree was disposed before its scheduler.");
            }

            JobHandle combinedDependency = JobHandle.CombineDependencies(_lastJobHandle, dependency);
            if (_agentCount == 0)
            {
                _lastJobHandle = combinedDependency;
                return _lastJobHandle;
            }

            var job = new BTTickJob
            {
                TreeNodes = _tree.NodeStorage,
                ChildIndices = _tree.ChildIndexStorage,
                NodeCount = _tree.NodeCount,
                DeltaTime = deltaTime,
                AllNodeStates = _nodeStates,
                AllAuxInts = _auxInts,
                AllAuxFloats = _auxFloats,
                AllActionSlots = _actionSlots,
                AllActionGenerations = _actionGenerations,
                ActionSlotCount = _actionSlotCount,
                AllBBInts = _bbInts,
                AllBBFloats = _bbFloats,
                AllBBBools = _bbBools,
                BBSlotCount = _bbSlotCount,
                TickCountdowns = _tickCountdowns,
                TickIntervals = _tickIntervals,
                AccumulatedDeltaTimes = _accumulatedDeltaTimes,
                ActiveFlags = _activeFlags,
            };

            _lastJobHandle = job.Schedule(_agentCount, batchSize, combinedDependency);
            return _lastJobHandle;
        }

        public void CompleteTick()
        {
            CompleteForAccess();
        }

        public void SetBBInt(BTAgentHandle agent, int slotIndex, int value)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, slotIndex, _bbSlotCount, nameof(slotIndex));
            _bbInts[agent.Index * _bbSlotCount + slotIndex] = value;
        }

        public int GetBBInt(BTAgentHandle agent, int slotIndex)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, slotIndex, _bbSlotCount, nameof(slotIndex));
            return _bbInts[agent.Index * _bbSlotCount + slotIndex];
        }

        public void SetBBFloat(BTAgentHandle agent, int slotIndex, float value)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, slotIndex, _bbSlotCount, nameof(slotIndex));
            _bbFloats[agent.Index * _bbSlotCount + slotIndex] = value;
        }

        public float GetBBFloat(BTAgentHandle agent, int slotIndex)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, slotIndex, _bbSlotCount, nameof(slotIndex));
            return _bbFloats[agent.Index * _bbSlotCount + slotIndex];
        }

        public void SetBBBool(BTAgentHandle agent, int slotIndex, bool value)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, slotIndex, _bbSlotCount, nameof(slotIndex));
            _bbBools[agent.Index * _bbSlotCount + slotIndex] = value ? (byte)1 : (byte)0;
        }

        public bool GetBBBool(BTAgentHandle agent, int slotIndex)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, slotIndex, _bbSlotCount, nameof(slotIndex));
            return _bbBools[agent.Index * _bbSlotCount + slotIndex] != 0;
        }

        public ActionRequestStatus GetActionStatus(BTAgentHandle agent, int actionId)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, actionId, _actionSlotCount, nameof(actionId));
            return _actionSlots[agent.Index * _actionSlotCount + actionId];
        }

        /// <summary>
        /// Gets the activation token for a requested or running action.
        /// </summary>
        public bool TryGetActionRequest(
            BTAgentHandle agent,
            int actionId,
            out BTActionRequestHandle request,
            out ActionRequestStatus status)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, actionId, _actionSlotCount, nameof(actionId));
            int slot = agent.Index * _actionSlotCount + actionId;
            status = _actionSlots[slot];
            if (status != ActionRequestStatus.Requested && status != ActionRequestStatus.Running)
            {
                request = default;
                return false;
            }

            request = new BTActionRequestHandle(agent, actionId, _actionGenerations[slot]);
            return true;
        }

        /// <summary>
        /// Updates one active request. Returns false when completion is stale or the agent was recycled.
        /// </summary>
        public bool TrySetActionStatus(BTActionRequestHandle request, ActionRequestStatus status)
        {
            CompleteForAccess();
            if (status != ActionRequestStatus.Running &&
                status != ActionRequestStatus.Success &&
                status != ActionRequestStatus.Failed)
            {
                return false;
            }

            if (!request.IsValid || !IsActiveHandle(request.Agent) ||
                (uint)request.ActionId >= (uint)_actionSlotCount)
            {
                return false;
            }

            int slot = request.Agent.Index * _actionSlotCount + request.ActionId;
            ActionRequestStatus current = _actionSlots[slot];
            if (_actionGenerations[slot] != request.Generation ||
                (current != ActionRequestStatus.Requested && current != ActionRequestStatus.Running))
            {
                return false;
            }

            _actionSlots[slot] = status;
            return true;
        }

        public bool CancelAction(BTAgentHandle agent, int actionId)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, actionId, _actionSlotCount, nameof(actionId));
            int slot = agent.Index * _actionSlotCount + actionId;
            if (_actionSlots[slot] == ActionRequestStatus.Idle)
            {
                return false;
            }

            InvalidateActionSlot(slot);
            return true;
        }

        public RuntimeState GetNodeState(BTAgentHandle agent, int nodeIndex)
        {
            CompleteForAccess();
            ValidateAgentAndSlot(agent, nodeIndex, _tree.NodeCount, nameof(nodeIndex));
            return (RuntimeState)_nodeStates[agent.Index * _tree.NodeCount + nodeIndex];
        }

        public RuntimeState GetRootState(BTAgentHandle agent)
        {
            return GetNodeState(agent, 0);
        }

        public ulong ComputeAgentStateHash(BTAgentHandle agent)
        {
            CompleteForAccess();
            ValidateActiveHandle(agent);

            ulong hash = BTDataOrientedStateHash.Begin(BTDataOrientedStateHash.SchedulerProfile);

            int nodeOffset = agent.Index * _tree.NodeCount;
            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.NodeStatesDomain,
                _tree.NodeCount);
            for (int i = 0; i < _tree.NodeCount; i++)
            {
                hash = BTDataOrientedStateHash.AddByte(hash, _nodeStates[nodeOffset + i]);
            }

            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.AuxIntsDomain,
                _tree.NodeCount);
            for (int i = 0; i < _tree.NodeCount; i++)
            {
                hash = BTDataOrientedStateHash.AddUInt32(
                    hash,
                    unchecked((uint)_auxInts[nodeOffset + i]));
            }

            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.AuxFloatsDomain,
                _tree.NodeCount);
            for (int i = 0; i < _tree.NodeCount; i++)
            {
                hash = BTDataOrientedStateHash.AddFloat(hash, _auxFloats[nodeOffset + i]);
            }

            int blackboardOffset = agent.Index * _bbSlotCount;
            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.BlackboardIntsDomain,
                _bbSlotCount);
            for (int i = 0; i < _bbSlotCount; i++)
            {
                hash = BTDataOrientedStateHash.AddUInt32(
                    hash,
                    unchecked((uint)_bbInts[blackboardOffset + i]));
            }

            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.BlackboardFloatsDomain,
                _bbSlotCount);
            for (int i = 0; i < _bbSlotCount; i++)
            {
                hash = BTDataOrientedStateHash.AddFloat(hash, _bbFloats[blackboardOffset + i]);
            }

            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.BlackboardBoolsDomain,
                _bbSlotCount);
            for (int i = 0; i < _bbSlotCount; i++)
            {
                hash = BTDataOrientedStateHash.AddByte(hash, _bbBools[blackboardOffset + i]);
            }

            int actionOffset = agent.Index * _actionSlotCount;
            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.ActionStatusesDomain,
                _actionSlotCount);
            for (int i = 0; i < _actionSlotCount; i++)
            {
                hash = BTDataOrientedStateHash.AddByte(hash, (byte)_actionSlots[actionOffset + i]);
            }

            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.ActionGenerationsDomain,
                _actionSlotCount);
            for (int i = 0; i < _actionSlotCount; i++)
            {
                hash = BTDataOrientedStateHash.AddUInt32(hash, _actionGenerations[actionOffset + i]);
            }

            hash = BTDataOrientedStateHash.BeginDomain(
                hash,
                BTDataOrientedStateHash.TimingDomain,
                3);
            hash = BTDataOrientedStateHash.AddUInt32(
                hash,
                unchecked((uint)_tickIntervals[agent.Index]));
            hash = BTDataOrientedStateHash.AddUInt32(
                hash,
                unchecked((uint)_tickCountdowns[agent.Index]));
            hash = BTDataOrientedStateHash.AddFloat(hash, _accumulatedDeltaTimes[agent.Index]);

            return hash;
        }

        private void AllocateArrays(int capacity)
        {
            int nodeCount = _tree.NodeCount;
            int nodeLength = checked(capacity * nodeCount);
            int actionLength = checked(capacity * _actionSlotCount);
            int blackboardLength = checked(capacity * _bbSlotCount);

            try
            {
                _nodeStates = new NativeArray<byte>(nodeLength, Allocator.Persistent);
                _auxInts = new NativeArray<int>(nodeLength, Allocator.Persistent);
                _auxFloats = new NativeArray<float>(nodeLength, Allocator.Persistent);
                _actionSlots = new NativeArray<ActionRequestStatus>(actionLength, Allocator.Persistent);
                _actionGenerations = new NativeArray<uint>(actionLength, Allocator.Persistent);
                _bbInts = new NativeArray<int>(blackboardLength, Allocator.Persistent);
                _bbFloats = new NativeArray<float>(blackboardLength, Allocator.Persistent);
                _bbBools = new NativeArray<byte>(blackboardLength, Allocator.Persistent);
                _tickCountdowns = new NativeArray<int>(capacity, Allocator.Persistent);
                _tickIntervals = new NativeArray<int>(capacity, Allocator.Persistent);
                _accumulatedDeltaTimes = new NativeArray<float>(capacity, Allocator.Persistent);
                _activeFlags = new NativeArray<byte>(capacity, Allocator.Persistent);
                _agentGenerations = new NativeArray<uint>(capacity, Allocator.Persistent);
                _freeList = new NativeArray<int>(capacity, Allocator.Persistent);
            }
            catch
            {
                DisposeArrays();
                throw;
            }
        }

        private void ResetAgentStorage(int agentIndex, bool invalidateActions)
        {
            ResetAgentExecution(agentIndex, invalidateActions);
            ClearBlackboardStorage(agentIndex);
        }

        private void ResetAgentExecution(int agentIndex, bool invalidateActions)
        {
            int nodeOffset = agentIndex * _tree.NodeCount;
            for (int i = 0; i < _tree.NodeCount; i++)
            {
                _nodeStates[nodeOffset + i] = (byte)RuntimeState.NotEntered;
                _auxInts[nodeOffset + i] = 0;
                _auxFloats[nodeOffset + i] = 0f;
            }

            int actionOffset = agentIndex * _actionSlotCount;
            for (int i = 0; i < _actionSlotCount; i++)
            {
                int slot = actionOffset + i;
                _actionSlots[slot] = ActionRequestStatus.Idle;
                if (invalidateActions)
                {
                    _actionGenerations[slot] = NextGeneration(_actionGenerations[slot]);
                }
            }

            _tickCountdowns[agentIndex] = 0;
            _accumulatedDeltaTimes[agentIndex] = 0f;
        }

        private void ClearBlackboardStorage(int agentIndex)
        {
            int blackboardOffset = agentIndex * _bbSlotCount;
            for (int i = 0; i < _bbSlotCount; i++)
            {
                _bbInts[blackboardOffset + i] = 0;
                _bbFloats[blackboardOffset + i] = 0f;
                _bbBools[blackboardOffset + i] = 0;
            }
        }

        private void Grow(int newCapacity)
        {
            if (newCapacity <= _agentCapacity)
            {
                throw new InvalidOperationException("Scheduler capacity cannot grow further.");
            }

            int nodeCount = _tree.NodeCount;
            int newNodeLength = checked(newCapacity * nodeCount);
            int usedNodeLength = checked(_agentCount * nodeCount);
            int newActionLength = checked(newCapacity * _actionSlotCount);
            int usedActionLength = checked(_agentCount * _actionSlotCount);
            int newBlackboardLength = checked(newCapacity * _bbSlotCount);
            int usedBlackboardLength = checked(_agentCount * _bbSlotCount);

            NativeArray<byte> nodeStates = default;
            NativeArray<int> auxInts = default;
            NativeArray<float> auxFloats = default;
            NativeArray<ActionRequestStatus> actionSlots = default;
            NativeArray<uint> actionGenerations = default;
            NativeArray<int> blackboardInts = default;
            NativeArray<float> blackboardFloats = default;
            NativeArray<byte> blackboardBools = default;
            NativeArray<int> tickCountdowns = default;
            NativeArray<int> tickIntervals = default;
            NativeArray<float> accumulatedDeltaTimes = default;
            NativeArray<byte> activeFlags = default;
            NativeArray<uint> agentGenerations = default;
            NativeArray<int> freeList = default;

            try
            {
                nodeStates = new NativeArray<byte>(newNodeLength, Allocator.Persistent);
                auxInts = new NativeArray<int>(newNodeLength, Allocator.Persistent);
                auxFloats = new NativeArray<float>(newNodeLength, Allocator.Persistent);
                actionSlots = new NativeArray<ActionRequestStatus>(newActionLength, Allocator.Persistent);
                actionGenerations = new NativeArray<uint>(newActionLength, Allocator.Persistent);
                blackboardInts = new NativeArray<int>(newBlackboardLength, Allocator.Persistent);
                blackboardFloats = new NativeArray<float>(newBlackboardLength, Allocator.Persistent);
                blackboardBools = new NativeArray<byte>(newBlackboardLength, Allocator.Persistent);
                tickCountdowns = new NativeArray<int>(newCapacity, Allocator.Persistent);
                tickIntervals = new NativeArray<int>(newCapacity, Allocator.Persistent);
                accumulatedDeltaTimes = new NativeArray<float>(newCapacity, Allocator.Persistent);
                activeFlags = new NativeArray<byte>(newCapacity, Allocator.Persistent);
                agentGenerations = new NativeArray<uint>(newCapacity, Allocator.Persistent);
                freeList = new NativeArray<int>(newCapacity, Allocator.Persistent);

                CopyIfNotEmpty(_nodeStates, nodeStates, usedNodeLength);
                CopyIfNotEmpty(_auxInts, auxInts, usedNodeLength);
                CopyIfNotEmpty(_auxFloats, auxFloats, usedNodeLength);
                CopyIfNotEmpty(_actionSlots, actionSlots, usedActionLength);
                CopyIfNotEmpty(_actionGenerations, actionGenerations, usedActionLength);
                CopyIfNotEmpty(_bbInts, blackboardInts, usedBlackboardLength);
                CopyIfNotEmpty(_bbFloats, blackboardFloats, usedBlackboardLength);
                CopyIfNotEmpty(_bbBools, blackboardBools, usedBlackboardLength);
                CopyIfNotEmpty(_tickCountdowns, tickCountdowns, _agentCount);
                CopyIfNotEmpty(_tickIntervals, tickIntervals, _agentCount);
                CopyIfNotEmpty(_accumulatedDeltaTimes, accumulatedDeltaTimes, _agentCount);
                CopyIfNotEmpty(_activeFlags, activeFlags, _agentCount);
                CopyIfNotEmpty(_agentGenerations, agentGenerations, _agentCount);
                CopyIfNotEmpty(_freeList, freeList, _freeCount);
            }
            catch
            {
                DisposeIfCreated(ref nodeStates);
                DisposeIfCreated(ref auxInts);
                DisposeIfCreated(ref auxFloats);
                DisposeIfCreated(ref actionSlots);
                DisposeIfCreated(ref actionGenerations);
                DisposeIfCreated(ref blackboardInts);
                DisposeIfCreated(ref blackboardFloats);
                DisposeIfCreated(ref blackboardBools);
                DisposeIfCreated(ref tickCountdowns);
                DisposeIfCreated(ref tickIntervals);
                DisposeIfCreated(ref accumulatedDeltaTimes);
                DisposeIfCreated(ref activeFlags);
                DisposeIfCreated(ref agentGenerations);
                DisposeIfCreated(ref freeList);
                throw;
            }

            _nodeStates.Dispose();
            _auxInts.Dispose();
            _auxFloats.Dispose();
            _actionSlots.Dispose();
            _actionGenerations.Dispose();
            _bbInts.Dispose();
            _bbFloats.Dispose();
            _bbBools.Dispose();
            _tickCountdowns.Dispose();
            _tickIntervals.Dispose();
            _accumulatedDeltaTimes.Dispose();
            _activeFlags.Dispose();
            _agentGenerations.Dispose();
            _freeList.Dispose();

            _nodeStates = nodeStates;
            _auxInts = auxInts;
            _auxFloats = auxFloats;
            _actionSlots = actionSlots;
            _actionGenerations = actionGenerations;
            _bbInts = blackboardInts;
            _bbFloats = blackboardFloats;
            _bbBools = blackboardBools;
            _tickCountdowns = tickCountdowns;
            _tickIntervals = tickIntervals;
            _accumulatedDeltaTimes = accumulatedDeltaTimes;
            _activeFlags = activeFlags;
            _agentGenerations = agentGenerations;
            _freeList = freeList;
            _agentCapacity = newCapacity;
        }

        private static void CopyIfNotEmpty<T>(NativeArray<T> source, NativeArray<T> destination, int length)
            where T : struct
        {
            if (length > 0)
            {
                NativeArray<T>.Copy(source, destination, length);
            }
        }

        private static void DisposeIfCreated<T>(ref NativeArray<T> array)
            where T : struct
        {
            if (array.IsCreated)
            {
                array.Dispose();
            }
        }

        private void CompleteForAccess()
        {
            EnsureOwnerThread();
            EnsureNotDisposed();
            _lastJobHandle.Complete();
        }

        private void EnsureOwnerThread()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException(
                    $"BTTickScheduler must run on owner thread {_ownerThreadId}.");
            }
        }

        private void ValidateAgentAndSlot(BTAgentHandle agent, int slot, int slotCount, string parameterName)
        {
            ValidateActiveHandle(agent);
            if ((uint)slot >= (uint)slotCount)
            {
                throw new ArgumentOutOfRangeException(parameterName);
            }
        }

        private void ValidateActiveHandle(BTAgentHandle agent)
        {
            if (!IsActiveHandle(agent))
            {
                throw new InvalidOperationException("Behavior tree agent handle is invalid, stale, or inactive.");
            }
        }

        private bool IsActiveHandle(BTAgentHandle agent)
        {
            return agent.IsValid &&
                   agent.Index < _agentCount &&
                   _activeFlags[agent.Index] != 0 &&
                   _agentGenerations[agent.Index] == agent.Generation;
        }

        private void InvalidateActionSlot(int slot)
        {
            _actionSlots[slot] = ActionRequestStatus.Idle;
            _actionGenerations[slot] = NextGeneration(_actionGenerations[slot]);
        }

        private static uint NextGeneration(uint generation)
        {
            generation++;
            return generation == 0u ? 1u : generation;
        }

        private static void ValidateTreeDefinition(
            FlatBehaviorTree tree,
            int blackboardSlotCount,
            int actionSlotCount)
        {
            FlatBehaviorTree.ReadOnlyBuffer<FlatNodeDef> nodes = tree.Nodes;
            FlatBehaviorTree.ReadOnlyBuffer<int> childIndices = tree.ChildIndices;
            if (nodes.Length < tree.NodeCount)
            {
                throw new ArgumentException("Flat tree node storage is smaller than NodeCount.", nameof(tree));
            }

            int nodeCount = tree.NodeCount;
            if (nodes[0].Type != FlatNodeType.Root)
            {
                throw new ArgumentException("Flat tree node 0 must be the unique root node.", nameof(tree));
            }

            var visitState = new byte[nodeCount];
            var parentCounts = new int[nodeCount];
            var stackNodes = new int[nodeCount];
            var stackChildren = new int[nodeCount];
            var actionOwners = new bool[actionSlotCount];

            for (int i = 0; i < nodeCount; i++)
            {
                FlatNodeDef node = nodes[i];
                if (i > 0 && node.Type == FlatNodeType.Root)
                {
                    throw new ArgumentException($"Flat tree node {i} cannot be a second root node.", nameof(tree));
                }

                int childEnd = node.ChildStartIndex + node.ChildCount;
                if (node.ChildStartIndex < 0 || childEnd < node.ChildStartIndex || childEnd > childIndices.Length)
                {
                    throw new ArgumentException($"Flat tree node {i} has an invalid child range.", nameof(tree));
                }

                if (IsTimeNode(node.Type) && (!math.isfinite(node.ParamFloat) || node.ParamFloat < 0f))
                {
                    throw new ArgumentException(
                        $"Flat tree time node {i} requires a finite, non-negative duration.",
                        nameof(tree));
                }

                if ((node.Type == FlatNodeType.Repeater || node.Type == FlatNodeType.Retry) &&
                    node.ParamInt != -1 &&
                    node.ParamInt < 1)
                {
                    throw new ArgumentException(
                        $"Flat tree {node.Type} node {i} requires -1 for infinite execution or a count of at least one.",
                        nameof(tree));
                }

                if (node.Type == FlatNodeType.WaitTicks && node.ParamInt < 1)
                {
                    throw new ArgumentException(
                        $"Flat tree WaitTicks node {i} requires a tick count of at least one.",
                        nameof(tree));
                }

                if (node.Type == FlatNodeType.ActionSlot &&
                    (uint)node.ParamInt >= (uint)actionSlotCount)
                {
                    throw new ArgumentException($"Flat tree action node {i} references an invalid action slot.", nameof(tree));
                }

                if (node.Type == FlatNodeType.ActionSlot)
                {
                    if (actionOwners[node.ParamInt])
                    {
                        throw new ArgumentException(
                            $"Flat tree action slot {node.ParamInt} has more than one node owner.",
                            nameof(tree));
                    }

                    actionOwners[node.ParamInt] = true;
                }

                if (node.Type == FlatNodeType.BlackboardCondition &&
                    (uint)node.BBKey >= (uint)blackboardSlotCount)
                {
                    throw new ArgumentException($"Flat tree condition node {i} references an invalid blackboard slot.", nameof(tree));
                }

                if (IsSingleChildNode(node.Type) && node.ChildCount != 1)
                {
                    throw new ArgumentException($"Flat tree node {i} requires exactly one child.", nameof(tree));
                }

                if (IsLeafNode(node.Type) && node.ChildCount != 0)
                {
                    throw new ArgumentException($"Flat tree leaf node {i} cannot contain children.", nameof(tree));
                }

                if (node.Type == FlatNodeType.Parallel && !HasValidParallelThresholds(node))
                {
                    throw new ArgumentException($"Flat tree parallel node {i} has invalid thresholds.", nameof(tree));
                }
            }

            int depth = 0;
            stackNodes[0] = 0;
            stackChildren[0] = 0;
            visitState[0] = 1;
            while (depth >= 0)
            {
                int nodeIndex = stackNodes[depth];
                FlatNodeDef node = nodes[nodeIndex];
                int nextChild = stackChildren[depth];
                if (nextChild >= node.ChildCount)
                {
                    visitState[nodeIndex] = 2;
                    depth--;
                    continue;
                }

                stackChildren[depth] = nextChild + 1;
                int childIndex = childIndices[node.ChildStartIndex + nextChild];
                if ((uint)childIndex >= (uint)nodeCount)
                {
                    throw new ArgumentException($"Flat tree node {nodeIndex} references invalid child {childIndex}.", nameof(tree));
                }

                parentCounts[childIndex]++;
                if (childIndex == 0 || parentCounts[childIndex] > 1 || visitState[childIndex] == 1)
                {
                    throw new ArgumentException("Flat tree definitions must be acyclic trees with one parent per non-root node.", nameof(tree));
                }

                if (visitState[childIndex] == 2)
                {
                    continue;
                }

                depth++;
                if (depth >= MAX_SUPPORTED_TREE_DEPTH || depth >= nodeCount)
                {
                    throw new ArgumentException(
                        $"Flat tree depth exceeds the supported limit of {MAX_SUPPORTED_TREE_DEPTH}.",
                        nameof(tree));
                }

                stackNodes[depth] = childIndex;
                stackChildren[depth] = 0;
                visitState[childIndex] = 1;
            }

            for (int i = 0; i < nodeCount; i++)
            {
                if (visitState[i] != 2 || (i > 0 && parentCounts[i] != 1))
                {
                    throw new ArgumentException("Flat tree contains unreachable or multiply-owned nodes.", nameof(tree));
                }
            }
        }

        private static bool IsSingleChildNode(FlatNodeType type)
        {
            return type == FlatNodeType.Root ||
                   type == FlatNodeType.Inverter ||
                   type == FlatNodeType.Repeater ||
                   type == FlatNodeType.Succeeder ||
                   type == FlatNodeType.ForceFailure ||
                   type == FlatNodeType.Retry ||
                   type == FlatNodeType.Timeout ||
                   type == FlatNodeType.Delay ||
                   type == FlatNodeType.RunOnce ||
                   type == FlatNodeType.CoolDown;
        }

        private static bool IsLeafNode(FlatNodeType type)
        {
            return type == FlatNodeType.ActionSlot ||
                   type == FlatNodeType.BlackboardCondition ||
                   type == FlatNodeType.WaitTicks;
        }

        private static bool IsTimeNode(FlatNodeType type)
        {
            return type == FlatNodeType.Timeout ||
                   type == FlatNodeType.Delay ||
                   type == FlatNodeType.CoolDown;
        }

        private static bool HasValidParallelThresholds(FlatNodeDef node)
        {
            if (node.ChildCount == 0)
            {
                return node.SuccessThreshold == 0 && node.FailureThreshold == 1;
            }

            return node.SuccessThreshold >= 1 &&
                   node.FailureThreshold >= 1 &&
                   node.SuccessThreshold <= node.ChildCount &&
                   node.FailureThreshold <= node.ChildCount &&
                   node.SuccessThreshold + node.FailureThreshold <= node.ChildCount + 1;
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(BTTickScheduler));
            }
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            EnsureOwnerThread();
            _lastJobHandle.Complete();
            _disposed = true;
            try
            {
                DisposeArrays();
            }
            finally
            {
                if (_treeLeaseHeld)
                {
                    _tree.ReleaseSchedulerLease();
                    _treeLeaseHeld = false;
                }
            }
        }

        private void DisposeArrays()
        {
            if (_nodeStates.IsCreated) _nodeStates.Dispose();
            if (_auxInts.IsCreated) _auxInts.Dispose();
            if (_auxFloats.IsCreated) _auxFloats.Dispose();
            if (_actionSlots.IsCreated) _actionSlots.Dispose();
            if (_actionGenerations.IsCreated) _actionGenerations.Dispose();
            if (_bbInts.IsCreated) _bbInts.Dispose();
            if (_bbFloats.IsCreated) _bbFloats.Dispose();
            if (_bbBools.IsCreated) _bbBools.Dispose();
            if (_tickCountdowns.IsCreated) _tickCountdowns.Dispose();
            if (_tickIntervals.IsCreated) _tickIntervals.Dispose();
            if (_accumulatedDeltaTimes.IsCreated) _accumulatedDeltaTimes.Dispose();
            if (_activeFlags.IsCreated) _activeFlags.Dispose();
            if (_agentGenerations.IsCreated) _agentGenerations.Dispose();
            if (_freeList.IsCreated) _freeList.Dispose();
        }
    }
}
