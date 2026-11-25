using System;
using System.Threading;
using System.Threading.Tasks;
using CycloneGames.Persistence;

namespace CycloneGames.Persistence.Resilience
{
    /// <summary>
    /// Bounded admission gate. Wait time is explicit and a timed-out caller is rejected rather
    /// than queued without limit.
    /// </summary>
    public sealed class BoundedWriteGate : IDisposable
    {
        private readonly SemaphoreSlim _semaphore;
        private readonly int _capacity;
        private readonly TimeSpan _timeout;
        private readonly IPersistenceEventSink _sink;
        private long _rejected;
        private long _acquired;

        public BoundedWriteGate(
            int capacity,
            TimeSpan timeout,
            IPersistenceEventSink eventSink)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            if (timeout <= TimeSpan.Zero)
            {
                throw new ArgumentOutOfRangeException(nameof(timeout));
            }

            _capacity = capacity;
            _timeout = timeout;
            _sink = eventSink;
            _semaphore = new SemaphoreSlim(capacity, capacity);
        }

        public int Capacity => _capacity;

        public int WaitingDepth => _capacity - _semaphore.CurrentCount;

        public long RejectedCount => Interlocked.Read(ref _rejected);

        public long AcquiredCount => Interlocked.Read(ref _acquired);

        public async Task<IDisposable> AcquireAsync(
            CancellationToken cancellationToken = default)
        {
            bool acquired = await _semaphore.WaitAsync(_timeout, cancellationToken)
                .ConfigureAwait(false);
            if (!acquired)
            {
                Interlocked.Increment(ref _rejected);
                Emit(PersistenceEventKind.WriteRejected, PersistenceErrorCode.WriteFailed, 0, 0);
                throw new TimeoutException(
                    "The write gate rejected the operation after its configured wait.");
            }

            Interlocked.Increment(ref _acquired);
            Emit(PersistenceEventKind.SaveStarted, PersistenceErrorCode.None, 0, 0);
            return new Lease(this);
        }

        public void Dispose()
        {
            _semaphore.Dispose();
        }

        private void Emit(
            PersistenceEventKind kind,
            PersistenceErrorCode errorCode,
            int recordBytes,
            long elapsedTicks)
        {
            if (_sink == null)
            {
                return;
            }

            var value = new PersistenceEvent(
                kind,
                DateTime.UtcNow.Ticks,
                errorCode,
                recordBytes,
                elapsedTicks);
            _sink.Record(in value);
        }

        private void Release()
        {
            _semaphore.Release();
        }

        private sealed class Lease : IDisposable
        {
            private BoundedWriteGate _owner;

            internal Lease(BoundedWriteGate owner)
            {
                _owner = owner;
            }

            public void Dispose()
            {
                BoundedWriteGate owner = Interlocked.Exchange(ref _owner, null);
                if (owner != null)
                {
                    owner.Release();
                }
            }
        }
    }
}
