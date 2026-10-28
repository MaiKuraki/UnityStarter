using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace CycloneGames.Scheduling.Core
{
    /// <summary>
    /// Fixed-capacity, owner-thread FIFO scheduler for finite cooperative work.
    /// The time budget limits new Step calls; it cannot preempt a running Step.
    /// </summary>
    public sealed class WorkScheduler : IDisposable
    {
        private readonly WorkSlot[] _slots;
        private readonly int[] _freeSlots;
        private readonly ISchedulerClock _clock;
        private readonly int _ownerThreadId;

        private int _freeCount;
        private int _head;
        private int _tail;
        private int _outstandingCount;
        private bool _isInCallback;
        private bool _isTicking;
        private bool _isShutdown;

        private long _acceptedCount;
        private long _rejectedCount;
        private long _completedCount;
        private long _cancelledCount;
        private long _faultedCount;
        private long _stepCount;
        private long _tickCount;
        private long _overBudgetTickCount;
        private double _lastTickMilliseconds;
        private double _maxTickMilliseconds;

        public WorkScheduler(int capacity)
            : this(capacity, StopwatchSchedulerClock.Instance)
        {
        }

        internal WorkScheduler(int capacity, ISchedulerClock clock, uint initialGeneration = 1)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (clock == null)
            {
                throw new ArgumentNullException(nameof(clock));
            }

            _slots = new WorkSlot[capacity];
            _freeSlots = new int[capacity];
            _clock = clock;
            _ownerThreadId = Environment.CurrentManagedThreadId;
            _head = -1;
            _tail = -1;
            _freeCount = capacity;

            uint generation = initialGeneration == 0 ? 1u : initialGeneration;
            for (int index = 0; index < capacity; index++)
            {
                _slots[index].Next = -1;
                _slots[index].Previous = -1;
                _slots[index].Generation = generation;
                _freeSlots[index] = capacity - 1 - index;
            }
        }

        public int Capacity => _slots.Length;
        public int OutstandingCount => _outstandingCount;
        public bool IsShutdown => _isShutdown;
        public bool IsShutdownCompleted => _isShutdown && _outstandingCount == 0;

        /// <summary>
        /// Accepts ownership of one work registration. On rejection, ownership remains with the caller
        /// and no termination callback is made. A work item instance must not be queued twice at once.
        /// </summary>
        public bool TrySchedule(IWorkItem item, out WorkItemHandle handle)
        {
            EnsurePublicAccess();
            if (item == null)
            {
                throw new ArgumentNullException(nameof(item));
            }

            if (_isShutdown || _freeCount == 0)
            {
                _rejectedCount++;
                handle = default;
                return false;
            }

            int slot = _freeSlots[--_freeCount];
            _slots[slot].Item = item;
            AppendToQueue(slot);
            _outstandingCount++;
            _acceptedCount++;
            handle = new WorkItemHandle(slot, _slots[slot].Generation);
            return true;
        }

        /// <summary>Cancels a queued registration and synchronously issues its terminal callback.</summary>
        public bool Cancel(WorkItemHandle handle)
        {
            EnsurePublicAccess();
            if (!TryGetOccupiedSlot(handle, out int slot))
            {
                return false;
            }

            DetachFromQueue(slot);
            Exception terminationFailure = Terminate(slot, WorkTerminationReason.Cancelled);
            if (terminationFailure != null)
            {
                ExceptionDispatchInfo.Capture(terminationFailure).Throw();
            }

            return true;
        }

        /// <summary>
        /// Runs each eligible item at most once. Running items are appended to the tail for a later Tick.
        /// A positive infinity time budget disables clock-based admission while retaining the Step limit.
        /// </summary>
        public int Tick(int maxSteps, double softBudgetMs)
        {
            EnsurePublicAccess();
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
            int steps = 0;
            long startedAt = _clock.GetTimestamp();
            _tickCount++;
            _isTicking = true;

            try
            {
                while (steps < maxSteps && steps < eligibleCount)
                {
                    if (steps > 0
                        && !double.IsPositiveInfinity(softBudgetMs)
                        && HasExpired(startedAt, softBudgetMs))
                    {
                        break;
                    }

                    int slot = PopHead();
                    bool completed = false;
                    try
                    {
                        steps++;
                        _stepCount++;

                        WorkStepResult result;
                        _isInCallback = true;
                        try
                        {
                            result = _slots[slot].Item.Step();
                        }
                        finally
                        {
                            _isInCallback = false;
                        }

                        if (result == WorkStepResult.Completed)
                        {
                            completed = true;
                        }
                        else if (result != WorkStepResult.Running)
                        {
                            throw new InvalidOperationException(
                                "IWorkItem.Step returned an undefined WorkStepResult value.");
                        }
                    }
                    catch (Exception stepFailure)
                    {
                        Exception terminationFailure = Terminate(slot, WorkTerminationReason.Faulted);
                        if (terminationFailure != null)
                        {
                            throw new AggregateException(
                                "A work item failed and its termination callback also failed.",
                                stepFailure,
                                terminationFailure);
                        }

                        throw;
                    }

                    if (completed)
                    {
                        Exception terminationFailure = Terminate(slot, WorkTerminationReason.Completed);
                        if (terminationFailure != null)
                        {
                            ExceptionDispatchInfo.Capture(terminationFailure).Throw();
                        }
                    }
                    else
                    {
                        AppendToQueue(slot);
                    }
                }

                return steps;
            }
            finally
            {
                RecordTick(startedAt, softBudgetMs);
                _isTicking = false;
            }
        }

        /// <summary>
        /// Stops admission and cancels every outstanding registration without further Step calls.
        /// All callbacks are attempted before an aggregate failure is thrown.
        /// </summary>
        public void Shutdown()
        {
            EnsurePublicAccess();
            ShutdownCore(int.MaxValue);
        }

        /// <summary>
        /// Stops admission and terminates at most maxItems registrations. Returns the number terminated.
        /// A zero budget begins shutdown without cancelling an item. Later calls continue the same drain.
        /// </summary>
        public int ShutdownStep(int maxItems)
        {
            EnsurePublicAccess();
            if (maxItems < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(maxItems));
            }

            return ShutdownCore(maxItems);
        }

        private int ShutdownCore(int maxItems)
        {
            _isShutdown = true;
            int processed = 0;
            List<Exception> failures = null;
            while (processed < maxItems && _head >= 0)
            {
                int slot = PopHead();
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
            }

            if (failures != null)
            {
                throw new AggregateException("One or more work items failed during scheduler shutdown.", failures);
            }

            return processed;
        }

        public void Dispose()
        {
            Shutdown();
        }

        public SchedulerSnapshot GetSnapshot()
        {
            EnsurePublicAccess();
            return new SchedulerSnapshot(
                _slots.Length,
                _outstandingCount,
                _acceptedCount,
                _rejectedCount,
                _completedCount,
                _cancelledCount,
                _faultedCount,
                _stepCount,
                _tickCount,
                _overBudgetTickCount,
                _lastTickMilliseconds,
                _maxTickMilliseconds,
                _isShutdown,
                _isShutdown && _outstandingCount == 0);
        }

        private bool TryGetOccupiedSlot(WorkItemHandle handle, out int slot)
        {
            slot = handle.Slot;
            return handle.Generation != 0
                && slot >= 0
                && slot < _slots.Length
                && _slots[slot].Item != null
                && _slots[slot].Generation == handle.Generation;
        }

        private void AppendToQueue(int slot)
        {
            _slots[slot].Next = -1;
            _slots[slot].Previous = _tail;
            if (_tail >= 0)
            {
                _slots[_tail].Next = slot;
            }
            else
            {
                _head = slot;
            }

            _tail = slot;
        }

        private int PopHead()
        {
            int slot = _head;
            DetachFromQueue(slot);
            return slot;
        }

        private void DetachFromQueue(int slot)
        {
            int previous = _slots[slot].Previous;
            int next = _slots[slot].Next;
            if (previous >= 0)
            {
                _slots[previous].Next = next;
            }
            else
            {
                _head = next;
            }

            if (next >= 0)
            {
                _slots[next].Previous = previous;
            }
            else
            {
                _tail = previous;
            }

            _slots[slot].Next = -1;
            _slots[slot].Previous = -1;
        }

        private Exception Terminate(int slot, WorkTerminationReason reason)
        {
            IWorkItem item = _slots[slot].Item;
            _slots[slot].Item = null;
            _slots[slot].Next = -1;
            _slots[slot].Previous = -1;
            _slots[slot].Generation = NextGeneration(_slots[slot].Generation);
            _freeSlots[_freeCount++] = slot;
            _outstandingCount--;

            if (reason == WorkTerminationReason.Completed)
            {
                _completedCount++;
            }
            else if (reason == WorkTerminationReason.Faulted)
            {
                _faultedCount++;
            }
            else
            {
                _cancelledCount++;
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

        private bool HasExpired(long startedAt, double softBudgetMs)
        {
            long elapsedTicks = _clock.GetTimestamp() - startedAt;
            double elapsedMs = _clock.ToMilliseconds(elapsedTicks);
            return elapsedMs >= softBudgetMs;
        }

        private void RecordTick(long startedAt, double softBudgetMs)
        {
            double elapsedMs = _clock.ToMilliseconds(_clock.GetTimestamp() - startedAt);
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

        private void EnsurePublicAccess()
        {
            if (Environment.CurrentManagedThreadId != _ownerThreadId)
            {
                throw new InvalidOperationException("WorkScheduler is confined to its owner thread.");
            }

            if (_isInCallback || _isTicking)
            {
                throw new InvalidOperationException(
                    "Public scheduler operations cannot be re-entered from Step or terminal callbacks.");
            }
        }

        private static uint NextGeneration(uint generation)
        {
            unchecked
            {
                uint next = generation + 1;
                return next == 0 ? 1u : next;
            }
        }
    }
}
