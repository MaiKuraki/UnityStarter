using System;
using System.Threading;

namespace CycloneGames.Logger
{
    internal sealed class BoundedLogQueue : IDisposable
    {
        private struct Entry
        {
            internal LogMessage Message;
            internal int Characters;
        }

        private readonly object _syncRoot = new object();
        private readonly Entry[] _entries;
        private readonly LoggerProcessingOptions _options;

        private int _head;
        private int _count;
        private int _reservedCount;
        private int _reservedCharacters;
        private int _queuedCharacters;
        private int _inFlightCount;
        private int _inFlightCharacters;
        private int _peakQueuedCount;
        private int _peakQueuedCharacters;
        private bool _addingCompleted;
        private bool _disposed;

        private long _enqueuedCount;
        private long _droppedNewestCount;
        private long _droppedOldestCount;
        private long _droppedCriticalCount;
        private long _rejectedAfterStopCount;
        private long _processedCount;

        internal bool IsStopped
        {
            get
            {
                lock (_syncRoot)
                {
                    return _addingCompleted && IsIdleNoLock();
                }
            }
        }

        internal BoundedLogQueue(LoggerProcessingOptions options)
        {
            _options = LoggerProcessingOptions.CreateValidated(options);
            _entries = new Entry[_options.MaxQueuedMessages];
        }

        internal bool TryReserve(
            LogLevel level,
            int estimatedCharacters,
            bool allowEviction,
            out int reservedCharacters)
        {
            reservedCharacters = Math.Min(Math.Max(estimatedCharacters, 0), _options.MaxQueuedCharacters);
            int startTick = Environment.TickCount;

            lock (_syncRoot)
            {
                while (true)
                {
                    if (_addingCompleted || _disposed)
                    {
                        _rejectedAfterStopCount++;
                        if (level >= _options.CriticalLevel)
                        {
                            _droppedCriticalCount++;
                        }

                        return false;
                    }

                    if (estimatedCharacters > _options.MaxQueuedCharacters)
                    {
                        RecordDropNewestNoLock(level);
                        return false;
                    }

                    if (HasCapacityNoLock(level, reservedCharacters))
                    {
                        _reservedCount++;
                        _reservedCharacters += reservedCharacters;
                        return true;
                    }

                    if (allowEviction && TryEvictForIncomingNoLock(level))
                    {
                        continue;
                    }

                    if (_options.OverflowPolicy != LogQueueOverflowPolicy.Block
                        || _options.EnqueueBlockTimeoutMs == 0)
                    {
                        RecordDropNewestNoLock(level);
                        return false;
                    }

                    int remaining = GetRemainingTimeout(startTick, _options.EnqueueBlockTimeoutMs);
                    if (remaining <= 0)
                    {
                        RecordDropNewestNoLock(level);
                        return false;
                    }

                    Monitor.Wait(_syncRoot, remaining);
                }
            }
        }

        internal bool TryCommit(LogMessage message, int reservedCharacters, int actualCharacters)
        {
            if (message == null)
            {
                CancelReservation(reservedCharacters);
                return false;
            }

            actualCharacters = Math.Max(actualCharacters, 0);
            lock (_syncRoot)
            {
                ReleaseReservationNoLock(reservedCharacters);
                if (_addingCompleted || _disposed)
                {
                    _rejectedAfterStopCount++;
                    if (message.Level >= _options.CriticalLevel)
                    {
                        _droppedCriticalCount++;
                    }

                    Monitor.PulseAll(_syncRoot);
                    return false;
                }

                if (actualCharacters > _options.MaxQueuedCharacters)
                {
                    RecordDropNewestNoLock(message.Level);
                    Monitor.PulseAll(_syncRoot);
                    return false;
                }

                if (actualCharacters > reservedCharacters)
                {
                    RecordDropNewestNoLock(message.Level);
                    Monitor.PulseAll(_syncRoot);
                    return false;
                }

                while (!HasCapacityNoLock(message.Level, actualCharacters))
                {
                    if (!TryEvictForIncomingNoLock(message.Level))
                    {
                        RecordDropNewestNoLock(message.Level);
                        Monitor.PulseAll(_syncRoot);
                        return false;
                    }
                }

                int tail = (_head + _count) % _entries.Length;
                _entries[tail].Message = message;
                _entries[tail].Characters = actualCharacters;
                _count++;
                _queuedCharacters += actualCharacters;
                _enqueuedCount++;
                int retainedCount = _count + _inFlightCount;
                if (retainedCount > _peakQueuedCount)
                {
                    _peakQueuedCount = retainedCount;
                }

                int retainedCharacters = _queuedCharacters + _inFlightCharacters;
                if (retainedCharacters > _peakQueuedCharacters)
                {
                    _peakQueuedCharacters = retainedCharacters;
                }

                Monitor.PulseAll(_syncRoot);
                return true;
            }
        }

