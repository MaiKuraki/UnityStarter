using Unity.Burst;
using Unity.Collections;
using Unity.Jobs;
using CycloneGames.BehaviorTree.Runtime.Core;

namespace CycloneGames.BehaviorTree.Runtime.DOD
{
    /// <summary>
    /// Burst-compiled parallel job that ticks N agents simultaneously.
    /// Each agent shares the same FlatBehaviorTree (read-only) but has
    /// its own execution state stored in interleaved NativeArrays.
    ///
    /// For 10,000+ agents: schedule with batchSize=64 for optimal cache usage.
    ///
    /// Interleaved layout: agent[i]'s node j is at index [i * NodeCount + j].
    /// This ensures each Execute(i) touches a contiguous memory region.
    /// </summary>
    [BurstCompile]
    public struct BTTickJob : IJobParallelFor
    {
        // Shared tree definition (read-only, same for all agents)
        [ReadOnly] public NativeArray<FlatNodeDef> TreeNodes;
        [ReadOnly] public NativeArray<int> ChildIndices;
        public int NodeCount;
        public float DeltaTime;

        // Per-agent interleaved state [agentIndex * NodeCount + nodeIndex]
        [NativeDisableParallelForRestriction] public NativeArray<byte> AllNodeStates;
        [NativeDisableParallelForRestriction] public NativeArray<int> AllAuxInts;
        [NativeDisableParallelForRestriction] public NativeArray<float> AllAuxFloats;

        // Per-agent action slots [agentIndex * ActionSlotCount + actionId]
        [NativeDisableParallelForRestriction] public NativeArray<ActionRequestStatus> AllActionSlots;
        public int ActionSlotCount;

        // Per-agent blackboard [agentIndex * BBSlotCount + slotIndex]
        [NativeDisableParallelForRestriction] public NativeArray<int> AllBBInts;
        [NativeDisableParallelForRestriction] public NativeArray<float> AllBBFloats;
        [NativeDisableParallelForRestriction] public NativeArray<byte> AllBBBools;
        public int BBSlotCount;

        // LOD tick gating
        [NativeDisableParallelForRestriction] public NativeArray<int> TickCountdowns;
        [ReadOnly] public NativeArray<int> TickIntervals;
        /// <summary>Agents marked inactive will be skipped entirely.</summary>
        [ReadOnly] public NativeArray<byte> ActiveFlags;

        public void Execute(int agentIndex)
        {
            if (ActiveFlags[agentIndex] == 0) return;

            int countdown = TickCountdowns[agentIndex];
            if (countdown > 0)
            {
                TickCountdowns[agentIndex] = countdown - 1;
                return;
            }
            TickCountdowns[agentIndex] = TickIntervals[agentIndex];

            int nodeOff = agentIndex * NodeCount;
            int bbOff = agentIndex * BBSlotCount;
            int actOff = agentIndex * ActionSlotCount;

            TickNode(0, nodeOff, bbOff, actOff);
        }

