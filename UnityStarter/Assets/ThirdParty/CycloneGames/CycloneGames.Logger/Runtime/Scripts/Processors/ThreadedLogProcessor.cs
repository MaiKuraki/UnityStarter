using System;
using System.Collections.Concurrent;
using System.Threading;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Background-thread processing strategy using BlockingCollection.
    /// Uses dedicated Thread (instead of Task) for better IL2CPP compatibility.
    /// </summary>
    internal sealed class ThreadedLogProcessor : ILogProcessor
    {
        private readonly CLogger _owner;
        private readonly BlockingCollection<LogMessage> _queue = new(new ConcurrentQueue<LogMessage>());
        private readonly CancellationTokenSource _cts = new();
        private readonly Thread _workerThread;

        public ThreadedLogProcessor(CLogger owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
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
            if (!_queue.IsAddingCompleted)
            {
                try { _queue.Add(message); } 
                catch (InvalidOperationException) { /* shutting down */ }
            }
        }

        public void Pump(int maxItems) { /* background thread handles it */ }

        private void ProcessLoop()
        {
            try
            {
                foreach (var msg in _queue.GetConsumingEnumerable(_cts.Token))
                {
                    _owner.DispatchToLoggers(msg);
                    LogMessagePool.Return(msg);
                }
            }
            catch (OperationCanceledException) { }
            catch (Exception ex)
            {
                Console.Error.WriteLine($"[CRITICAL] ThreadedLogProcessor: {ex}");
            }
        }

        public void Dispose()
        {
            _queue.CompleteAdding();
            _cts.Cancel();
            
            if (!_workerThread.Join(TimeSpan.FromSeconds(2)))
            {
                Console.Error.WriteLine("[WARNING] ThreadedLogProcessor: Worker thread did not exit gracefully.");
            }
            
            _cts.Dispose();
            _queue.Dispose();
        }
    }
}