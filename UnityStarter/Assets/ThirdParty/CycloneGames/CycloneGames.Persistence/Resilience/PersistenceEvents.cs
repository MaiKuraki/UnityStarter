using System;
using System.Threading;
using CycloneGames.Persistence;

namespace CycloneGames.Persistence.Resilience
{
    public enum PersistenceEventKind
    {
        SaveStarted = 0,
        SaveSucceeded = 1,
        SaveFailed = 2,
        WriteRejected = 3,
        LeaseConflict = 4,
        RecoveryCommitted = 5,
        RecoveryRestored = 6,
        BackupPruned = 7,
        WalCorrupt = 8
    }

    public readonly struct PersistenceEvent
    {
        public PersistenceEvent(
            PersistenceEventKind kind,
            long timestampTicks,
            PersistenceErrorCode errorCode,
            int recordBytes,
            long elapsedTicks)
        {
            Kind = kind;
            TimestampTicks = timestampTicks;
            ErrorCode = errorCode;
            RecordBytes = recordBytes;
            ElapsedTicks = elapsedTicks;
        }

        public PersistenceEventKind Kind { get; }

        public long TimestampTicks { get; }

        public PersistenceErrorCode ErrorCode { get; }

        public int RecordBytes { get; }

        public long ElapsedTicks { get; }
    }

    public interface IPersistenceEventSink
    {
        void Record(in PersistenceEvent value);
    }

    /// <summary>
    /// Fixed-capacity ring buffer for diagnostics. Storage is allocated once, so a long-running
    /// process does not accumulate event objects.
    /// </summary>
    public sealed class InMemoryEventSink : IPersistenceEventSink
    {
        private readonly PersistenceEvent[] _buffer;
        private readonly object _sync = new object();
        private long _written;

        public InMemoryEventSink(int capacity)
        {
            if (capacity < 1)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _buffer = new PersistenceEvent[capacity];
        }

        public int Capacity => _buffer.Length;

        public long WrittenCount
        {
            get
            {
                lock (_sync)
                {
                    return _written;
                }
            }
        }

        public void Record(in PersistenceEvent value)
        {
            lock (_sync)
            {
                _buffer[(int)(_written % _buffer.Length)] = value;
                _written++;
            }
        }

        public bool TryGet(int index, out PersistenceEvent value)
        {
            lock (_sync)
            {
                if (index < 0 || index >= _buffer.Length || index >= _written)
                {
                    value = default;
                    return false;
                }

                value = _buffer[index];
                return true;
            }
        }

        public int CountByKind(PersistenceEventKind kind)
        {
            int count = 0;
            lock (_sync)
            {
                int visible = (int)Math.Min(_written, _buffer.Length);
                for (int index = 0; index < visible; index++)
                {
                    if (_buffer[index].Kind == kind)
                    {
                        count++;
                    }
                }
            }

            return count;
        }
    }
}
