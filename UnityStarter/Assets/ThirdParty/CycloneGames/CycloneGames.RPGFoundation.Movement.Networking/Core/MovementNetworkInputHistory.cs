using System;

namespace CycloneGames.RPGFoundation.Movement.Networking
{
    public readonly struct MovementHistoryDiagnostics
    {
        public readonly int TrackedEntities;
        public readonly int EntityCapacity;
        public readonly int PerEntityCapacity;
        public readonly int StoredEntries;
        public readonly long OverwriteCount;
        public readonly long EntityEvictionCount;

        public MovementHistoryDiagnostics(
            int trackedEntities, int entityCapacity, int perEntityCapacity,
            int storedEntries, long overwriteCount, long entityEvictionCount)
        {
            TrackedEntities = trackedEntities;
            EntityCapacity = entityCapacity;
            PerEntityCapacity = perEntityCapacity;
            StoredEntries = storedEntries;
            OverwriteCount = overwriteCount;
            EntityEvictionCount = entityEvictionCount;
        }
    }

    public sealed class MovementEntityHistory<TSnapshot> where TSnapshot : struct
    {
        private const int PROBE_LIMIT = 16;
        private const byte INDEX_EMPTY = 0;
        private const byte INDEX_OCCUPIED = 1;
        private const byte INDEX_TOMBSTONE = 2;

        private struct Slot
        {
            public ulong EntityId;
            public long Tick;
            public ushort Sequence;
            public uint Generation;
            public TSnapshot Snapshot;
            public bool IsValid;
        }

        private struct EntityMeta
        {
            public ulong EntityId;
            public int Base;
            public int Head;
            public int Count;
            public long LastSeenTick;
            public bool InUse;
        }

        private struct EntityIndexEntry
        {
            public ulong EntityId;
            public int MetaIndex;
            public byte State;
        }

        private struct SlotIndexEntry
        {
            public ulong Hash;
            public ulong EntityId;
            public long Tick;
            public ushort Sequence;
            public int SlotIndex;
            public uint Generation;
        }

        private readonly Slot[] _slots;
        private readonly EntityMeta[] _metas;
        private readonly EntityIndexEntry[] _entityIndex;
        private readonly SlotIndexEntry[] _slotIndex;
        private readonly int[] _freeMetas;
        private readonly int[] _lruPrev;
        private readonly int[] _lruNext;
        private readonly int _entityMask;
        private readonly int _slotMask;
        private readonly int _perEntity;
        private int _freeCount;
        private int _lruHead;
        private int _lruTail;
        private int _trackedEntities;
        private int _storedEntries;
        private long _overwriteCount;
        private long _entityEvictionCount;

        public MovementEntityHistory(MovementHistoryCapacity capacity)
        {
            int entityCount = capacity.EntityCount;
            _perEntity = capacity.PerEntityCount;
            _slots = new Slot[entityCount * _perEntity];
            _metas = new EntityMeta[entityCount];
            _entityIndex = new EntityIndexEntry[NextPowerOfTwo(entityCount * 2)];
            _slotIndex = new SlotIndexEntry[NextPowerOfTwo(_slots.Length * 2)];
            _freeMetas = new int[entityCount];
            _lruPrev = new int[entityCount];
            _lruNext = new int[entityCount];
            _entityMask = _entityIndex.Length - 1;
            _slotMask = _slotIndex.Length - 1;

            for (int i = 0; i < entityCount; i++)
            {
                _freeMetas[i] = entityCount - 1 - i;
                _lruPrev[i] = -1;
                _lruNext[i] = -1;
                _metas[i].Base = i * _perEntity;
            }
            _freeCount = entityCount;
            _lruHead = -1;
            _lruTail = -1;
        }

        public int EntityCapacity { get { return _metas.Length; } }
        public int PerEntityCapacity { get { return _perEntity; } }
        public long OverwriteCount { get { return _overwriteCount; } }
        public long EntityEvictionCount { get { return _entityEvictionCount; } }

        public MovementHistoryDiagnostics GetDiagnostics()
        {
            return new MovementHistoryDiagnostics(
                _trackedEntities, _metas.Length, _perEntity,
                _storedEntries, _overwriteCount, _entityEvictionCount);
        }

