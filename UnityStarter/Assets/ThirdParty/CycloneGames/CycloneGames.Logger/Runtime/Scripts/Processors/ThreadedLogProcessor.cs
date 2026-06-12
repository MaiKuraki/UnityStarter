using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Background-thread processing strategy using BlockingCollection.
    /// Uses dedicated Thread (instead of Task) for better IL2CPP compatibility.
    /// </summary>
    internal sealed class ThreadedLogProcessor : ILogProcessor, ILogProcessorDiagnostics
    {
        private readonly CLogger _owner;
        private readonly LoggerProcessingOptions _options;
        private readonly BlockingCollection<LogMessage> _queue;
        private readonly Thread _workerThread;
        private volatile bool _isDisposed;
        private volatile bool _isStopped;
        private long _droppedMessageCount;
        private long _processedMessageCount;

        public bool IsStopped => _isStopped;

        public ThreadedLogProcessor(CLogger owner, LoggerProcessingOptions options = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _options = LoggerProcessingOptions.CreateValidated(options);
            _queue = new BlockingCollection<LogMessage>(new ConcurrentQueue<LogMessage>(), _options.MaxQueuedMessages);
            _workerThread = new Thread(ProcessLoop)
            {
                Name = "CLogger.Worker",
                IsBackground = true,
                Priority = ThreadPriority.BelowNormal
            };
            _workerThread.Start();
        }

        public void Enqueue(LogMessage message)
        {
            if (message == null) return;

            if (_isDisposed || _queue.IsAddingCompleted)
            {
                LogMessagePool.Return(message);
                return;
            }

            try
            {
                if (TryEnqueue(message)) return;

                DropMessage(message);
            }
            catch (InvalidOperationException)
            {
                LogMessagePool.Return(message);
            }
        }

        public void Pump(int maxItems) { /* background thread handles it */ }

        public LogProcessingStatistics GetStatistics()
        {
            return new LogProcessingStatistics(_queue.Count, Interlocked.Read(ref _droppedMessageCount), Interlocked.Read(ref _processedMessageCount));
        }

        private bool TryEnqueue(LogMessage message)
        {
            if (_queue.TryAdd(message)) return true;

            if (message.Level >= _options.GuaranteedLevel && TryDropOldest())
            {
                return _queue.TryAdd(message);
            }

            switch (_options.OverflowPolicy)
            {
                case LogQueueOverflowPolicy.Block:
                    if (_queue.TryAdd(message, _options.EnqueueBlockTimeoutMs)) return true;
                    if (message.Level >= _options.GuaranteedLevel && TryDropOldest())
                    {
                        return _queue.TryAdd(message);
                    }
                    return false;

                case LogQueueOverflowPolicy.DropOldest:
                    if (!TryDropOldest()) return false;
                    return _queue.TryAdd(message);

                default:
                    return false;
            }
        }

        private bool TryDropOldest()
        {
            try
            {
                if (!_queue.TryTake(out var dropped)) return false;

                LogMessagePool.Return(dropped);
                Interlocked.Increment(ref _droppedMessageCount);
                return true;
            }
            catch (InvalidOperationException)
            {
                return false;
            }
        }

        private void DropMessage(LogMessage message)
        {
            LogMessagePool.Return(message);
            Interlocked.Increment(ref _droppedMessageCount);
        }

        private void ProcessLoop()
        {
            try
            {
                // GetConsumingEnumerable blocks when empty, completes when CompleteAdding() is called and queue is drained.
                foreach (var msg in _queue.GetConsumingEnumerable())
                {
                    try
                    {
                        _owner.DispatchToLoggers(msg);
                        Interlocked.Increment(ref _processedMessageCount);
                    }
                    finally
                    {
                        LogMessagePool.Return(msg);
                    }
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRITICAL] ThreadedLogProcessor: {ex}");
            }
            finally
            {
                _isStopped = true;
            }
        }

        public void Dispose()
        {
            if (_isDisposed) return;
            _isDisposed = true;

            // Signal no more items; worker will drain remaining messages then exit naturally.
            _queue.CompleteAdding();

            if (!_workerThread.Join(TimeSpan.FromMilliseconds(_options.ShutdownDrainTimeoutMs)))
            {
                Console.Error.WriteLine("[WARNING] ThreadedLogProcessor: Worker thread did not exit gracefully.");
                DrainPendingMessagesAsDropped();
                return;
            }

            _queue.Dispose();
        }

        private void DrainPendingMessagesAsDropped()
        {
            try
            {
                while (_queue.TryTake(out var message))
                {
                    DropMessage(message);
                }
            }
            catch (InvalidOperationException)
            {
            }
        }
    }
}