        internal void CancelReservation(int reservedCharacters)
        {
            lock (_syncRoot)
            {
                ReleaseReservationNoLock(reservedCharacters);
                Monitor.PulseAll(_syncRoot);
            }
        }

        internal bool TryDequeue(out LogMessage message, out int characters)
        {
            lock (_syncRoot)
            {
                return TryDequeueNoLock(out message, out characters);
            }
        }

        internal bool WaitDequeue(int timeoutMs, out LogMessage message, out int characters, out bool addingCompleted)
        {
            lock (_syncRoot)
            {
                if (_count == 0 && !_addingCompleted && !_disposed)
                {
                    Monitor.Wait(_syncRoot, timeoutMs);
                }

                if (TryDequeueNoLock(out message, out characters))
                {
                    addingCompleted = false;
                    return true;
                }

                addingCompleted = _addingCompleted || _disposed;
                characters = 0;
                return false;
            }
        }

        internal void CompleteProcessing(int characters)
        {
            lock (_syncRoot)
            {
                if (_inFlightCount > 0)
                {
                    _inFlightCount--;
                }

                _inFlightCharacters -= Math.Max(characters, 0);
                if (_inFlightCharacters < 0)
                {
                    _inFlightCharacters = 0;
                }

                _processedCount++;
                Monitor.PulseAll(_syncRoot);
            }
        }

        internal void CompleteAdding()
        {
            lock (_syncRoot)
            {
                _addingCompleted = true;
                Monitor.PulseAll(_syncRoot);
            }
        }

        internal bool WaitUntilIdle(int timeoutMs)
        {
            int startTick = Environment.TickCount;
            lock (_syncRoot)
            {
                while (!IsIdleNoLock())
                {
                    int remaining = GetRemainingTimeout(startTick, timeoutMs);
                    if (remaining <= 0)
                    {
                        return false;
                    }

                    Monitor.Wait(_syncRoot, remaining);
                }

                return true;
            }
        }

        internal void DrainPendingAsDropped()
        {
            lock (_syncRoot)
            {
                while (_count > 0)
                {
                    Entry entry = RemoveAtOffsetNoLock(0);
                    _droppedNewestCount++;
                    if (entry.Message.Level >= _options.CriticalLevel)
                    {
                        _droppedCriticalCount++;
                    }

                    LogMessagePool.Return(entry.Message);
                }

                Monitor.PulseAll(_syncRoot);
            }
        }

        internal LogProcessingStatistics GetStatistics()
        {
            lock (_syncRoot)
            {
                long dropped = _droppedNewestCount + _droppedOldestCount + _rejectedAfterStopCount;
                return new LogProcessingStatistics(
                    _count,
                    _reservedCount,
                    _inFlightCount,
                    _peakQueuedCount,
                    _queuedCharacters,
                    _inFlightCharacters,
                    _peakQueuedCharacters,
                    _enqueuedCount,
                    dropped,
                    _droppedNewestCount,
                    _droppedOldestCount,
                    _droppedCriticalCount,
                    _rejectedAfterStopCount,
                    _processedCount,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0,
                    0);
            }
        }

        public void Dispose()
        {
            lock (_syncRoot)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _addingCompleted = true;
                while (_count > 0)
                {
                    Entry entry = RemoveAtOffsetNoLock(0);
                    _droppedNewestCount++;
                    if (entry.Message.Level >= _options.CriticalLevel)
                    {
                        _droppedCriticalCount++;
                    }

                    LogMessagePool.Return(entry.Message);
                }

                Monitor.PulseAll(_syncRoot);
            }
        }