        public void Record(ulong entityId, long tick, ushort sequence, in TSnapshot snapshot)
        {
            if (entityId == 0UL || tick < 0L) return;

            int metaIndex = FindEntity(entityId);
            if (metaIndex < 0) metaIndex = AcquireEntity(entityId);
            if (metaIndex < 0) return;

            EntityMeta meta = _metas[metaIndex];
            int slotIndex = meta.Base + meta.Head;
            Slot slot = _slots[slotIndex];
            if (slot.IsValid)
            {
                slot.Generation++;
                _overwriteCount++;
            }
            else
            {
                _storedEntries++;
            }

            slot.EntityId = entityId;
            slot.Tick = tick;
            slot.Sequence = sequence;
            slot.Snapshot = snapshot;
            slot.IsValid = true;
            _slots[slotIndex] = slot;

            meta.Head++;
            if (meta.Head == _perEntity) meta.Head = 0;
            if (meta.Count < _perEntity) meta.Count++;
            meta.LastSeenTick = tick;
            _metas[metaIndex] = meta;

            WriteSlotIndex(entityId, tick, sequence, slotIndex, slot.Generation);
            TouchLru(metaIndex);
        }

        public bool TryGet(ulong entityId, long tick, ushort sequence, out TSnapshot snapshot)
        {
            snapshot = default;
            if (entityId == 0UL || tick < 0L) return false;

            ulong hash = SlotHash(entityId, tick, sequence);
            int start = (int)(hash & (ulong)_slotMask);
            for (int i = 0; i < PROBE_LIMIT; i++)
            {
                int index = (start + i) & _slotMask;
                SlotIndexEntry entry = _slotIndex[index];
                if (entry.Hash == 0UL) return false;
                if (entry.Hash != hash || entry.EntityId != entityId
                    || entry.Tick != tick || entry.Sequence != sequence)
                    continue;

                if (entry.SlotIndex < 0 || entry.SlotIndex >= _slots.Length) return false;
                Slot slot = _slots[entry.SlotIndex];
                if (!slot.IsValid || slot.Generation != entry.Generation
                    || slot.EntityId != entityId || slot.Tick != tick || slot.Sequence != sequence)
                    return false;

                snapshot = slot.Snapshot;
                return true;
            }
            return false;
        }

        public bool Contains(ulong entityId, long tick, ushort sequence)
        {
            TSnapshot ignored;
            return TryGet(entityId, tick, sequence, out ignored);
        }

        public bool TryGetLatest(ulong entityId, out TSnapshot snapshot, out long tick, out ushort sequence)
        {
            snapshot = default;
            tick = -1L;
            sequence = 0;
            int metaIndex = FindEntity(entityId);
            if (metaIndex < 0) return false;

            EntityMeta meta = _metas[metaIndex];
            if (meta.Count == 0) return false;

            int latest = meta.Head - 1;
            if (latest < 0) latest = _perEntity - 1;
            Slot slot = _slots[meta.Base + latest];
            if (!slot.IsValid || slot.EntityId != entityId) return false;

            snapshot = slot.Snapshot;
            tick = slot.Tick;
            sequence = slot.Sequence;
            return true;
        }

        public int PruneUpTo(ulong entityId, long acknowledgedTick)
        {
            int metaIndex = FindEntity(entityId);
            if (metaIndex < 0) return 0;

            EntityMeta meta = _metas[metaIndex];
            int removed = 0;
            for (int i = 0; i < _perEntity; i++)
            {
                int slotIndex = meta.Base + i;
                Slot slot = _slots[slotIndex];
                if (!slot.IsValid || slot.EntityId != entityId || slot.Tick > acknowledgedTick)
                    continue;
                slot.Generation++;
                slot.IsValid = false;
                slot.Snapshot = default;
                _slots[slotIndex] = slot;
                _storedEntries--;
                removed++;
            }
            if (removed > 0)
            {
                meta.Count -= removed;
                if (meta.Count < 0) meta.Count = 0;
                _metas[metaIndex] = meta;
            }
            return removed;
        }

        public int RemoveEntity(ulong entityId)
        {
            int metaIndex = FindEntity(entityId);
            if (metaIndex < 0) return 0;
            return ReleaseMeta(metaIndex);
        }

        public void Clear()
        {
            Array.Clear(_slots, 0, _slots.Length);
            Array.Clear(_entityIndex, 0, _entityIndex.Length);
            Array.Clear(_slotIndex, 0, _slotIndex.Length);
            for (int i = 0; i < _metas.Length; i++)
            {
                EntityMeta meta = _metas[i];
                meta.InUse = false;
                meta.EntityId = 0UL;
                meta.Count = 0;
                meta.Head = 0;
                meta.LastSeenTick = 0L;
                _metas[i] = meta;
                _lruPrev[i] = -1;
                _lruNext[i] = -1;
                _freeMetas[i] = _metas.Length - 1 - i;
            }
            _freeCount = _metas.Length;
            _lruHead = -1;
            _lruTail = -1;
            _trackedEntities = 0;
            _storedEntries = 0;
        }

