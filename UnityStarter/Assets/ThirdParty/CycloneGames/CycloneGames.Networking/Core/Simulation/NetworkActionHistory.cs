using System;

namespace CycloneGames.Networking.Simulation
{
    public readonly struct NetworkActionHistoryEntry<TSnapshot>
        where TSnapshot : struct
    {
        public readonly ulong EntityId;
        public readonly NetworkTickId Tick;
        public readonly ushort Sequence;
        public readonly TSnapshot Snapshot;
        public readonly bool IsValid;

        public NetworkActionHistoryEntry(
            ulong entityId,
            NetworkTickId tick,
            ushort sequence,
            in TSnapshot snapshot)
        {
            EntityId = entityId;
            Tick = tick;
            Sequence = sequence;
            Snapshot = snapshot;
            IsValid = true;
        }
    }

    public sealed class NetworkActionHistory<TSnapshot>
        where TSnapshot : struct
    {
        private readonly NetworkActionHistoryEntry<TSnapshot>[] _entries;
        private int _nextIndex;
        private int _count;

        public NetworkActionHistory(int capacity)
        {
            if (capacity <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(capacity));
            }

            _entries = new NetworkActionHistoryEntry<TSnapshot>[capacity];
            _nextIndex = 0;
            _count = 0;
        }

        public int Capacity
        {
            get
            {
                return _entries.Length;
            }
        }

        public int Count
        {
            get
            {
                return _count;
            }
        }

        public void Record(ulong entityId, NetworkTickId tick, ushort sequence, in TSnapshot snapshot)
        {
            ValidateEntityAndTick(entityId, tick);

            if (!_entries[_nextIndex].IsValid)
            {
                _count++;
            }

            _entries[_nextIndex] = new NetworkActionHistoryEntry<TSnapshot>(
                entityId,
                tick,
                sequence,
                snapshot);

            _nextIndex++;
            if (_nextIndex == _entries.Length)
            {
                _nextIndex = 0;
            }
        }

        public bool TryGet(ulong entityId, NetworkTickId tick, out TSnapshot snapshot)
        {
            ValidateEntityAndTick(entityId, tick);

            for (int i = 0; i < _entries.Length; i++)
            {
                NetworkActionHistoryEntry<TSnapshot> entry = _entries[i];
                if (entry.IsValid && entry.EntityId == entityId && entry.Tick == tick)
                {
                    snapshot = entry.Snapshot;
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        public bool TryGet(ulong entityId, NetworkTickId tick, ushort sequence, out TSnapshot snapshot)
        {
            ValidateEntityAndTick(entityId, tick);

            for (int i = 0; i < _entries.Length; i++)
            {
                NetworkActionHistoryEntry<TSnapshot> entry = _entries[i];
                if (entry.IsValid
                    && entry.EntityId == entityId
                    && entry.Tick == tick
                    && entry.Sequence == sequence)
                {
                    snapshot = entry.Snapshot;
                    return true;
                }
            }

            snapshot = default;
            return false;
        }

        public bool TryGetLatest(ulong entityId, out NetworkActionHistoryEntry<TSnapshot> latest)
        {
            if (entityId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            latest = default;
            bool found = false;

            for (int i = 0; i < _entries.Length; i++)
            {
                NetworkActionHistoryEntry<TSnapshot> entry = _entries[i];
                if (!entry.IsValid || entry.EntityId != entityId)
                {
                    continue;
                }

                if (!found
                    || entry.Tick > latest.Tick
                    || (entry.Tick == latest.Tick && IsSequenceNewer(entry.Sequence, latest.Sequence)))
                {
                    latest = entry;
                    found = true;
                }
            }

            return found;
        }

        public int CopyEntityHistory(ulong entityId, Span<NetworkActionHistoryEntry<TSnapshot>> destination)
        {
            if (entityId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            int written = 0;
            int oldest = _nextIndex;

            for (int i = 0; i < _entries.Length && written < destination.Length; i++)
            {
                int index = oldest + i;
                if (index >= _entries.Length)
                {
                    index -= _entries.Length;
                }

                NetworkActionHistoryEntry<TSnapshot> entry = _entries[index];
                if (entry.IsValid && entry.EntityId == entityId)
                {
                    destination[written] = entry;
                    written++;
                }
            }

            return written;
        }

        public int RemoveEntity(ulong entityId)
        {
            if (entityId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            int removed = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].IsValid && _entries[i].EntityId == entityId)
                {
                    _entries[i] = default;
                    removed++;
                }
            }

            _count -= removed;
            return removed;
        }

        public void Clear()
        {
            Array.Clear(_entries, 0, _entries.Length);
            _nextIndex = 0;
            _count = 0;
        }

        private static bool IsSequenceNewer(ushort candidate, ushort current)
        {
            ushort delta = unchecked((ushort)(candidate - current));
            return delta != 0 && delta < 32768;
        }

        private static void ValidateEntityAndTick(ulong entityId, NetworkTickId tick)
        {
            if (entityId == 0UL)
            {
                throw new ArgumentOutOfRangeException(nameof(entityId));
            }

            if (!tick.IsValid)
            {
                throw new ArgumentOutOfRangeException(nameof(tick));
            }
        }
    }
}