        private byte TickNode(int nodeIdx, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            int gi = nOff + nodeIdx;

            byte result;
            switch (def.Type)
            {
                case FlatNodeType.Sequence:
                    result = TickSequence(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.Selector:
                    result = TickSelector(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.Parallel:
                    result = TickParallel(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.ReactiveSequence:
                    result = TickReactiveSequence(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.ReactiveSelector:
                    result = TickReactiveSelector(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.Inverter:
                    result = TickDecorator1Child(nodeIdx, nOff, bbOff, actOff, invert: true, forceSuccess: false, forceFail: false);
                    break;
                case FlatNodeType.Succeeder:
                    result = TickDecorator1Child(nodeIdx, nOff, bbOff, actOff, invert: false, forceSuccess: true, forceFail: false);
                    break;
                case FlatNodeType.ForceFailure:
                    result = TickDecorator1Child(nodeIdx, nOff, bbOff, actOff, invert: false, forceSuccess: false, forceFail: true);
                    break;
                case FlatNodeType.Repeater:
                    result = TickRepeater(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.Retry:
                    result = TickRetry(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.Timeout:
                    result = TickTimeout(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.Delay:
                    result = TickDelay(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.RunOnce:
                    result = TickRunOnce(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.CoolDown:
                    result = TickCoolDown(nodeIdx, gi, nOff, bbOff, actOff);
                    break;
                case FlatNodeType.ActionSlot:
                    result = TickAction(nodeIdx, gi, actOff);
                    break;
                case FlatNodeType.BlackboardCondition:
                    result = TickBBCondition(nodeIdx, bbOff);
                    break;
                case FlatNodeType.WaitTicks:
                    result = TickWaitTicks(nodeIdx, gi);
                    break;
                default:
                    result = (byte)RuntimeState.Failure;
                    break;
            }

            AllNodeStates[gi] = result;
            return result;
        }

        // Reset a subtree rooted at nodeIdx to NotEntered
        private void ResetSubtree(int nodeIdx, int nOff, int bbOff, int actOff)
        {
            AllNodeStates[nOff + nodeIdx] = (byte)RuntimeState.NotEntered;
            AllAuxInts[nOff + nodeIdx] = 0;
            AllAuxFloats[nOff + nodeIdx] = 0f;

            var def = TreeNodes[nodeIdx];
            for (int i = 0; i < def.ChildCount; i++)
            {
                int childIdx = ChildIndices[def.ChildStartIndex + i];
                ResetSubtree(childIdx, nOff, bbOff, actOff);
            }
        }

        #region Composites

        private byte TickSequence(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            int current = AllAuxInts[gi];

            for (int i = current; i < def.ChildCount; i++)
            {
                int childIdx = ChildIndices[def.ChildStartIndex + i];
                byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

                if (childResult == (byte)RuntimeState.Running)
                {
                    AllAuxInts[gi] = i;
                    return (byte)RuntimeState.Running;
                }
                if (childResult == (byte)RuntimeState.Failure)
                {
                    AllAuxInts[gi] = 0;
                    return (byte)RuntimeState.Failure;
                }
            }
            AllAuxInts[gi] = 0;
            return (byte)RuntimeState.Success;
        }

        private byte TickSelector(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            int current = AllAuxInts[gi];

            for (int i = current; i < def.ChildCount; i++)
            {
                int childIdx = ChildIndices[def.ChildStartIndex + i];
                byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

                if (childResult == (byte)RuntimeState.Running)
                {
                    AllAuxInts[gi] = i;
                    return (byte)RuntimeState.Running;
                }
                if (childResult == (byte)RuntimeState.Success)
                {
                    AllAuxInts[gi] = 0;
                    return (byte)RuntimeState.Success;
                }
            }
            AllAuxInts[gi] = 0;
            return (byte)RuntimeState.Failure;
        }

        private byte TickParallel(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            int successCount = 0;
            int failCount = 0;

            for (int i = 0; i < def.ChildCount; i++)
            {
                int childIdx = ChildIndices[def.ChildStartIndex + i];
                byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

                if (childResult == (byte)RuntimeState.Success) successCount++;
                else if (childResult == (byte)RuntimeState.Failure) failCount++;
            }

            if (def.FailureThreshold > 0 && failCount >= def.FailureThreshold)
                return (byte)RuntimeState.Failure;
            if (def.SuccessThreshold > 0 && successCount >= def.SuccessThreshold)
                return (byte)RuntimeState.Success;
            if (successCount + failCount >= def.ChildCount)
                return (byte)RuntimeState.Success;

            return (byte)RuntimeState.Running;
        }

        // Re-evaluate from child[0] every tick (reactive pattern)
        private byte TickReactiveSequence(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            for (int i = 0; i < def.ChildCount; i++)
            {
                int childIdx = ChildIndices[def.ChildStartIndex + i];
                byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

                if (childResult == (byte)RuntimeState.Running)
                    return (byte)RuntimeState.Running;
                if (childResult == (byte)RuntimeState.Failure)
                    return (byte)RuntimeState.Failure;
            }
            return (byte)RuntimeState.Success;
        }

        private byte TickReactiveSelector(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            for (int i = 0; i < def.ChildCount; i++)
            {
                int childIdx = ChildIndices[def.ChildStartIndex + i];
                byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

                if (childResult == (byte)RuntimeState.Running)
                    return (byte)RuntimeState.Running;
                if (childResult == (byte)RuntimeState.Success)
                    return (byte)RuntimeState.Success;
            }
            return (byte)RuntimeState.Failure;
        }

        #endregion

        #region Decorators

        /// <summary>Single-child decorator with optional invert / force result.</summary>
        private byte TickDecorator1Child(int nodeIdx, int nOff, int bbOff, int actOff,
            bool invert, bool forceSuccess, bool forceFail)
        {
            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0) return (byte)RuntimeState.Failure;

            int childIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

            if (childResult == (byte)RuntimeState.Running)
                return (byte)RuntimeState.Running;

            if (forceSuccess) return (byte)RuntimeState.Success;
            if (forceFail) return (byte)RuntimeState.Failure;

            if (invert)
            {
                return childResult == (byte)RuntimeState.Success
                    ? (byte)RuntimeState.Failure
                    : (byte)RuntimeState.Success;
            }

            return childResult;
        }

        private byte TickRepeater(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0) return (byte)RuntimeState.Failure;

            int childIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

            if (childResult == (byte)RuntimeState.Running)
                return (byte)RuntimeState.Running;

            int count = AllAuxInts[gi] + 1;
            int maxCount = def.ParamInt; // -1 = infinite

            if (maxCount > 0 && count >= maxCount)
            {
                AllAuxInts[gi] = 0;
                ResetSubtree(childIdx, nOff, bbOff, actOff);
                return (byte)RuntimeState.Success;
            }

            AllAuxInts[gi] = count;
            ResetSubtree(childIdx, nOff, bbOff, actOff);
            return (byte)RuntimeState.Running;
        }

        private byte TickRetry(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0) return (byte)RuntimeState.Failure;

            int childIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

            if (childResult == (byte)RuntimeState.Running)
                return (byte)RuntimeState.Running;
            if (childResult == (byte)RuntimeState.Success)
            {
                AllAuxInts[gi] = 0;
                return (byte)RuntimeState.Success;
            }

            // Child failed — retry
            int attempts = AllAuxInts[gi] + 1;
            int maxAttempts = def.ParamInt; // -1 = infinite

            if (maxAttempts > 0 && attempts >= maxAttempts)
            {
                AllAuxInts[gi] = 0;
                return (byte)RuntimeState.Failure;
            }

            AllAuxInts[gi] = attempts;
            ResetSubtree(childIdx, nOff, bbOff, actOff);
            return (byte)RuntimeState.Running;
        }

        private byte TickTimeout(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0) return (byte)RuntimeState.Failure;

            float elapsed = AllAuxFloats[gi] + DeltaTime;
            AllAuxFloats[gi] = elapsed;

            if (elapsed >= def.ParamFloat)
            {
                int childIdx = ChildIndices[def.ChildStartIndex];
                ResetSubtree(childIdx, nOff, bbOff, actOff);
                AllAuxFloats[gi] = 0f;
                return (byte)RuntimeState.Failure;
            }

            int cIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(cIdx, nOff, bbOff, actOff);

            if (childResult != (byte)RuntimeState.Running)
                AllAuxFloats[gi] = 0f;

            return childResult;
        }

        private byte TickDelay(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            float elapsed = AllAuxFloats[gi] + DeltaTime;
            AllAuxFloats[gi] = elapsed;

            if (elapsed < TreeNodes[nodeIdx].ParamFloat)
                return (byte)RuntimeState.Running;

            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0)
            {
                AllAuxFloats[gi] = 0f;
                return (byte)RuntimeState.Success;
            }

            int childIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

            if (childResult != (byte)RuntimeState.Running)
                AllAuxFloats[gi] = 0f;

            return childResult;
        }

        private byte TickRunOnce(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            // AuxInt: 0=not run, 1=completed success, 2=completed failure
            int cached = AllAuxInts[gi];
            if (cached == 1) return (byte)RuntimeState.Success;
            if (cached == 2) return (byte)RuntimeState.Failure;

            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0) return (byte)RuntimeState.Failure;

            int childIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

            if (childResult == (byte)RuntimeState.Success) AllAuxInts[gi] = 1;
            else if (childResult == (byte)RuntimeState.Failure) AllAuxInts[gi] = 2;

            return childResult;
        }

        private byte TickCoolDown(int nodeIdx, int gi, int nOff, int bbOff, int actOff)
        {
            float remaining = AllAuxFloats[gi];
            if (remaining > 0f)
            {
                AllAuxFloats[gi] = remaining - DeltaTime;
                return (byte)RuntimeState.Failure;
            }

            var def = TreeNodes[nodeIdx];
            if (def.ChildCount == 0) return (byte)RuntimeState.Failure;

            int childIdx = ChildIndices[def.ChildStartIndex];
            byte childResult = TickNode(childIdx, nOff, bbOff, actOff);

            if (childResult == (byte)RuntimeState.Success || childResult == (byte)RuntimeState.Failure)
                AllAuxFloats[gi] = def.ParamFloat; // start cooldown

            return childResult;
        }

        #endregion

        #region Leaf Nodes

        /// <summary>
        /// Action node: reads/writes external action slot.
        /// BT requests action → external system executes → signals completion.
        /// </summary>
        private byte TickAction(int nodeIdx, int gi, int actOff)
        {
            int actionId = TreeNodes[nodeIdx].ParamInt;
            int slotIdx = actOff + actionId;

            var status = AllActionSlots[slotIdx];
            switch (status)
            {
                case ActionRequestStatus.Idle:
                    AllActionSlots[slotIdx] = ActionRequestStatus.Requested;
                    return (byte)RuntimeState.Running;

                case ActionRequestStatus.Requested:
                case ActionRequestStatus.Running:
                    return (byte)RuntimeState.Running;

                case ActionRequestStatus.Success:
                    AllActionSlots[slotIdx] = ActionRequestStatus.Idle;
                    return (byte)RuntimeState.Success;

                case ActionRequestStatus.Failed:
                    AllActionSlots[slotIdx] = ActionRequestStatus.Idle;
                    return (byte)RuntimeState.Failure;

                default:
                    return (byte)RuntimeState.Failure;
            }
        }

        /// <summary>
        /// Blackboard condition: compare BB int value against a constant.
        /// Returns Success if condition holds, Failure otherwise.
        /// </summary>
        private byte TickBBCondition(int nodeIdx, int bbOff)
        {
            var def = TreeNodes[nodeIdx];
            int bbIdx = bbOff + def.BBKey;
            int value = AllBBInts[bbIdx];

            bool passed;
            switch (def.Compare)
            {
                case CompareOp.Equal:        passed = value == def.CompareValue; break;
                case CompareOp.NotEqual:     passed = value != def.CompareValue; break;
                case CompareOp.Less:         passed = value < def.CompareValue;  break;
                case CompareOp.LessEqual:    passed = value <= def.CompareValue; break;
                case CompareOp.Greater:      passed = value > def.CompareValue;  break;
                case CompareOp.GreaterEqual: passed = value >= def.CompareValue; break;
                default:                     passed = false; break;
            }

            return passed ? (byte)RuntimeState.Success : (byte)RuntimeState.Failure;
        }

        /// <summary>Wait for N ticks, then return Success.</summary>
        private byte TickWaitTicks(int nodeIdx, int gi)
        {
            int count = AllAuxInts[gi] + 1;
            int target = TreeNodes[nodeIdx].ParamInt;

            if (count >= target)
            {
                AllAuxInts[gi] = 0;
                return (byte)RuntimeState.Success;
            }

            AllAuxInts[gi] = count;
            return (byte)RuntimeState.Running;
        }

        #endregion
    }
}
