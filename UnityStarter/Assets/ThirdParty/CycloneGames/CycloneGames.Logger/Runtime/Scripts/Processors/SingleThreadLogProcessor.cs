using System;
using System.Diagnostics;
using System.Threading;

namespace CycloneGames.Logger
{
    internal sealed class SingleThreadLogProcessor : ILogProcessor
    {
        private readonly CLogger _owner;
        private readonly BoundedLogQueue _queue;
        private int _shutdownState;
        private int _pumpGate;

        public bool IsStopped => Volatile.Read(ref _shutdownState) == 2 && _queue.IsStopped;

        internal SingleThreadLogProcessor(CLogger owner, LoggerProcessingOptions options = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _queue = new BoundedLogQueue(options);
        }

        public bool TryReserve(LogLevel level, int estimatedCharacters, bool allowEviction, out int reservedCharacters)
        {
            return _queue.TryReserve(level, estimatedCharacters, allowEviction, out reservedCharacters);
        }

        public bool TryCommit(LogMessage message, int reservedCharacters, int actualCharacters)
        {
            return _queue.TryCommit(message, reservedCharacters, actualCharacters);
        }

        public void CancelReservation(int reservedCharacters)
        {
            _queue.CancelReservation(reservedCharacters);
        }

        public void Pump(int maxItems, int budgetMilliseconds)
        {
            if (maxItems <= 0)
            {
                return;
            }

            if (Interlocked.CompareExchange(ref _pumpGate, 1, 0) != 0)
            {
                return;
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            long budgetTicks = budgetMilliseconds < 0
                ? long.MaxValue
                : Math.Max(1L, Stopwatch.Frequency * budgetMilliseconds / 1000L);
            try
            {
                int processed = 0;
                while (processed < maxItems && _queue.TryDequeue(out LogMessage message, out int characters))
                {
                    try
                    {
                        _owner.DispatchToLoggers(message);
                    }
                    finally
                    {
                        LogMessagePool.Return(message);
                        _queue.CompleteProcessing(characters);
                    }

                    processed++;
                    if (budgetMilliseconds >= 0
                        && Stopwatch.GetTimestamp() - startTimestamp >= budgetTicks)
                    {
                        break;
                    }
                }

                _owner.PerformSinkMaintenance();
            }
            finally
            {
                Volatile.Write(ref _pumpGate, 0);
            }
        }

        public bool TryFlush(int timeoutMs)
        {
            if (timeoutMs == 0)
            {
                return _queue.WaitUntilIdle(0);
            }

            long startTimestamp = Stopwatch.GetTimestamp();
            while (!_queue.IsStopped && !_queue.WaitUntilIdle(0))
            {
                int remaining = timeoutMs < 0
                    ? -1
                    : Math.Max(0, timeoutMs - GetElapsedMilliseconds(startTimestamp));
                Pump(256, remaining);
                if (_queue.WaitUntilIdle(0))
                {
                    return true;
                }

                if (timeoutMs >= 0 && GetElapsedMilliseconds(startTimestamp) >= timeoutMs)
                {
                    return false;
                }

#if !UNITY_WEBGL || UNITY_EDITOR
                Thread.Yield();
#endif
            }

            return _queue.WaitUntilIdle(0);
        }

        private static int GetElapsedMilliseconds(long startTimestamp)
        {
            long elapsedTicks = Stopwatch.GetTimestamp() - startTimestamp;
            long elapsedSeconds = elapsedTicks / Stopwatch.Frequency;
            if (elapsedSeconds >= int.MaxValue / 1000L)
            {
                return int.MaxValue;
            }

            long remainder = elapsedTicks % Stopwatch.Frequency;
            long elapsedMilliseconds = elapsedSeconds * 1000L
                + remainder * 1000L / Stopwatch.Frequency;
            return elapsedMilliseconds >= int.MaxValue ? int.MaxValue : (int)elapsedMilliseconds;
        }

        public LoggerShutdownResult Shutdown(int timeoutMs)
        {
            int previous = Interlocked.CompareExchange(ref _shutdownState, 1, 0);
            if (previous == 2 && _queue.IsStopped)
            {
                return new LoggerShutdownResult(LoggerShutdownStatus.AlreadyStopped, GetStatistics().DroppedMessageCount, true);
            }

            _queue.CompleteAdding();
            bool drained = TryFlush(timeoutMs);
            if (!drained)
            {
                _queue.DrainPendingAsDropped();
            }

            Volatile.Write(ref _shutdownState, 2);
            LogProcessingStatistics statistics = GetStatistics();
            LoggerShutdownStatus status = drained
                ? statistics.DroppedMessageCount == 0
                    ? LoggerShutdownStatus.Completed
                    : LoggerShutdownStatus.CompletedWithDrops
                : LoggerShutdownStatus.TimedOut;
            return new LoggerShutdownResult(status, statistics.DroppedMessageCount, false);
        }

        public LogProcessingStatistics GetStatistics()
        {
            return _queue.GetStatistics();
        }

        public void Dispose()
        {
            Shutdown(LoggerProcessingOptions.DefaultShutdownDrainTimeoutMs);
            _queue.Dispose();
        }
    }
}
