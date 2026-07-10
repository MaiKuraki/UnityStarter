using System;
using System.Threading;

namespace CycloneGames.Logger
{
    internal sealed class ThreadedLogProcessor : ILogProcessor
    {
        private readonly CLogger _owner;
        private readonly BoundedLogQueue _queue;
        private readonly Thread _workerThread;
        private readonly int _maintenanceIntervalMs;
        private int _shutdownState;

        public bool IsStopped => Volatile.Read(ref _shutdownState) == 2 && _queue.IsStopped && !_workerThread.IsAlive;

        internal ThreadedLogProcessor(CLogger owner, LoggerProcessingOptions options = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            LoggerProcessingOptions validatedOptions = LoggerProcessingOptions.CreateValidated(options);
            _maintenanceIntervalMs = validatedOptions.MaintenanceIntervalMs;
            _queue = new BoundedLogQueue(validatedOptions);
            _workerThread = new Thread(ProcessLoop)
            {
                Name = "CLogger.Worker",
                IsBackground = true
            };
            _workerThread.Start();
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
        }

        public bool TryFlush(int timeoutMs)
        {
            return _queue.WaitUntilIdle(timeoutMs);
        }

        public LoggerShutdownResult Shutdown(int timeoutMs)
        {
            int previous = Interlocked.CompareExchange(ref _shutdownState, 1, 0);
            if (previous == 2 && !_workerThread.IsAlive)
            {
                return new LoggerShutdownResult(LoggerShutdownStatus.AlreadyStopped, GetStatistics().DroppedMessageCount, true);
            }

            _queue.CompleteAdding();
            bool stopped = _workerThread.Join(timeoutMs);
            if (!stopped)
            {
                LogProcessingStatistics timedOutStatistics = GetStatistics();
                return new LoggerShutdownResult(LoggerShutdownStatus.TimedOut, timedOutStatistics.DroppedMessageCount, false);
            }

            Volatile.Write(ref _shutdownState, 2);
            LogProcessingStatistics statistics = GetStatistics();
            LoggerShutdownStatus status = statistics.DroppedMessageCount == 0
                ? LoggerShutdownStatus.Completed
                : LoggerShutdownStatus.CompletedWithDrops;
            return new LoggerShutdownResult(status, statistics.DroppedMessageCount, false);
        }

        public LogProcessingStatistics GetStatistics()
        {
            return _queue.GetStatistics();
        }

        public void Dispose()
        {
            LoggerShutdownResult result = Shutdown(LoggerProcessingOptions.DefaultShutdownDrainTimeoutMs);
            if (result.IsComplete)
            {
                _queue.Dispose();
            }
        }

        private void ProcessLoop()
        {
            try
            {
                while (true)
                {
                    if (_queue.WaitDequeue(_maintenanceIntervalMs, out LogMessage message, out int characters, out bool addingCompleted))
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

                        continue;
                    }

                    _owner.PerformSinkMaintenance();
                    if (addingCompleted && _queue.WaitUntilIdle(_maintenanceIntervalMs))
                    {
                        break;
                    }
                }
            }
            catch (Exception exception)
            {
                EmergencyLogger.TryWrite("ThreadedLogProcessor stopped after an unexpected failure.", exception);
                _queue.CompleteAdding();
                _queue.DrainPendingAsDropped();
            }
            finally
            {
                Volatile.Write(ref _shutdownState, 2);
            }
        }
    }
}