        private int ReleaseMeta(int metaIndex)
        {
            EntityMeta meta = _metas[metaIndex];
            ulong entityId = meta.EntityId;
            int removed = 0;
            for (int i = 0; i < _perEntity; i++)
            {
                int slotIndex = meta.Base + i;
                Slot slot = _slots[slotIndex];
                if (!slot.IsValid || slot.EntityId != entityId) continue;
                slot.Generation++;
                slot.IsValid = false;
                slot.Snapshot = default;
                _slots[slotIndex] = slot;
                _storedEntries--;
                removed++;
            }

            meta.InUse = false;
            meta.EntityId = 0UL;
            meta.Count = 0;
            meta.Head = 0;
            meta.LastSeenTick = 0L;
            _metas[metaIndex] = meta;

            RemoveFromLru(metaIndex);
            _freeMetas[_freeCount++] = metaIndex;
            _trackedEntities--;
            if (entityId != 0UL) RemoveEntityIndex(entityId);
            return removed;
        }

        private int FindEntity(ulong entityId)
        {
            if (entityId == 0UL) return -1;
            int start = (int)(EntityHash(entityId) & (ulong)_entityMask);
            for (int i = 0; i < PROBE_LIMIT; i++)
            {
                int index = (start + i) & _entityMask;
                EntityIndexEntry entry = _entityIndex[index];
                if (entry.State == INDEX_EMPTY) return -1;
                if (entry.State == INDEX_TOMBSTONE || entry.EntityId != entityId) continue;
                if (entry.MetaIndex < 0 || entry.MetaIndex >= _metas.Length) continue;
                EntityMeta meta = _metas[entry.MetaIndex];
                if (meta.InUse && meta.EntityId == entityId) return entry.MetaIndex;
            }
            return -1;
        }

        private int AcquireEntity(ulong entityId)
        {
            if (_freeCount == 0)
            {
                int victim = _lruHead >= 0 ? _lruHead : 0;
                ReleaseMeta(victim);
                _entityEvictionCount++;
            }
            if (_freeCount == 0) return -1;

            int metaIndex = _freeMetas[--_freeCount];
            EntityMeta meta = _metas[metaIndex];
            meta.EntityId = entityId;
            meta.InUse = true;
            meta.Count = 0;
            meta.Head = 0;
            meta.LastSeenTick = 0L;
            _metas[metaIndex] = meta;
            _trackedEntities++;
            WriteEntityIndex(entityId, metaIndex);
            AddToLru(metaIndex);
            return metaIndex;
        }

        private void WriteEntityIndex(ulong entityId, int metaIndex)
        {
            int start = (int)(EntityHash(entityId) & (ulong)_entityMask);
            int tombstone = -1;
            for (int i = 0; i < PROBE_LIMIT; i++)
            {
                int index = (start + i) & _entityMask;
                EntityIndexEntry entry = _entityIndex[index];
                if (entry.State == INDEX_EMPTY)
                {
                    WriteEntityIndexAt(tombstone >= 0 ? tombstone : index, entityId, metaIndex);
                    return;
                }
                if (entry.State == INDEX_TOMBSTONE)
                {
                    if (tombstone < 0) tombstone = index;
                    continue;
                }
                if (entry.EntityId == entityId)
                {
                    WriteEntityIndexAt(index, entityId, metaIndex);
                    return;
                }
            }
            WriteEntityIndexAt(tombstone >= 0 ? tombstone : start, entityId, metaIndex);
        }

        private void WriteEntityIndexAt(int index, ulong entityId, int metaIndex)
        {
            EntityIndexEntry entry = default;
            entry.EntityId = entityId;
            entry.MetaIndex = metaIndex;
            entry.State = INDEX_OCCUPIED;
            _entityIndex[index] = entry;
        }

        private void RemoveEntityIndex(ulong entityId)
        {
            int start = (int)(EntityHash(entityId) & (ulong)_entityMask);
            for (int i = 0; i < PROBE_LIMIT; i++)
            {
                int index = (start + i) & _entityMask;
                EntityIndexEntry entry = _entityIndex[index];
                if (entry.State == INDEX_EMPTY) return;
                if (entry.State == INDEX_TOMBSTONE || entry.EntityId != entityId) continue;
                entry.State = INDEX_TOMBSTONE;
                entry.EntityId = 0UL;
                entry.MetaIndex = -1;
                _entityIndex[index] = entry;
                return;
            }
        }