        private bool TryDequeueNoLock(out LogMessage message, out int characters)
        {
            if (_count == 0)
            {
                message = null;
                characters = 0;
                return false;
            }

            Entry entry = _entries[_head];
            _entries[_head] = default;
            _head = (_head + 1) % _entries.Length;
            _count--;
            _queuedCharacters -= entry.Characters;
            _inFlightCount++;
            _inFlightCharacters += entry.Characters;
            message = entry.Message;
            characters = entry.Characters;
            Monitor.PulseAll(_syncRoot);
            return true;
        }

        private bool HasCapacityNoLock(LogLevel level, int characters)
        {
            bool critical = level >= _options.CriticalLevel;
            int messageLimit = critical
                ? _options.MaxQueuedMessages
                : _options.MaxQueuedMessages - _options.ReservedCriticalMessages;
            int characterLimit = critical
                ? _options.MaxQueuedCharacters
                : _options.MaxQueuedCharacters - _options.ReservedCriticalCharacters;

            return _count + _inFlightCount + _reservedCount < messageLimit
                && (long)_queuedCharacters + _inFlightCharacters + _reservedCharacters + characters <= characterLimit;
        }

        private bool TryEvictForIncomingNoLock(LogLevel incomingLevel)
        {
            bool incomingCritical = incomingLevel >= _options.CriticalLevel;
            int offset = FindOldestNonCriticalOffsetNoLock();
            if (offset < 0)
            {
                if (!incomingCritical || _options.OverflowPolicy != LogQueueOverflowPolicy.DropOldest || _count == 0)
                {
                    return false;
                }

                offset = 0;
            }
            else if (!incomingCritical && _options.OverflowPolicy != LogQueueOverflowPolicy.DropOldest)
            {
                return false;
            }

            Entry dropped = RemoveAtOffsetNoLock(offset);
            _droppedOldestCount++;
            if (dropped.Message.Level >= _options.CriticalLevel)
            {
                _droppedCriticalCount++;
            }

            LogMessagePool.Return(dropped.Message);
            Monitor.PulseAll(_syncRoot);
            return true;
        }

        private int FindOldestNonCriticalOffsetNoLock()
        {
            for (int offset = 0; offset < _count; offset++)
            {
                int index = (_head + offset) % _entries.Length;
                if (_entries[index].Message.Level < _options.CriticalLevel)
                {
                    return offset;
                }
            }

            return -1;
        }

        private Entry RemoveAtOffsetNoLock(int offset)
        {
            int index = (_head + offset) % _entries.Length;
            Entry removed = _entries[index];
            if (offset == 0)
            {
                _entries[_head] = default;
                _head = (_head + 1) % _entries.Length;
                _count--;
                _queuedCharacters -= removed.Characters;
                return removed;
            }

            for (int current = offset; current < _count - 1; current++)
            {
                int destination = (_head + current) % _entries.Length;
                int source = (_head + current + 1) % _entries.Length;
                _entries[destination] = _entries[source];
            }

            int tail = (_head + _count - 1) % _entries.Length;
            _entries[tail] = default;
            _count--;
            _queuedCharacters -= removed.Characters;
            return removed;
        }

        private void ReleaseReservationNoLock(int reservedCharacters)
        {
            if (_reservedCount <= 0)
            {
                return;
            }

            _reservedCount--;
            _reservedCharacters -= Math.Min(Math.Max(reservedCharacters, 0), _options.MaxQueuedCharacters);
            if (_reservedCharacters < 0)
            {
                _reservedCharacters = 0;
            }
        }

        private void RecordDropNewestNoLock(LogLevel level)
        {
            _droppedNewestCount++;
            if (level >= _options.CriticalLevel)
            {
                _droppedCriticalCount++;
            }
        }

        private bool IsIdleNoLock()
        {
            return _count == 0 && _reservedCount == 0 && _inFlightCount == 0;
        }

        private static int GetRemainingTimeout(int startTick, int timeoutMs)
        {
            if (timeoutMs < 0)
            {
                return Timeout.Infinite;
            }

            int elapsed = unchecked(Environment.TickCount - startTick);
            return timeoutMs - elapsed;
        }
    }
}
