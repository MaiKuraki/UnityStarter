using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Runtime.ExceptionServices;
using System.Threading;

namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// Fixed-capacity, owner-thread scheduler with lane priority and a bounded starvation window.
    /// The time budget bounds admission of new Step calls; it cannot preempt a running Step.
    /// A fault terminates only the failing registration, and is rethrown after the tick completes.
    /// </summary>
    public sealed class WorkScheduler : IDisposable
    {
        /// <summary>Matches <see cref="WorkLane"/>.</summary>
        public const int LaneCount = 4;

        public const int DefaultStarvationLimitTicks = 4;

        private static int s_nextSchedulerId;

        private readonly WorkSlot[] _slots;
        private readonly ISchedulerClock _clock;
        private readonly int _ownerThreadId;
        private readonly uint _schedulerId;
        private readonly int _starvationLimitTicks;

        // Indexed by (int)WorkLane. Lane 0 is drained first.
        private readonly int[] _laneHead;
        private readonly int[] _laneTail;
        private readonly int[] _laneOutstanding;
        private readonly int[] _laneStarvedTicks;

#if DEBUG
        private readonly DuplicateRegistrationGuard _duplicateGuard;
#endif

        private int _freeHead;
        private int _freeCount;
        private int _outstandingCount;

        private bool _isInCallback;
        private bool _isTicking;
        private bool _isShutdown;

        private long _acceptedCount;
        private long _rejectedCount;
        private long _completedCount;
        private long _cancelledCount;
        private long _faultedCount;
        private long _shutdownCount;
        private long _stepCount;
        private long _tickCount;
        private long _overBudgetTickCount;

        private double _lastTickMilliseconds;
        private double _maxTickMilliseconds;
        private bool _trackStepTiming;
        private double _slowestStepMilliseconds;
        private WorkItemHandle _slowestStep;

        public WorkScheduler(int capacity)
            : this(capacity, StopwatchSchedulerClock.Instance, 1u, DefaultStarvationLimitTicks)
        {
        }

        public WorkScheduler(int capacity, int starvationLimitTicks)
            : this(capacity, StopwatchSchedulerClock.Instance, 1u, starvationLimitTicks)
        {
        }

        internal WorkScheduler(int capacity, ISchedulerClock clock, uint initialGeneration = 1, int starvationLimitTicks = DefaultStarvationLimitTicks)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            if (starvationLimitTicks < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(starvationLimitTicks));
            }

            _slots = new WorkSlot[capacity];
            _clock = clock;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _starvationLimitTicks = starvationLimitTicks;

            uint schedulerId = unchecked((uint)Interlocked.Increment(ref s_nextSchedulerId));
            _schedulerId = schedulerId == 0 ? 1u : schedulerId;

            _laneHead = new int[LaneCount];
            _laneTail = new int[LaneCount];
            _laneOutstanding = new int[LaneCount];
            _laneStarvedTicks = new int[LaneCount];
            for (int lane = 0; lane < LaneCount; lane++)
            {
                _laneHead[lane] = -1;
                _laneTail[lane] = -1;
            }

            // A slot is either queued or free, so Next is shared. Handout stays ascending and
            // release is LIFO, which keeps the live working set contiguous.
            uint generation = initialGeneration == 0 ? 1u : initialGeneration;
            for (int index = 0; index < capacity; index++)
            {
                _slots[index].Item = null;
                _slots[index].Next = index + 1 < capacity ? index + 1 : -1;
                _slots[index].Previous = -1;
                _slots[index].Generation = generation;
                _slots[index].Lane = (byte)WorkLane.Normal;
            }

            _freeHead = 0;
            _freeCount = capacity;

#if DEBUG
            _duplicateGuard = new DuplicateRegistrationGuard(capacity);