        private void WriteSlotIndex(ulong entityId, long tick, ushort sequence, int slotIndex, uint generation)
        {
            ulong hash = SlotHash(entityId, tick, sequence);
            int start = (int)(hash & (ulong)_slotMask);
            for (int i = 0; i < PROBE_LIMIT; i++)
            {
                int index = (start + i) & _slotMask;
                SlotIndexEntry entry = _slotIndex[index];
                if (entry.Hash == 0UL
                    || (entry.EntityId == entityId && entry.Tick == tick && entry.Sequence == sequence))
                {
                    WriteSlotIndexAt(index, hash, entityId, tick, sequence, slotIndex, generation);
                    return;
                }
            }
            WriteSlotIndexAt(start, hash, entityId, tick, sequence, slotIndex, generation);
        }

        private void WriteSlotIndexAt(int index, ulong hash, ulong entityId, long tick,
            ushort sequence, int slotIndex, uint generation)
        {
            SlotIndexEntry entry = default;
            entry.Hash = hash;
            entry.EntityId = entityId;
            entry.Tick = tick;
            entry.Sequence = sequence;
            entry.SlotIndex = slotIndex;
            entry.Generation = generation;
            _slotIndex[index] = entry;
        }

        private void AddToLru(int metaIndex)
        {
            _lruPrev[metaIndex] = _lruTail;
            _lruNext[metaIndex] = -1;
            if (_lruTail >= 0) _lruNext[_lruTail] = metaIndex;
            _lruTail = metaIndex;
            if (_lruHead < 0) _lruHead = metaIndex;
        }

        private void RemoveFromLru(int metaIndex)
        {
            int previous = _lruPrev[metaIndex];
            int next = _lruNext[metaIndex];
            if (previous >= 0) _lruNext[previous] = next; else _lruHead = next;
            if (next >= 0) _lruPrev[next] = previous; else _lruTail = previous;
            _lruPrev[metaIndex] = -1;
            _lruNext[metaIndex] = -1;
        }

        private void TouchLru(int metaIndex)
        {
            if (_lruTail == metaIndex) return;
            RemoveFromLru(metaIndex);
            AddToLru(metaIndex);
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value) result <<= 1;
            return result;
        }

        private static ulong Mix(ulong value)
        {
            unchecked
            {
                value ^= value >> 30;
                value *= 0xBF58476D1CE4E5B9UL;
                value ^= value >> 27;
                value *= 0x94D049BB133111EBUL;
                value ^= value >> 31;
                return value == 0UL ? 1UL : value;
            }
        }

        private static ulong EntityHash(ulong entityId)
        {
            return Mix(entityId ^ 0x9E3779B97F4A7C15UL);
        }

        private static ulong SlotHash(ulong entityId, long tick, ushort sequence)
        {
            unchecked
            {
                ulong value = entityId;
                value ^= (ulong)tick * 0x9E3779B97F4A7C15UL;
                value ^= (ulong)sequence * 0xC2B2AE3D27D4EB4FUL;
                return Mix(value);
            }
        }
    }

    public sealed class MovementNetworkInputHistory
    {
        private readonly MovementEntityHistory<MovementInputCommandMessage> _history;

        public MovementNetworkInputHistory(MovementHistoryCapacity capacity)
        {
            _history = new MovementEntityHistory<MovementInputCommandMessage>(capacity);
        }

        public MovementNetworkInputHistory(int entityCount, int perEntityCount)
            : this(new MovementHistoryCapacity(entityCount, perEntityCount))
        {
        }

        public MovementHistoryDiagnostics GetDiagnostics()
        {
            return _history.GetDiagnostics();
        }

        public void Record(in MovementInputCommandMessage command)
        {
            _history.Record(command.EntityId, command.ClientTick, command.InputSequence, command);
        }

        public bool TryGet(ulong entityId, long clientTick, ushort inputSequence, out MovementInputCommandMessage command)
        {
            return _history.TryGet(entityId, clientTick, inputSequence, out command);
        }

        public bool TryGetLatest(ulong entityId, out MovementInputCommandMessage command, out long clientTick, out ushort inputSequence)
        {
            return _history.TryGetLatest(entityId, out command, out clientTick, out inputSequence);
        }

        public bool Contains(in MovementInputCommandMessage command)
        {
            return _history.Contains(command.EntityId, command.ClientTick, command.InputSequence);
        }

        public int PruneUpTo(in MovementInputAckMessage ack)
        {
            return _history.PruneUpTo(ack.EntityId, ack.AckedClientTick);
        }

        public int RemoveEntity(ulong entityId)
        {
            return _history.RemoveEntity(entityId);
        }

        public void Clear()
        {
            _history.Clear();
        }
    }
}
