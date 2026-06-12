using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Single-thread (manual pump) processing strategy. Suitable for platforms without threads.
    /// Use <see cref="CLogger.Pump"/> to bound per-frame processing.
    /// </summary>
    internal sealed class SingleThreadLogProcessor : ILogProcessor, ILogProcessorDiagnostics
    {
        private readonly CLogger _owner;
        private readonly LoggerProcessingOptions _options;
        private readonly ConcurrentQueue<LogMessage> _queue = new();
        private volatile bool _isDisposing;
        private volatile bool _isStopped;
        private int _queueCount;
        private long _droppedMessageCount;
        private long _processedMessageCount;

        public bool IsStopped => _isStopped;

        public SingleThreadLogProcessor(CLogger owner, LoggerProcessingOptions options = null)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _options = LoggerProcessingOptions.CreateValidated(options);
        }

        public void Enqueue(LogMessage message)
        {
            if (message == null) return;
            if (_isDisposing)
            {
                LogMessagePool.Return(message);
                return;
            }

            if (!TryEnqueue(message))
            {
                DropMessage(message);
                return;
            }

            _queue.Enqueue(message);
        }

        public void Pump(int maxItems)
        {
            if (maxItems <= 0) return;

            int processed = 0;
            while (processed < maxItems && _queue.TryDequeue(out var msg))
            {
                Interlocked.Decrement(ref _queueCount);
                try
                {
                    _owner.DispatchToLoggers(msg);
                    Interlocked.Increment(ref _processedMessageCount);
                }
                finally
                {
                    LogMessagePool.Return(msg);
                }
                processed++;
            }
        }

        public LogProcessingStatistics GetStatistics()
        {
            return new LogProcessingStatistics(Volatile.Read(ref _queueCount), Interlocked.Read(ref _droppedMessageCount), Interlocked.Read(ref _processedMessageCount));
        }

        public void Dispose()
        {
            if (_isDisposing) return;
            _isDisposing = true;
            Pump(int.MaxValue);
            DrainPendingMessagesAsDropped();
            _isStopped = true;
        }

        private bool TryEnqueue(LogMessage message)
        {
            if (TryReserveSlot()) return true;

            if (message.Level >= _options.GuaranteedLevel && TryDropOldest())
            {
                return TryReserveSlot();
            }

            switch (_options.OverflowPolicy)
            {
                case LogQueueOverflowPolicy.Block:
                    if (_options.EnqueueBlockTimeoutMs <= 0) return false;
                    return SpinWait.SpinUntil(TryReserveSlot, _options.EnqueueBlockTimeoutMs);

                case LogQueueOverflowPolicy.DropOldest:
                    return TryDropOldest() && TryReserveSlot();

                default:
                    return false;
            }
        }

        private bool TryReserveSlot()
        {
            while (true)
            {
                int current = Volatile.Read(ref _queueCount);
                if (current >= _options.MaxQueuedMessages) return false;
                if (Interlocked.CompareExchange(ref _queueCount, current + 1, current) == current) return true;
            }
        }

        private bool TryDropOldest()
        {
            if (!_queue.TryDequeue(out var dropped)) return false;

            Interlocked.Decrement(ref _queueCount);
            DropMessage(dropped);
            return true;
        }

        private void DropMessage(LogMessage message)
        {
            LogMessagePool.Return(message);
            Interlocked.Increment(ref _droppedMessageCount);
        }

        private void DrainPendingMessagesAsDropped()
        {
            while (_queue.TryDequeue(out var message))
            {
                Interlocked.Decrement(ref _queueCount);
                DropMessage(message);
            }
        }
    }
}