#endif
        }

        public int Capacity => _slots.Length;

        public int OutstandingCount => _outstandingCount;

        public bool IsShutdown => _isShutdown;

        public bool IsShutdownCompleted => _isShutdown && _outstandingCount == 0;

        public int StarvationLimitTicks => _starvationLimitTicks;

        /// <summary>
        /// Enables per-step cost attribution. Off by default: attribution adds a second clock
        /// conversion and a write per step, which is a large share of the per-step overhead.
        /// Enable it while diagnosing spikes, then turn it off again.
        /// </summary>
        public bool TrackStepTiming
        {
            get => _trackStepTiming;
            set => _trackStepTiming = value;
        }

        /// <summary>Identity embedded in every handle this scheduler issues.</summary>
        public uint SchedulerId => _schedulerId;

        public int GetLaneOutstanding(WorkLane lane)
        {
            return _laneOutstanding[(int)ValidateLane(lane)];
        }

        /// <summary>Accepts one registration on <see cref="WorkLane.Normal"/>.</summary>
        public bool TrySchedule(IWorkItem item, out WorkItemHandle handle)
        {
            return TrySchedule(item, WorkLane.Normal, out handle);
        }

        /// <summary>
        /// Accepts ownership of one registration. On rejection ownership stays with the caller
        /// and no termination callback is made. An instance must not be scheduled twice at once.
        /// </summary>
        public bool TrySchedule(IWorkItem item, WorkLane lane, out WorkItemHandle handle)
        {
            EnsureAccess(allowDuringTick: false);
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            int laneIndex = (int)ValidateLane(lane);

            if (_isShutdown || _freeCount == 0)
            {
                _rejectedCount++;
                handle = default;
                return false;
            }

#if DEBUG
            // Reference-identity guard, compiled out of Release Player builds.
            if (!_duplicateGuard.TryAdd(item))
            {
                throw new InvalidOperationException(
                    "The same IWorkItem instance is already scheduled. Duplicate registration would cause "
                    + "two Step calls per Tick and two OnTerminated callbacks.");
            }
#endif

            int slot = _freeHead;
            _freeHead = _slots[slot].Next;
            _freeCount--;
            _slots[slot].Next = -1;
            _slots[slot].Item = item;
            _slots[slot].Lane = (byte)laneIndex;

            AppendToLane(laneIndex, slot);
            _laneOutstanding[laneIndex]++;
            _outstandingCount++;
            _acceptedCount++;
            handle = new WorkItemHandle(slot, _slots[slot].Generation, _schedulerId);
            return true;
        }

        /// <summary>Cancels a queued registration and synchronously issues its terminal callback.</summary>
        public bool Cancel(WorkItemHandle handle)
        {
            EnsureAccess(allowDuringTick: false);
            if (!TryResolve(handle, out int slot))
            {
                return false;
            }

            DetachFromLane(_slots[slot].Lane, slot);
            Exception terminationFailure = Terminate(slot, WorkTerminationReason.Cancelled);
            if (terminationFailure != null)
            {
                ExceptionDispatchInfo.Capture(terminationFailure).Throw();
            }

            return true;
        }

        /// <summary>
        /// Runs each eligible item at most once, draining higher lanes first. Running items are
        /// appended to their lane tail for a later Tick. A positive infinity budget disables
        /// clock-based admission while retaining the step limit.
        /// </summary>
        public int Tick(int maxSteps, double softBudgetMs)
        {
            EnsureAccess(allowDuringTick: false);
            if (maxSteps < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxSteps));
            }

            if (double.IsNaN(softBudgetMs) || double.IsNegativeInfinity(softBudgetMs) || softBudgetMs < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(softBudgetMs));
            }

            if (_isShutdown || maxSteps == 0 || softBudgetMs == 0 || _outstandingCount == 0)
            {
                return 0;
            }

            int eligibleCount = _outstandingCount;
            bool budgetFinite = !double.IsPositiveInfinity(softBudgetMs);
            bool trackTiming = budgetFinite && _trackStepTiming;
            int steps = 0;
            int servicedMask = 0;
            List<Exception> faults = null;

            long startedAt = _clock.GetTimestamp();
            long sampleAt = startedAt;
            int pendingCostSlot = -1;
            uint pendingCostGeneration = 0u;

            _tickCount++;
            _isTicking = true;

            try
            {
                int lane = FindStarvedLane();
                int boostedLane = lane;

                while (steps < maxSteps && steps < eligibleCount)
                {
                    if (budgetFinite)
                    {
                        long now = _clock.GetTimestamp();
                        if (trackTiming && pendingCostSlot >= 0)
                        {
                            RecordStepCost(pendingCostSlot, pendingCostGeneration, _clock.ToMilliseconds(now - sampleAt));
                        }

                        sampleAt = now;
                        if (steps > 0 && _clock.ToMilliseconds(now - startedAt) >= softBudgetMs)
                        {
                            break;
                        }
                    }

                    if (lane < 0)
                    {
                        lane = FindHighestNonEmptyLane();
                        if (lane < 0)
                        {
                            break;
                        }
                    }

                    int slot = PopHead(lane);
                    servicedMask |= 1 << lane;
                    steps++;
                    _stepCount++;
                    if (trackTiming)
                    {
                        pendingCostSlot = slot;
                        pendingCostGeneration = _slots[slot].Generation;
                    }

                    WorkStepResult result = WorkStepResult.Running;
                    Exception stepFailure = null;
                    _isInCallback = true;
                    try
                    {
                        result = _slots[slot].Item.Step();
                    }
                    catch (Exception failure)
                    {
                        stepFailure = failure;
                    }
                    finally
                    {
                        _isInCallback = false;
                    }

                    if (stepFailure != null)
                    {
                        TerminateCollected(slot, WorkTerminationReason.Faulted, stepFailure, ref faults);
                    }
                    else if (result == WorkStepResult.Completed)
                    {
                        TerminateCollected(slot, WorkTerminationReason.Completed, null, ref faults);
                    }
                    else if (result != WorkStepResult.Running)
                    {
                        TerminateCollected(
                            slot,
                            WorkTerminationReason.Faulted,
                            new InvalidOperationException("IWorkItem.Step returned an undefined WorkStepResult value."),
                            ref faults);
                    }
                    else
                    {
                        AppendToLane(lane, slot);
                    }

                    if (_laneOutstanding[lane] == 0)
                    {
                        lane = -1;
                    }
                    else if (lane == boostedLane)
                    {
                        // A starvation boost grants exactly one step, then priority order resumes.
                        lane = -1;
                        boostedLane = -1;
                    }
                }

                if (faults != null)
                {
                    if (faults.Count == 1)
                    {
                        ExceptionDispatchInfo.Capture(faults[0]).Throw();
                    }

                    throw new AggregateException("One or more work items failed during Tick.", faults);
                }

                return steps;
            }
            finally
            {
                long endedAt = _clock.GetTimestamp();
                if (trackTiming && pendingCostSlot >= 0)
                {
                    RecordStepCost(pendingCostSlot, pendingCostGeneration, _clock.ToMilliseconds(endedAt - sampleAt));
                }

                RecordTick(_clock.ToMilliseconds(endedAt - startedAt), softBudgetMs);
                UpdateStarvation(servicedMask);
                _isTicking = false;
            }
        }

        /// <summary>
        /// Stops admission and cancels every outstanding registration without further Step calls.
        /// All callbacks are attempted before an aggregate failure is thrown.
        /// </summary>
        public void Shutdown()
        {
            EnsureAccess(allowDuringTick: false);
            ShutdownCore(int.MaxValue);
        }

        /// <summary>
        /// Stops admission and terminates at most maxItems registrations. Returns the number
        /// terminated. A zero budget begins shutdown without cancelling an item. Later calls
        /// continue the same drain.
        /// </summary>
        public int ShutdownStep(int maxItems)
        {
            EnsureAccess(allowDuringTick: false);
            if (maxItems < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            return ShutdownCore(maxItems);
        }

        /// <summary>
        /// Drains the remaining registrations. Callback failures are reported by
        /// <see cref="Shutdown"/> and swallowed here; misuse (wrong thread, re-entrant call)
        /// still throws.
        /// </summary>
        public void Dispose()
        {
            EnsureAccess(allowDuringTick: false);
            try
            {
                ShutdownCore(int.MaxValue);
            }
            catch (AggregateException)
            {
                // Callback failures are a Shutdown contract, not a Dispose one.
            }
        }

        /// <summary>
        /// Reads counters. Safe on the owner thread during a Tick, because nothing here mutates state.
        /// </summary>
        public SchedulerSnapshot GetSnapshot()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("WorkScheduler is confined to its owner thread.");
            }

            return new SchedulerSnapshot(
                _slots.Length,
                _outstandingCount,
                _acceptedCount,
                _rejectedCount,
                _completedCount,
                _cancelledCount,
                _faultedCount,
                _shutdownCount,
                _stepCount,
                _tickCount,
                _overBudgetTickCount,
                _lastTickMilliseconds,
                _maxTickMilliseconds,
                _slowestStepMilliseconds,
                _slowestStep,
                _isShutdown,
                _isShutdown && _outstandingCount == 0);
        }

        private int ShutdownCore(int maxItems)
        {
            _isShutdown = true;
            int processed = 0;
            List<Exception> failures = null;

            int lane = FindHighestNonEmptyLane();
            while (processed < maxItems && lane >= 0)
            {
                int slot = PopHead(lane);
                Exception failure = Terminate(slot, WorkTerminationReason.Shutdown);
                processed++;
                if (failure != null)
                {
                    if (failures == null)
                    {
                        failures = new List<Exception>();
                    }

                    failures.Add(failure);
                }

                if (_laneOutstanding[lane] == 0)
                {
                    lane = FindHighestNonEmptyLane();
                }
            }

            if (failures != null)
            {
                throw new AggregateException("One or more work items failed during scheduler shutdown.", failures);
            }

            return processed;
        }

        private void TerminateCollected(int slot, WorkTerminationReason reason, Exception primary, ref List<Exception> faults)
        {
            Exception callbackFailure = Terminate(slot, reason);
            if (primary != null)
            {
                AddFault(ref faults, primary);
            }

            if (callbackFailure != null)
            {
                AddFault(ref faults, callbackFailure);
            }
        }

        private static void AddFault(ref List<Exception> faults, Exception failure)
        {
            if (faults == null)
            {
                faults = new List<Exception>(2);
            }

            faults.Add(failure);
        }

        private bool TryResolve(WorkItemHandle handle, out int slot)
        {
            slot = handle.Slot;
            return handle.Generation != 0
                && handle.SchedulerId == _schedulerId
                && slot >= 0
                && slot < _slots.Length
                && _slots[slot].Item != null
                && _slots[slot].Generation == handle.Generation;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void AppendToLane(int lane, int slot)
        {
            int tail = _laneTail[lane];
            _slots[slot].Next = -1;
            _slots[slot].Previous = tail;
            if (tail >= 0)
            {
                _slots[tail].Next = slot;
            }
            else
            {
                _laneHead[lane] = slot;
            }

            _laneTail[lane] = slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int PopHead(int lane)
        {
            int slot = _laneHead[lane];
            DetachFromLane(lane, slot);
            return slot;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private void DetachFromLane(int lane, int slot)
        {
            int previous = _slots[slot].Previous;
            int next = _slots[slot].Next;
            if (previous >= 0)
            {
                _slots[previous].Next = next;
            }
            else
            {
                _laneHead[lane] = next;
            }

            if (next >= 0)
            {
                _slots[next].Previous = previous;
            }
            else
            {
                _laneTail[lane] = previous;
            }

            _slots[slot].Next = -1;
            _slots[slot].Previous = -1;
        }

        private Exception Terminate(int slot, WorkTerminationReason reason)
        {
            IWorkItem item = _slots[slot].Item;
            int lane = _slots[slot].Lane;

            _slots[slot].Item = null;
            _slots[slot].Previous = -1;
            _slots[slot].Generation = NextGeneration(_slots[slot].Generation);
            _slots[slot].Next = _freeHead;
            _freeHead = slot;
            _freeCount++;
            _outstandingCount--;
            _laneOutstanding[lane]--;

#if DEBUG
            _duplicateGuard.Remove(item);
#endif

            if (reason == WorkTerminationReason.Completed)
            {
                _completedCount++;
            }
            else if (reason == WorkTerminationReason.Faulted)
            {
                _faultedCount++;
            }
            else if (reason == WorkTerminationReason.Cancelled)
            {
                _cancelledCount++;
            }
            else
            {
                _shutdownCount++;
            }

            _isInCallback = true;
            try
            {
                item.OnTerminated(reason);
                return null;
            }
            catch (Exception failure)
            {
                return failure;
            }
            finally
            {
                _isInCallback = false;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindHighestNonEmptyLane()
        {
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if (_laneOutstanding[lane] > 0)
                {
                    return lane;
                }
            }

            return -1;
        }

        private int FindStarvedLane()
        {
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if (_laneOutstanding[lane] > 0 && _laneStarvedTicks[lane] >= _starvationLimitTicks)
                {
                    return lane;
                }
            }

            return -1;
        }

        private void UpdateStarvation(int servicedMask)
        {
            for (int lane = 0; lane < LaneCount; lane++)
            {
                if ((servicedMask & (1 << lane)) != 0)
                {
                    _laneStarvedTicks[lane] = 0;
                }
                else if (_laneOutstanding[lane] > 0)
                {
                    // Saturates at the threshold, which is the only value ever compared, so the
                    // counter stays bounded independently of how the boost policy evolves.
                    if (_laneStarvedTicks[lane] < _starvationLimitTicks)
                    {
                        _laneStarvedTicks[lane]++;
                    }
                }
                else
                {
                    _laneStarvedTicks[lane] = 0;
                }
            }
        }

        private void RecordStepCost(int slot, uint generation, double milliseconds)
        {
            if (milliseconds <= _slowestStepMilliseconds)
            {
                return;
            }

            _slowestStepMilliseconds = milliseconds;
            _slowestStep = new WorkItemHandle(slot, generation, _schedulerId);
        }

        private void RecordTick(double elapsedMs, double softBudgetMs)
        {
            if (elapsedMs < 0)
            {
                elapsedMs = 0;
            }

            _lastTickMilliseconds = elapsedMs;
            if (elapsedMs > _maxTickMilliseconds)
            {
                _maxTickMilliseconds = elapsedMs;
            }

            if (!double.IsPositiveInfinity(softBudgetMs) && elapsedMs > softBudgetMs)
            {
                _overBudgetTickCount++;
            }
        }

        private void EnsureAccess(bool allowDuringTick)
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("WorkScheduler is confined to its owner thread.");
            }

            if (_isInCallback || (!allowDuringTick && _isTicking))
            {
                throw new InvalidOperationException(
                    "Public scheduler operations cannot be re-entered from Step or terminal callbacks.");
            }
        }

        private static WorkLane ValidateLane(WorkLane lane)
        {
            if ((byte)lane >= LaneCount)
            {
                throw new ArgumentOutOfRangeException(nameof(lane));
            }

            return lane;
        }

        private static uint NextGeneration(uint generation)
        {
            unchecked
            {
                uint next = generation + 1;
                return next == 0 ? 1u : next;
            }
        }

#if DEBUG
        /// <summary>
        /// Open-addressing set keyed by reference identity. Sized once at construction, so it
        /// adds no steady-state allocation, and it is absent from Release Player builds.
        /// </summary>
        private sealed class DuplicateRegistrationGuard
        {
            private readonly IWorkItem[] _entries;
            private readonly int _mask;

            public DuplicateRegistrationGuard(int capacity)
            {
                int size = 8;
                while (size < capacity * 2)
                {
                    size <<= 1;
                }

                _entries = new IWorkItem[size];
                _mask = size - 1;
            }

            public bool TryAdd(IWorkItem item)
            {
                int index = RuntimeHelpers.GetHashCode(item) & _mask;
                while (true)
                {
                    IWorkItem existing = _entries[index];
                    if (existing == null)
                    {
                        _entries[index] = item;
                        return true;
                    }

                    if (ReferenceEquals(existing, item))
                    {
                        return false;
                    }

                    index = (index + 1) & _mask;
                }
            }

            public void Remove(IWorkItem item)
            {
                int index = RuntimeHelpers.GetHashCode(item) & _mask;
                while (true)
                {
                    IWorkItem existing = _entries[index];
                    if (existing == null)
                    {
                        return;
                    }

                    if (!ReferenceEquals(existing, item))
                    {
                        index = (index + 1) & _mask;
                        continue;
                    }

                    _entries[index] = null;

                    // Backward-shift the rest of the probe chain so later lookups stay reachable.
                    int next = (index + 1) & _mask;
                    while (_entries[next] != null)
                    {
                        IWorkItem moved = _entries[next];
                        _entries[next] = null;
                        int target = RuntimeHelpers.GetHashCode(moved) & _mask;
                        while (_entries[target] != null)
                        {
                            target = (target + 1) & _mask;
                        }

                        _entries[target] = moved;
                        next = (next + 1) & _mask;
                    }

                    return;
                }
            }
        }
#endif
    }
}
