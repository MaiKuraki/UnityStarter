using System;
using System.Diagnostics;
using System.Threading;

namespace CycloneGames.Logging
{
    internal sealed class ThreadedLogProcessor : ILogProcessor
    {
        private readonly LogPipeline _owner;
        private readonly BoundedLogQueue _queue;
        private readonly Thread _workerThread;
        private readonly int _maintenanceIntervalMs;
        private int _shutdownState;

        public bool IsStopped => Volatile.Read(ref _shutdownState) == 2 && _queue.IsStopped && !_workerThread.IsAlive;

        internal ThreadedLogProcessor(LogPipeline owner, LogPipelineOptions options = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            LogPipelineOptions validatedOptions = LogPipelineOptions.CreateValidated(options);
            _maintenanceIntervalMs = validatedOptions.MaintenanceIntervalMs;
            _queue = new BoundedLogQueue(validatedOptions);
            _workerThread = new Thread(ProcessLoop)
            {
                Name = "LogPipeline.Worker",
                IsBackground = true
            };
            _workerThread.Start();
        }

        public bool TryReserve(LogSeverity level, int estimatedCharacters, bool allowEviction, out int reservedCharacters)
        {
            return _queue.TryReserve(level, estimatedCharacters, allowEviction, out reservedCharacters);
        }

        public bool TryCommit(LogEvent message, int reservedCharacters, int actualCharacters)
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

        public LogPipelineShutdownResult Shutdown(int timeoutMs)
        {
            int previous = Interlocked.CompareExchange(ref _shutdownState, 1, 0);
            if (previous == 2 && !_workerThread.IsAlive)
            {
                return new LogPipelineShutdownResult(LogPipelineShutdownStatus.AlreadyStopped, GetStatistics().DroppedMessageCount, true);
            }

            _queue.CompleteAdding();
            bool stopped = _workerThread.Join(timeoutMs);
            if (!stopped)
            {
                LogPipelineStatistics timedOutStatistics = GetStatistics();
                return new LogPipelineShutdownResult(LogPipelineShutdownStatus.TimedOut, timedOutStatistics.DroppedMessageCount, false);
            }

            Volatile.Write(ref _shutdownState, 2);
            LogPipelineStatistics statistics = GetStatistics();
            LogPipelineShutdownStatus status = statistics.DroppedMessageCount == 0
                ? LogPipelineShutdownStatus.Completed
                : LogPipelineShutdownStatus.CompletedWithDrops;
            return new LogPipelineShutdownResult(status, statistics.DroppedMessageCount, false);
        }

        public LogPipelineStatistics GetStatistics()
        {
            return _queue.GetStatistics();
        }

        public void Dispose()
        {
            LogPipelineShutdownResult result = Shutdown(LogPipelineOptions.DefaultShutdownDrainTimeoutMs);
            if (result.IsComplete)
            {
                _queue.Dispose();
            }
        }

        private void ProcessLoop()
        {
            long maintenanceIntervalTicks = Math.Max(
                1L,
                (long)(Stopwatch.Frequency * (_maintenanceIntervalMs / 1000.0)));
            long nextMaintenanceTimestamp = Stopwatch.GetTimestamp() + maintenanceIntervalTicks;
            try
            {
                while (true)
                {
                    if (_queue.WaitDequeue(_maintenanceIntervalMs, out LogEvent message, out int characters, out bool addingCompleted))
                    {
                        try
                        {
                            _owner.DispatchToSinks(message);
                        }
                        finally
                        {
                            LogEventPool.Return(message);
                            _queue.CompleteProcessing(characters);
                        }

                        PerformMaintenanceIfDue(ref nextMaintenanceTimestamp, maintenanceIntervalTicks);
                        continue;
                    }

                    _owner.PerformSinkMaintenance();
                    nextMaintenanceTimestamp = Stopwatch.GetTimestamp() + maintenanceIntervalTicks;
                    if (addingCompleted && _queue.WaitUntilIdle(_maintenanceIntervalMs))
                    {
                        break;
                    }
                }
            }
            catch (OutOfMemoryException exception)
            {
                _owner.RecordFatalFailure(exception);
                _queue.CompleteAdding();
                _queue.DrainPendingAsDropped();
                Volatile.Write(ref _shutdownState, 2);
            }
            catch (Exception exception)
            {
                _owner.RecordFatalFailure(exception);
                EmergencyLogWriter.TryWrite("ThreadedLogProcessor stopped after an unexpected failure.", exception);
                _queue.CompleteAdding();
                _queue.DrainPendingAsDropped();
                Volatile.Write(ref _shutdownState, 2);
            }
            finally
            {
                Volatile.Write(ref _shutdownState, 2);
            }
        }

        private void PerformMaintenanceIfDue(ref long nextTimestamp, long intervalTicks)
        {
            long now = Stopwatch.GetTimestamp();
            if (unchecked(now - nextTimestamp) < 0)
            {
                return;
            }

            _owner.PerformSinkMaintenance();
            nextTimestamp = now + intervalTicks;
        }
    }
}
