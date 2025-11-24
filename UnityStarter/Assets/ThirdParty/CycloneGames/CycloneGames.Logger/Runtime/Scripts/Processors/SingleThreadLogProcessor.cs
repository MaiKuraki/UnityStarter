using System;
using System.Collections.Concurrent;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Single-thread (manual pump) processing strategy. Suitable for platforms without threads.
    /// Use <see cref="CLogger.Pump"/> to bound per-frame processing.
    /// </summary>
    internal sealed class SingleThreadLogProcessor : ILogProcessor
    {
        private readonly CLogger _owner;
        private readonly ConcurrentQueue<LogMessage> _queue = new();
        private volatile bool _isDisposing;

        public SingleThreadLogProcessor(CLogger owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
        }

        public void Enqueue(LogMessage message)
        {
            if (_isDisposing) return;
            _queue.Enqueue(message);
        }

        public void Pump(int maxItems)
        {
            int processed = 0;
            while (processed < maxItems && _queue.TryDequeue(out var msg))
            {
                _owner.DispatchToLoggers(msg);
                LogMessagePool.Return(msg);
                processed++;
            }
        }

        public void Dispose()
        {
            _isDisposing = true;
            Pump(int.MaxValue);
        }
    }
}