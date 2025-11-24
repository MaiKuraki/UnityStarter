using System;
using System.Collections.Concurrent;
using System.Threading;
using System.Threading.Tasks;

namespace CycloneGames.Logger
{
    /// <summary>
    /// Background-thread processing strategy using BlockingCollection.
    /// Guarantees FIFO semantics per-queue and amortizes contention under load.
    /// </summary>
    internal sealed class ThreadedLogProcessor : ILogProcessor
    {
        private readonly CLogger _owner;
        private readonly BlockingCollection<LogMessage> _queue = new(new ConcurrentQueue<LogMessage>());
        private readonly CancellationTokenSource _cts = new();
        private readonly Task _worker;

        public ThreadedLogProcessor(CLogger owner)
        {
            _owner = owner ?? throw new ArgumentNullException(nameof(owner));
            _worker = Task.Factory.StartNew(ProcessLoop, _cts.Token, TaskCreationOptions.LongRunning, TaskScheduler.Default);
        }

        public void Enqueue(LogMessage message)
        {
            if (!_queue.IsAddingCompleted)
            {
                try { _queue.Add(message); } catch (InvalidOperationException) { /* shutting down */ }
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
            try { _worker.Wait(TimeSpan.FromSeconds(2)); } catch { }
            _cts.Dispose();
            _queue.Dispose();
        }
    }
}