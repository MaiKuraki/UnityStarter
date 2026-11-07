using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using CycloneGames.Hash.Core;

namespace CycloneGames.BehaviorTree.Core
{
    public delegate void BlackboardObserverCallback(int keyHash, RuntimeBlackboard bb);

    public delegate int StringHashFunction(string key);

    /// <summary>
    /// Selects the schema visibility boundary used by network serialization and hashing.
    /// Snapshot includes only keys marked for full snapshots. Networked includes every
    /// non-local primitive, including delta-only keys.
    /// </summary>
    public enum RuntimeBlackboardNetworkScope : byte
    {
        Snapshot = 1,
        Networked = 2
    }

    internal enum RuntimeBlackboardMutationKind : byte
    {
        Int = 1,
        Float = 2,
        Bool = 3,
        Vector3 = 4,
        Object = 5,
        Long = 6,
        Long2 = 7,
        Long3 = 8,
        Remove = byte.MaxValue
    }

    internal struct RuntimeBlackboardMutation
    {
        public int Key;
        public RuntimeBlackboardMutationKind Kind;
        public int IntValue;
        public float FloatValue;
        public bool BoolValue;
        public BTFloat3 VectorValue;
        public object ObjectValue;
        public long LongValue;
        public RuntimeBlackboardLong2 Long2Value;
        public RuntimeBlackboardLong3 Long3Value;
    }

#if UNITY_EDITOR
        public readonly struct RuntimeBlackboardDebugEntry
    {
        internal RuntimeBlackboardDebugEntry(
            int key,
            RuntimeBlackboardValueType valueType,
            int intValue = default,
            float floatValue = default,
            bool boolValue = default,
            BTFloat3 vectorValue = default,
            object objectValue = null,
            long longValue = default,
            RuntimeBlackboardLong2 long2Value = default,
            RuntimeBlackboardLong3 long3Value = default)
        {
            Key = key;
            ValueType = valueType;
            IntValue = intValue;
            FloatValue = floatValue;
            BoolValue = boolValue;
            VectorValue = vectorValue;
            ObjectValue = objectValue;
            LongValue = longValue;
            Long2Value = long2Value;
            Long3Value = long3Value;
        }

        public int Key { get; }
        public RuntimeBlackboardValueType ValueType { get; }
        public int IntValue { get; }
        public float FloatValue { get; }
        public bool BoolValue { get; }
        public BTFloat3 VectorValue { get; }
        public object ObjectValue { get; }
        public long LongValue { get; }
        public RuntimeBlackboardLong2 Long2Value { get; }
        public RuntimeBlackboardLong3 Long3Value { get; }
    }
#endif

    public readonly struct RuntimeBlackboardSerializationLimits
    {
        public const int DEFAULT_MAX_ENTRIES_PER_TYPE = 4096;
        public const int DEFAULT_MAX_TOTAL_ENTRIES = 16384;

        public RuntimeBlackboardSerializationLimits(int maxEntriesPerType, int maxTotalEntries)
        {
            MaxEntriesPerType = maxEntriesPerType > 0 ? maxEntriesPerType : DEFAULT_MAX_ENTRIES_PER_TYPE;
            MaxTotalEntries = maxTotalEntries > 0 ? maxTotalEntries : DEFAULT_MAX_TOTAL_ENTRIES;
        }

        public int MaxEntriesPerType { get; }
        public int MaxTotalEntries { get; }

        public static RuntimeBlackboardSerializationLimits Default =>
            new RuntimeBlackboardSerializationLimits(DEFAULT_MAX_ENTRIES_PER_TYPE, DEFAULT_MAX_TOTAL_ENTRIES);
    }

    /// <summary>
    /// Typed key/value store with optional strict schema, per-key stamps and observers.
    /// Storage is a single open-addressed table; steady-state mutations allocate nothing.
    /// </summary>
    public class RuntimeBlackboard : IDisposable
    {
        private const byte TYPE_INT = 1;
        private const byte TYPE_FLOAT = 2;
        private const byte TYPE_BOOL = 3;
        private const byte TYPE_VECTOR3 = 4;
        private const byte TYPE_OBJECT = 5;
        private const byte TYPE_LONG = 6;
        private const byte TYPE_LONG2 = 7;
        private const byte TYPE_LONG3 = 8;

        private const int Tombstone = -1;

        private struct RuntimeBlackboardCell
        {
            public int Key;
            public byte Type;
            public ulong Stamp;
            public int IntValue;
            public float FloatValue;
            public bool BoolValue;
            public BTFloat3 VectorValue;
            public long LongValue;
            public RuntimeBlackboardLong2 Long2Value;
            public RuntimeBlackboardLong3 Long3Value;
            public object ObjectValue;
        }

        // Single contiguous open-addressed table. _buckets stores cellIndex+1 (0 = empty,
        // Tombstone = deleted) and is probed linearly from HashKey; _cells[0.._count) are
        // the live entries in dense order. A probe chain ends only at a 0 bucket, so
        // tombstones must survive until a rebuild or keys further down become unreachable.
        // Deletion tombstones one bucket and swap-removes the dense cell, repointing the
        // moved cell's bucket. _used counts live + tombstones; crossing the load threshold
        // rebuilds (doubling when live entries dominate) to bound probe length and reclaim
        // tombstones.
        private RuntimeBlackboardCell[] _cells;
        private int[] _buckets;
        private int _count;
        private int _used;

        private ulong _sequenceId;
        private readonly List<int> _sortedKeyScratch;

        internal RuntimeDeterministicRandom _defaultRandom;

        // _observerCount is a fast gate for the common no-observer case. An observer
        // registered concurrently with a write is not guaranteed to observe that write.
        private readonly object _observerGate = new object();
        private int _observerCount;
        private Dictionary<int, BlackboardObserverCallback[]> _keyObservers;
        private BlackboardObserverCallback[] _globalObservers;

        // Storage is single-threaded by default; the optional lock guards every access.
        // Serialization and hashing reuse the instance scratch, so they take the exclusive
        // write lock even though they do not mutate values.
        private ReaderWriterLockSlim _lock;
        private RuntimeBlackboardSchema _schema;

        private RuntimeBlackboard _parent;
        private IRuntimeBTContext _context;

        public RuntimeBlackboard Parent
        {
            get => _parent;
            set
            {
                ThrowIfDisposed();
                ValidateParent(value);
                _parent = value;
            }
        }

        public IRuntimeBTContext Context
        {
            get => _context;
            set
            {
                ThrowIfDisposed();
                _context = value;
            }
        }

        public RuntimeBlackboardSchema Schema => _schema;
        public bool IsDisposed => _disposed;

        private StringHashFunction _stringHashFunc;

        public static StringHashFunction DefaultStringHashFunc { get; } = BTHash.FNV1A;

        public StringHashFunction StringHashFunc
        {
            get
            {
                ThrowIfDisposed();
                return _stringHashFunc ?? DefaultStringHashFunc;
            }
            set
            {
                ThrowIfDisposed();
                _stringHashFunc = value;
            }
        }

        private int Hash(string key)
        {
            ThrowIfDisposed();
            if (string.IsNullOrEmpty(key))
            {
                throw new ArgumentException("Blackboard key cannot be null or empty.", nameof(key));
            }
            return (_stringHashFunc ?? DefaultStringHashFunc)(key);
        }

        public RuntimeBlackboard(
            RuntimeBlackboard parent = null,
            int initialCapacity = 8,
            RuntimeBlackboardSchema schema = null,
            bool applySchemaDefaults = true)
        {
            Parent = parent;
            int bucketCount = ComputeBucketCount(initialCapacity > 0 ? initialCapacity : 8);
            _buckets = new int[bucketCount];
            _cells = new RuntimeBlackboardCell[bucketCount];
            _count = 0;
            _used = 0;
            _sortedKeyScratch = new List<int>(bucketCount);
            _defaultRandom = new RuntimeDeterministicRandom(0u);
            _schema = schema;

            if (_schema != null && applySchemaDefaults)
            {
                ApplySchemaDefaults();
            }
        }

        public void BindSchema(RuntimeBlackboardSchema schema, bool applyDefaults = true)
        {
            ThrowIfDisposed();
            if (schema == null)
            {
                throw new ArgumentNullException(nameof(schema));
            }

            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                ValidateExistingValuesAgainstSchema(schema);
                _schema = schema;
                if (applyDefaults)
                {
                    ApplySchemaDefaults();
                }
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Protects the value table for concurrent reads and writes.
        /// Observer callbacks still run synchronously on the writing thread; Parent,
        /// Context, tree execution, and Unity objects are not made thread-safe.
        /// Call only during setup and quiesce all users before Dispose.
        /// </summary>
        public void EnableConcurrentStorageAccess()
        {
            ThrowIfDisposed();
            _lock ??= new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        public T GetContextOwner<T>() where T : class
        {
            ThrowIfDisposed();
            if (Context != null)
            {
                var ownerFromContext = Context.GetOwner<T>();
                if (ownerFromContext != null) return ownerFromContext;
            }
            return Parent != null ? Parent.GetContextOwner<T>() : null;
        }

        public T GetService<T>() where T : class
        {
            ThrowIfDisposed();
            if (Context != null)
            {
                var serviceFromContext = Context.GetService<T>();
                if (serviceFromContext != null) return serviceFromContext;
            }
            return Parent != null ? Parent.GetService<T>() : null;
        }

        internal int NextDefaultInt(int minInclusive, int maxExclusive)
        {
            return _defaultRandom.NextInt(minInclusive, maxExclusive);
        }

        internal float NextDefaultFloat()
        {
            return _defaultRandom.NextFloat();
        }

        public void ResetDefaultRandom(uint seed = 0u)
        {
            ThrowIfDisposed();
            _defaultRandom = new RuntimeDeterministicRandom(seed);
        }

        #region Int-Key Methods
        public void SetInt(int key, int value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetIntCore(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public int GetInt(int key, int defaultValue = 0)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_INT) return _cells[cellIndex].IntValue;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetInt(key, defaultValue) : defaultValue;
        }

        public void SetFloat(int key, float value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetFloatCore(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public float GetFloat(int key, float defaultValue = 0f)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_FLOAT) return _cells[cellIndex].FloatValue;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetFloat(key, defaultValue) : defaultValue;
        }

        public void SetBool(int key, bool value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetBoolCore(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public bool GetBool(int key, bool defaultValue = false)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_BOOL) return _cells[cellIndex].BoolValue;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetBool(key, defaultValue) : defaultValue;
        }

        public void SetFloat3(int key, BTFloat3 value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetFloat3Core(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public BTFloat3 GetFloat3(int key, BTFloat3 defaultValue = default)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_VECTOR3) return _cells[cellIndex].VectorValue;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetFloat3(key, defaultValue) : defaultValue;
        }

        public void SetLong(int key, long value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetLongCore(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public long GetLong(int key, long defaultValue = 0L)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_LONG) return _cells[cellIndex].LongValue;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetLong(key, defaultValue) : defaultValue;
        }

        public void SetLong2(int key, RuntimeBlackboardLong2 value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetLong2Core(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public RuntimeBlackboardLong2 GetLong2(int key, RuntimeBlackboardLong2 defaultValue = default)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_LONG2) return _cells[cellIndex].Long2Value;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetLong2(key, defaultValue) : defaultValue;
        }

        public void SetLong3(int key, RuntimeBlackboardLong3 value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetLong3Core(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public RuntimeBlackboardLong3 GetLong3(int key, RuntimeBlackboardLong3 defaultValue = default)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_LONG3) return _cells[cellIndex].Long3Value;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetLong3(key, defaultValue) : defaultValue;
        }

        public void SetObject(int key, object value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetObjectCore(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public T GetObject<T>(int key)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_OBJECT && _cells[cellIndex].ObjectValue is T tVal) return tVal;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetObject<T>(key) : default;
        }

        public ulong GetStamp(int key)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0) return _cells[cellIndex].Stamp;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetStamp(key) : 0;
        }

        public bool HasKey(int key)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                if (FindCellIndex(key) >= 0) return true;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null && Parent.HasKey(key);
        }

        public void Remove(int key)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex < 0)
                {
                    changed = false;
                }
                else
                {
                    NextSequence();
                    RemoveCellAt(key, cellIndex);
                    changed = true;
                }
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }
        #endregion

        #region String-Key Convenience Methods
        public void SetInt(string key, int value) => SetInt(Hash(key), value);
        public int GetInt(string key, int defaultValue = 0) => GetInt(Hash(key), defaultValue);

        public void SetFloat(string key, float value) => SetFloat(Hash(key), value);
        public float GetFloat(string key, float defaultValue = 0f) => GetFloat(Hash(key), defaultValue);

        public void SetBool(string key, bool value) => SetBool(Hash(key), value);
        public bool GetBool(string key, bool defaultValue = false) => GetBool(Hash(key), defaultValue);

        public void SetFloat3(string key, BTFloat3 value) => SetFloat3(Hash(key), value);
        public BTFloat3 GetFloat3(string key, BTFloat3 defaultValue = default) => GetFloat3(Hash(key), defaultValue);

        public void SetLong(string key, long value) => SetLong(Hash(key), value);
        public long GetLong(string key, long defaultValue = 0L) => GetLong(Hash(key), defaultValue);

        public void SetLong2(string key, RuntimeBlackboardLong2 value) => SetLong2(Hash(key), value);
        public RuntimeBlackboardLong2 GetLong2(string key, RuntimeBlackboardLong2 defaultValue = default) => GetLong2(Hash(key), defaultValue);

        public void SetLong3(string key, RuntimeBlackboardLong3 value) => SetLong3(Hash(key), value);
        public RuntimeBlackboardLong3 GetLong3(string key, RuntimeBlackboardLong3 defaultValue = default) => GetLong3(Hash(key), defaultValue);

        public void SetObject(string key, object value) => SetObject(Hash(key), value);
        public T GetObject<T>(string key) => GetObject<T>(Hash(key));

        public bool HasKey(string key) => HasKey(Hash(key));
        public void Remove(string key) => Remove(Hash(key));
        public ulong GetStamp(string key) => GetStamp(Hash(key));
        #endregion

        #region TryGet Methods (typed probing)
        public bool TryGetInt(int key, out int value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_INT)
                {
                    value = _cells[cellIndex].IntValue;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetFloat(int key, out float value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_FLOAT)
                {
                    value = _cells[cellIndex].FloatValue;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetBool(int key, out bool value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_BOOL)
                {
                    value = _cells[cellIndex].BoolValue;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetFloat3(int key, out BTFloat3 value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_VECTOR3)
                {
                    value = _cells[cellIndex].VectorValue;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong(int key, out long value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_LONG)
                {
                    value = _cells[cellIndex].LongValue;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong2(int key, out RuntimeBlackboardLong2 value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_LONG2)
                {
                    value = _cells[cellIndex].Long2Value;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong3(int key, out RuntimeBlackboardLong3 value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_LONG3)
                {
                    value = _cells[cellIndex].Long3Value;
                    return true;
                }
                value = default;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetObject<T>(int key, out T value) where T : class
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0 && _cells[cellIndex].Type == TYPE_OBJECT && _cells[cellIndex].ObjectValue is T tVal)
                {
                    value = tVal;
                    return true;
                }
                value = null;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetInt(string key, out int value) => TryGetInt(Hash(key), out value);
        public bool TryGetFloat(string key, out float value) => TryGetFloat(Hash(key), out value);
        public bool TryGetBool(string key, out bool value) => TryGetBool(Hash(key), out value);
        public bool TryGetFloat3(string key, out BTFloat3 value) => TryGetFloat3(Hash(key), out value);
        public bool TryGetLong(string key, out long value) => TryGetLong(Hash(key), out value);
        public bool TryGetLong2(string key, out RuntimeBlackboardLong2 value) => TryGetLong2(Hash(key), out value);
        public bool TryGetLong3(string key, out RuntimeBlackboardLong3 value) => TryGetLong3(Hash(key), out value);
        public bool TryGetObject<T>(string key, out T value) where T : class => TryGetObject<T>(Hash(key), out value);
        #endregion

        public void Clear()
        {
            ThrowIfDisposed();
            int[] removedKeys = null;
            int removedCount = 0;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                removedCount = _count;
                if (removedCount > 0)
                {
                    NextSequence();
                    removedKeys = ArrayPool<int>.Shared.Rent(removedCount);
                    for (int i = 0; i < _count; i++)
                    {
                        removedKeys[i] = _cells[i].Key;
                    }

                    Array.Sort(removedKeys, 0, removedCount);
                }

                ClearStorageNoNotify();
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (removedCount == 0)
            {
                return;
            }

            try
            {
                NotifyObserversAfterCommit(removedKeys, removedCount);
            }
            finally
            {
                ArrayPool<int>.Shared.Return(removedKeys, clearArray: true);
            }
        }

        /// <summary>
        /// Atomically replaces all local values with this blackboard's schema defaults. An
        /// unbound schema resets to an empty local blackboard. Observers run after commit and see
        /// only the final state; each changed key is published once in stable hash order.
        /// </summary>
        public void ResetToSchemaDefaults()
        {
            ThrowIfDisposed();
            int[] changedKeys = null;
            int changedCount = 0;

            try
            {
                if (_lock != null) _lock.EnterWriteLock();
                try
                {
                    RuntimeBlackboardSchema schema = _schema;
                    int schemaCount = schema != null ? schema.Count : 0;
                    int candidateCapacity = checked(_count + schemaCount);
                    _sortedKeyScratch.Clear();
                    for (int i = 0; i < _count; i++)
                    {
                        _sortedKeyScratch.Add(_cells[i].Key);
                    }

                    for (int i = 0; i < schemaCount; i++)
                    {
                        RuntimeBlackboardKeyDefinition entry = schema.GetEntry(i);
                        if (!entry.HasDefaultValue)
                        {
                            continue;
                        }

                        _sortedKeyScratch.Add(entry.KeyHash);
                    }

                    _sortedKeyScratch.Sort();
                    int previousKey = 0;
                    bool hasPreviousKey = false;
                    for (int i = 0; i < _sortedKeyScratch.Count; i++)
                    {
                        int key = _sortedKeyScratch[i];
                        if (hasPreviousKey && key == previousKey)
                        {
                            continue;
                        }

                        previousKey = key;
                        hasPreviousKey = true;
                        bool hasExistingValue = FindCellIndex(key) >= 0;
                        RuntimeBlackboardKeyDefinition definition = default;
                        bool hasDefault =
                            schema != null &&
                            schema.TryGetDefinition(key, out definition) &&
                            definition.HasDefaultValue;
                        bool changed = hasDefault
                            ? !MatchesSchemaDefault(definition)
                            : hasExistingValue;
                        if (!changed)
                        {
                            continue;
                        }

                        changedKeys ??= ArrayPool<int>.Shared.Rent(candidateCapacity);
                        changedKeys[changedCount++] = key;
                    }

                    EnsureSequenceCapacity(changedCount);
                    for (int i = 0; i < changedCount; i++)
                    {
                        int key = changedKeys[i];
                        if (schema != null &&
                            schema.TryGetDefinition(key, out RuntimeBlackboardKeyDefinition definition) &&
                            definition.HasDefaultValue)
                        {
                            ApplySchemaDefault(definition);
                            continue;
                        }

                        int existingIndex = FindCellIndex(key);
                        if (existingIndex >= 0)
                        {
                            NextSequence();
                            RemoveCellAt(key, existingIndex);
                        }
                    }
                }
                finally
                {
                    _sortedKeyScratch.Clear();
                    if (_lock != null) _lock.ExitWriteLock();
                }

                NotifyObserversAfterCommit(changedKeys, changedCount);
            }
            finally
            {
                if (changedKeys != null)
                {
                    ArrayPool<int>.Shared.Return(changedKeys, clearArray: true);
                }
            }
        }

        #region Serialization (Network Sync)
        // Object values are local-only and never enter the wire payload.
        public void WriteTo(BinaryWriter writer)
        {
            WriteTo(writer, RuntimeBlackboardNetworkScope.Snapshot);
        }

        public void WriteTo(BinaryWriter writer, RuntimeBlackboardNetworkScope scope)
        {
            WriteTo(writer, scope, int.MaxValue, out _);
        }

        /// <summary>
        /// Writes a bounded payload and returns the exact blackboard revision captured under
        /// the same storage lock. No payload bytes are written when the computed size exceeds maxBytes.
        /// </summary>
        public ulong WriteTo(BinaryWriter writer, RuntimeBlackboardNetworkScope scope, int maxBytes)
        {
            WriteTo(writer, scope, maxBytes, out ulong revision);
            return revision;
        }

        internal void WriteTo(
            BinaryWriter writer,
            RuntimeBlackboardNetworkScope scope,
            int maxBytes,
            out ulong revision)
        {
            ThrowIfDisposed();
            if (writer == null) throw new ArgumentNullException(nameof(writer));
            ValidateNetworkScope(scope);
            if (maxBytes <= 0) throw new ArgumentOutOfRangeException(nameof(maxBytes));
            // Serialization reuses the instance sorting scratch, so concurrent readers
            // must be excluded even though the stored values are not modified.
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                int byteCount = GetSerializedByteCountCore(scope);
                if (byteCount > maxBytes)
                {
                    throw new InvalidDataException(
                        $"RuntimeBlackboard {scope} payload size {byteCount} exceeds max bytes {maxBytes}.");
                }

                revision = _sequenceId;
                writer.Write(_sequenceId);

                // Wire contract: keys within each typed section are emitted in ascending
                // order so payloads and hashes stay deterministic across peers.
                WriteTypeSection(writer, TYPE_INT, scope);
                WriteTypeSection(writer, TYPE_FLOAT, scope);
                WriteTypeSection(writer, TYPE_BOOL, scope);
                WriteTypeSection(writer, TYPE_VECTOR3, scope);
                WriteTypeSection(writer, TYPE_LONG, scope);
                WriteTypeSection(writer, TYPE_LONG2, scope);
                WriteTypeSection(writer, TYPE_LONG3, scope);
                WritePrimitiveStampsOrdered(writer, scope);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        public void ReadFrom(BinaryReader reader)
        {
            ThrowIfDisposed();
            ReadFrom(reader, RuntimeBlackboardSerializationLimits.Default, RuntimeBlackboardNetworkScope.Snapshot);
        }

        public void ReadFrom(BinaryReader reader, RuntimeBlackboardSerializationLimits limits)
        {
            ReadFrom(reader, limits, RuntimeBlackboardNetworkScope.Snapshot);
        }

        public void ReadFrom(
            BinaryReader reader,
            RuntimeBlackboardSerializationLimits limits,
            RuntimeBlackboardNetworkScope scope)
        {
            ThrowIfDisposed();
            if (reader == null) throw new ArgumentNullException(nameof(reader));
            ValidateNetworkScope(scope);
            ulong sequenceId = reader.ReadUInt64();
            int totalEntryCount = 0;

            int intCount = ReadValidatedEntryCount(reader, "int", limits, ref totalEntryCount, 8);
            var intData = new Dictionary<int, int>(intCount);
            var allKeys = new HashSet<int>(intCount);
            var typeByKey = new Dictionary<int, byte>(intCount);
            for (int i = 0; i < intCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_INT;
                intData[key] = reader.ReadInt32();
            }

            int floatCount = ReadValidatedEntryCount(reader, "float", limits, ref totalEntryCount, 8);
            var floatData = new Dictionary<int, float>(floatCount);
            for (int i = 0; i < floatCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_FLOAT;
                floatData[key] = reader.ReadSingle();
            }

            int boolCount = ReadValidatedEntryCount(reader, "bool", limits, ref totalEntryCount, 5);
            var boolData = new Dictionary<int, bool>(boolCount);
            for (int i = 0; i < boolCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_BOOL;
                byte boolValue = reader.ReadByte();
                if (boolValue > 1)
                {
                    throw new InvalidDataException(
                        $"RuntimeBlackboard serialized bool value for key {key} must be 0 or 1.");
                }

                boolData[key] = boolValue != 0;
            }

            int vecCount = ReadValidatedEntryCount(reader, "vector3", limits, ref totalEntryCount, 16);
            var vectorData = new Dictionary<int, BTFloat3>(vecCount);
            for (int i = 0; i < vecCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_VECTOR3;
                vectorData[key] = new BTFloat3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            int longCount = ReadValidatedEntryCount(reader, "long", limits, ref totalEntryCount, 12);
            var longData = new Dictionary<int, long>(longCount);
            for (int i = 0; i < longCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_LONG;
                longData[key] = reader.ReadInt64();
            }

            int long2Count = ReadValidatedEntryCount(reader, "long2", limits, ref totalEntryCount, 20);
            var long2Data = new Dictionary<int, RuntimeBlackboardLong2>(long2Count);
            for (int i = 0; i < long2Count; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_LONG2;
                long2Data[key] = new RuntimeBlackboardLong2(reader.ReadInt64(), reader.ReadInt64());
            }

            int long3Count = ReadValidatedEntryCount(reader, "long3", limits, ref totalEntryCount, 28);
            var long3Data = new Dictionary<int, RuntimeBlackboardLong3>(long3Count);
            for (int i = 0; i < long3Count; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_LONG3;
                long3Data[key] = new RuntimeBlackboardLong3(reader.ReadInt64(), reader.ReadInt64(), reader.ReadInt64());
            }

            int stampCount = reader.ReadInt32();
            if (stampCount != allKeys.Count)
            {
                throw new InvalidDataException("RuntimeBlackboard serialized stamp count must match serialized primitive key count.");
            }

            EnsureRemainingBytes(reader, stampCount, 12, "stamp");

            var stamps = new Dictionary<int, ulong>(stampCount);
            for (int i = 0; i < stampCount; i++)
            {
                int key = reader.ReadInt32();
                if (!allKeys.Contains(key))
                {
                    throw new InvalidDataException($"RuntimeBlackboard serialized stamp key {key} does not have a value.");
                }

                if (stamps.ContainsKey(key))
                {
                    throw new InvalidDataException($"RuntimeBlackboard serialized stamp key {key} appears more than once.");
                }

                ulong remoteStamp = reader.ReadUInt64();
                if (remoteStamp == 0 || remoteStamp > sequenceId)
                {
                    throw new InvalidDataException(
                        $"RuntimeBlackboard serialized stamp {remoteStamp} for key {key} is outside sequence {sequenceId}.");
                }

                stamps[key] = remoteStamp;
            }

            if (stamps.Count != allKeys.Count)
            {
                throw new InvalidDataException("RuntimeBlackboard serialized stamps must match all serialized primitive keys.");
            }

            int[] changedKeys = null;
            int changedCount = 0;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                // BindSchema may run concurrently. Validate against the schema that is
                // protected by the same write lock as the following commit.
                ValidateSerializedKeysAgainstSchema(typeByKey, scope);

                for (int i = 0; i < _count; i++)
                {
                    RuntimeBlackboardCell cell = _cells[i];
                    if (cell.Type == TYPE_OBJECT && allKeys.Contains(cell.Key))
                    {
                        throw new InvalidDataException(
                            $"RuntimeBlackboard serialized primitive key {cell.Key} conflicts with a local object value.");
                    }
                }

                var changedKeySet = new HashSet<int>(allKeys);
                for (int i = 0; i < _count; i++)
                {
                    RuntimeBlackboardCell cell = _cells[i];
                    if (cell.Type != TYPE_OBJECT && ShouldIncludeKey(cell.Key, scope))
                    {
                        changedKeySet.Add(cell.Key);
                    }
                }

                changedCount = changedKeySet.Count;
                EnsureSequenceCapacity(changedCount);
                if (changedCount > 0)
                {
                    changedKeys = ArrayPool<int>.Shared.Rent(changedCount);
                    int changedIndex = 0;
                    foreach (int key in changedKeySet)
                    {
                        changedKeys[changedIndex++] = key;
                    }

                    Array.Sort(changedKeys, 0, changedCount);
                }

                RemoveScopeValues(scope);
                for (int i = 0; i < changedCount; i++)
                {
                    if (!allKeys.Contains(changedKeys[i]))
                    {
                        NextSequence();
                    }
                }

                foreach (KeyValuePair<int, int> item in intData)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_INT, Stamp = NextSequence(), IntValue = item.Value });

                foreach (KeyValuePair<int, float> item in floatData)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_FLOAT, Stamp = NextSequence(), FloatValue = item.Value });

                foreach (KeyValuePair<int, bool> item in boolData)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_BOOL, Stamp = NextSequence(), BoolValue = item.Value });

                foreach (KeyValuePair<int, BTFloat3> item in vectorData)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_VECTOR3, Stamp = NextSequence(), VectorValue = item.Value });

                foreach (KeyValuePair<int, long> item in longData)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_LONG, Stamp = NextSequence(), LongValue = item.Value });

                foreach (KeyValuePair<int, RuntimeBlackboardLong2> item in long2Data)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_LONG2, Stamp = NextSequence(), Long2Value = item.Value });

                foreach (KeyValuePair<int, RuntimeBlackboardLong3> item in long3Data)
                    CommitWireValue(new RuntimeBlackboardCell {
                        Key = item.Key, Type = TYPE_LONG3, Stamp = NextSequence(), Long3Value = item.Value });
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changedCount > 0)
            {
                try
                {
                    NotifyObserversAfterCommit(changedKeys, changedCount);
                }
                finally
                {
                    ArrayPool<int>.Shared.Return(changedKeys, clearArray: true);
                }
            }
        }

        public ulong ComputeHash()
        {
            return ComputeHash(RuntimeBlackboardNetworkScope.Networked);
        }

        public ulong ComputeHash(RuntimeBlackboardNetworkScope scope)
        {
            ThrowIfDisposed();
            ValidateNetworkScope(scope);
            // Hashing reuses the instance sorting scratch, so concurrent readers must
            // be excluded to keep the hash deterministic.
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                const ulong FNV_OFFSET = Fnv1a64.OffsetBasis;
                const ulong FNV_PRIME = Fnv1a64.Prime;
                ulong hash = (FNV_OFFSET ^ (byte)scope) * FNV_PRIME;

                FillSortedKeys(TYPE_INT, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardCell cell = _cells[FindCellIndex(key)];
                    hash = (hash ^ TYPE_INT) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = (hash ^ (uint)cell.IntValue) * FNV_PRIME;
                }

                FillSortedKeys(TYPE_FLOAT, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardCell cell = _cells[FindCellIndex(key)];
                    hash = (hash ^ TYPE_FLOAT) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    var u = new FloatUintUnion { FloatValue = cell.FloatValue };
                    hash = (hash ^ u.UintValue) * FNV_PRIME;
                }

                FillSortedKeys(TYPE_BOOL, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardCell cell = _cells[FindCellIndex(key)];
                    hash = (hash ^ TYPE_BOOL) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = (hash ^ (cell.BoolValue ? 1u : 0u)) * FNV_PRIME;
                }

                FillSortedKeys(TYPE_VECTOR3, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    BTFloat3 value = _cells[FindCellIndex(key)].VectorValue;
                    hash = (hash ^ TYPE_VECTOR3) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    var ux = new FloatUintUnion { FloatValue = value.X };
                    var uy = new FloatUintUnion { FloatValue = value.Y };
                    var uz = new FloatUintUnion { FloatValue = value.Z };
                    hash = (hash ^ ux.UintValue) * FNV_PRIME;
                    hash = (hash ^ uy.UintValue) * FNV_PRIME;
                    hash = (hash ^ uz.UintValue) * FNV_PRIME;
                }

                FillSortedKeys(TYPE_LONG, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardCell cell = _cells[FindCellIndex(key)];
                    hash = (hash ^ TYPE_LONG) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, cell.LongValue);
                }

                FillSortedKeys(TYPE_LONG2, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardLong2 value = _cells[FindCellIndex(key)].Long2Value;
                    hash = (hash ^ TYPE_LONG2) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, value.X);
                    hash = MixInt64(hash, value.Y);
                }

                FillSortedKeys(TYPE_LONG3, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardLong3 value = _cells[FindCellIndex(key)].Long3Value;
                    hash = (hash ^ TYPE_LONG3) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, value.X);
                    hash = MixInt64(hash, value.Y);
                    hash = MixInt64(hash, value.Z);
                }

                return hash;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        private static ulong MixInt64(ulong hash, long value)
        {
            const ulong FNV_PRIME = Fnv1a64.Prime;
            ulong raw = unchecked((ulong)value);
            hash = (hash ^ (uint)raw) * FNV_PRIME;
            return (hash ^ (uint)(raw >> 32)) * FNV_PRIME;
        }

        [StructLayout(LayoutKind.Explicit)]
        private struct FloatUintUnion
        {
            [FieldOffset(0)] public float FloatValue;
            [FieldOffset(0)] public uint UintValue;
        }

        private static bool FloatBitsEqual(float left, float right)
        {
            var leftBits = new FloatUintUnion { FloatValue = left };
            var rightBits = new FloatUintUnion { FloatValue = right };
            return leftBits.UintValue == rightBits.UintValue;
        }

        private static bool VectorBitsEqual(BTFloat3 left, BTFloat3 right)
        {
            return BTFloat3.BitEquals(left, right);
        }
        #endregion

        #region Observer System
        public void AddObserver(int keyHash, BlackboardObserverCallback callback)
        {
            ThrowIfDisposed();
            if (callback == null)
            {
                return;
            }

            lock (_observerGate)
            {
                _keyObservers ??= new Dictionary<int, BlackboardObserverCallback[]>(4);
                if (!_keyObservers.TryGetValue(keyHash, out var observers))
                {
                    _keyObservers[keyHash] = new[] { callback };
                    Interlocked.Increment(ref _observerCount);
                    return;
                }

                int before = observers.Length;
                var updated = AddObserverCallback(observers, callback);
                _keyObservers[keyHash] = updated;
                if (updated.Length > before)
                {
                    Interlocked.Increment(ref _observerCount);
                }
            }
        }

        public void AddObserver(string key, BlackboardObserverCallback callback)
        {
            AddObserver(Hash(key), callback);
        }

        public void RemoveObserver(int keyHash, BlackboardObserverCallback callback)
        {
            ThrowIfDisposed();
            lock (_observerGate)
            {
                if (_keyObservers == null)
                {
                    return;
                }

                if (_keyObservers.TryGetValue(keyHash, out var observers))
                {
                    int before = observers.Length;
                    observers = RemoveObserverCallback(observers, callback);
                    if (observers.Length < before)
                    {
                        Interlocked.Decrement(ref _observerCount);
                    }
                    if (observers.Length == 0)
                    {
                        _keyObservers.Remove(keyHash);
                        return;
                    }

                    _keyObservers[keyHash] = observers;
                }
            }
        }

        public void RemoveObserver(string key, BlackboardObserverCallback callback)
        {
            RemoveObserver(Hash(key), callback);
        }

        public void AddGlobalObserver(BlackboardObserverCallback callback)
        {
            ThrowIfDisposed();
            if (callback == null)
            {
                return;
            }

            lock (_observerGate)
            {
                int before = _globalObservers != null ? _globalObservers.Length : 0;
                var updated = AddObserverCallback(_globalObservers, callback);
                _globalObservers = updated;
                if (updated.Length > before)
                {
                    Interlocked.Increment(ref _observerCount);
                }
            }
        }

        public void RemoveGlobalObserver(BlackboardObserverCallback callback)
        {
            ThrowIfDisposed();
            lock (_observerGate)
            {
                int before = _globalObservers != null ? _globalObservers.Length : 0;
                var updated = RemoveObserverCallback(_globalObservers, callback);
                _globalObservers = updated;
                if (updated != null && updated.Length < before)
                {
                    Interlocked.Decrement(ref _observerCount);
                }
            }
        }

        public void ClearAllObservers()
        {
            ThrowIfDisposed();
            lock (_observerGate)
            {
                _keyObservers?.Clear();
                _globalObservers = null;
                Volatile.Write(ref _observerCount, 0);
            }
        }

        private void NotifyObservers(int keyHash)
        {
            if (Volatile.Read(ref _observerCount) == 0)
            {
                return;
            }

            BlackboardObserverCallback[] keyObservers = null;
            BlackboardObserverCallback[] globalObservers = null;

            lock (_observerGate)
            {
                if (_keyObservers != null)
                {
                    _keyObservers.TryGetValue(keyHash, out keyObservers);
                }

                globalObservers = _globalObservers;
            }

            List<Exception> failures = null;
            if (keyObservers != null)
            {
                for (int i = 0; i < keyObservers.Length; i++)
                {
                    try
                    {
                        keyObservers[i](keyHash, this);
                    }
                    catch (Exception exception)
                    {
                        failures ??= new List<Exception>(1);
                        failures.Add(exception);
                    }
                }
            }

            if (globalObservers != null)
            {
                for (int i = 0; i < globalObservers.Length; i++)
                {
                    try
                    {
                        globalObservers[i](keyHash, this);
                    }
                    catch (Exception exception)
                    {
                        failures ??= new List<Exception>(1);
                        failures.Add(exception);
                    }
                }
            }

            if (failures != null)
            {
                throw new AggregateException(
                    "RuntimeBlackboard value was committed, but one or more post-commit observers failed.",
                    failures);
            }
        }
        #endregion

        #region IDisposable
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;

            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                ClearStorageNoNotify();
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            lock (_observerGate)
            {
                _keyObservers?.Clear();
                _globalObservers = null;
                Volatile.Write(ref _observerCount, 0);
            }
            _lock?.Dispose();
            _lock = null;
            _parent = null;
            _context = null;
            _disposed = true;
        }
        #endregion

        private void ThrowIfDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(RuntimeBlackboard));
            }
        }

        private void ValidateParent(RuntimeBlackboard parent)
        {
            RuntimeBlackboard current = parent;
            int depth = 0;
            while (current != null)
            {
                if (ReferenceEquals(current, this))
                {
                    throw new InvalidOperationException("RuntimeBlackboard parent chain cannot contain a cycle.");
                }

                if (++depth > 1024)
                {
                    throw new InvalidOperationException("RuntimeBlackboard parent chain exceeds the safety limit (1024).");
                }

                current = current._parent;
            }
        }

        #region Open-Addressed Table
        private static int ComputeBucketCount(int required)
        {
            int buckets = 8;
            while (buckets - (buckets >> 2) < required)
            {
                buckets <<= 1;
            }
            return buckets;
        }

        private static int HashKey(int key)
        {
            uint x = (uint)key;
            x ^= x >> 16;
            x *= 0x7feb352du;
            x ^= x >> 15;
            x *= 0x846ca68bu;
            x ^= x >> 16;
            return (int)x;
        }

        // Linear probing over _buckets. A 0 bucket terminates the chain; tombstones and
        // non-matching live buckets are skipped. Returns the dense cell index or -1.
        private int FindCellIndex(int key)
        {
            int[] buckets = _buckets;
            int mask = buckets.Length - 1;
            int slot = HashKey(key) & mask;
            while (true)
            {
                int bucket = buckets[slot];
                if (bucket == 0) return -1;
                if (bucket > 0 && _cells[bucket - 1].Key == key) return bucket - 1;
                slot = (slot + 1) & mask;
            }
        }

        private void EnsureInsertCapacity()
        {
            int threshold = _buckets.Length - (_buckets.Length >> 2);
            if (_used + 1 <= threshold) return;
            int newSize = _count >= threshold / 2 ? _buckets.Length * 2 : _buckets.Length;
            Rebuild(newSize);
        }

        private void Rebuild(int bucketCount)
        {
            var newBuckets = new int[bucketCount];
            var newCells = new RuntimeBlackboardCell[bucketCount];
            Array.Copy(_cells, newCells, _count);
            int mask = bucketCount - 1;
            for (int i = 0; i < _count; i++)
            {
                int slot = HashKey(newCells[i].Key) & mask;
                while (newBuckets[slot] != 0) slot = (slot + 1) & mask;
                newBuckets[slot] = i + 1;
            }
            _buckets = newBuckets;
            _cells = newCells;
            _used = _count;
        }

        // Appends a fresh entry. Called only after EnsureInsertCapacity, so at least one
        // empty bucket exists. Reusing a tombstone keeps _used unchanged; consuming an
        // empty bucket increments it.
        private void InsertNewCell(RuntimeBlackboardCell entry)
        {
            int mask = _buckets.Length - 1;
            int slot = HashKey(entry.Key) & mask;
            int firstTombstone = -1;
            while (true)
            {
                int bucket = _buckets[slot];
                if (bucket == 0) break;
                if (bucket < 0 && firstTombstone < 0) firstTombstone = slot;
                slot = (slot + 1) & mask;
            }
            int target = firstTombstone >= 0 ? firstTombstone : slot;
            if (firstTombstone < 0) _used++;
            int cellIndex = _count;
            _cells[cellIndex] = entry;
            _count++;
            _buckets[target] = cellIndex + 1;
        }

        private void CommitCell(int existingIndex, RuntimeBlackboardCell entry)
        {
            if (existingIndex >= 0)
            {
                _cells[existingIndex] = entry;
                return;
            }
            EnsureInsertCapacity();
            InsertNewCell(entry);
        }

        private void CommitWireValue(RuntimeBlackboardCell entry)
        {
            int existingIndex = FindCellIndex(entry.Key);
            if (existingIndex >= 0)
            {
                _cells[existingIndex] = entry;
                return;
            }
            EnsureInsertCapacity();
            InsertNewCell(entry);
        }

        private bool HasObjectValue(int key)
        {
            int cellIndex = FindCellIndex(key);
            return cellIndex >= 0 && _cells[cellIndex].Type == TYPE_OBJECT;
        }

        // Tombstones the removed key's bucket and swap-removes the dense cell, repointing
        // the moved cell's bucket so lookups stay correct. _used is unchanged.
        private void RemoveCellAt(int key, int cellIndex)
        {
            int mask = _buckets.Length - 1;
            int slot = HashKey(key) & mask;
            while (_buckets[slot] != cellIndex + 1) slot = (slot + 1) & mask;
            _buckets[slot] = Tombstone;

            int last = _count - 1;
            if (cellIndex != last)
            {
                _cells[cellIndex] = _cells[last];
                int movedKey = _cells[cellIndex].Key;
                int movedSlot = HashKey(movedKey) & mask;
                while (_buckets[movedSlot] != last + 1) movedSlot = (movedSlot + 1) & mask;
                _buckets[movedSlot] = cellIndex + 1;
            }
            _cells[last] = default;
            _count--;
        }
        #endregion

        private bool SetIntCore(int key, int value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Int);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_INT && _cells[existing].IntValue == value) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_INT, Stamp = NextSequence(), IntValue = value });
            return true;
        }

        private bool SetFloatCore(int key, float value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Float);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_FLOAT && FloatBitsEqual(_cells[existing].FloatValue, value)) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_FLOAT, Stamp = NextSequence(), FloatValue = value });
            return true;
        }

        private bool SetBoolCore(int key, bool value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Bool);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_BOOL && _cells[existing].BoolValue == value) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_BOOL, Stamp = NextSequence(), BoolValue = value });
            return true;
        }

        private bool SetFloat3Core(int key, BTFloat3 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Vector3);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_VECTOR3 && VectorBitsEqual(_cells[existing].VectorValue, value)) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_VECTOR3, Stamp = NextSequence(), VectorValue = value });
            return true;
        }

        private bool SetLongCore(int key, long value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_LONG && _cells[existing].LongValue == value) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_LONG, Stamp = NextSequence(), LongValue = value });
            return true;
        }

        private bool SetLong2Core(int key, RuntimeBlackboardLong2 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long2);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_LONG2 && _cells[existing].Long2Value.Equals(value)) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_LONG2, Stamp = NextSequence(), Long2Value = value });
            return true;
        }

        private bool SetLong3Core(int key, RuntimeBlackboardLong3 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long3);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_LONG3 && _cells[existing].Long3Value.Equals(value)) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_LONG3, Stamp = NextSequence(), Long3Value = value });
            return true;
        }

        private bool SetObjectCore(int key, object value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Object);
            int existing = FindCellIndex(key);
            if (existing >= 0 && _cells[existing].Type == TYPE_OBJECT && ReferenceEquals(_cells[existing].ObjectValue, value)) return false;
            EnsureSequenceCapacity(1);
            CommitCell(existing, new RuntimeBlackboardCell {
                Key = key, Type = TYPE_OBJECT, Stamp = NextSequence(), ObjectValue = value });
            return true;
        }

        private void ValidateSchemaWrite(int key, RuntimeBlackboardValueType valueType)
        {
            _schema?.ValidateWrite(key, valueType);
        }

        private void ValidateExistingValuesAgainstSchema(RuntimeBlackboardSchema schema)
        {
            for (int i = 0; i < _count; i++)
            {
                schema.ValidateWrite(_cells[i].Key, ToValueType(_cells[i].Type));
            }
        }

        private void ValidateSerializedKeysAgainstSchema(
            Dictionary<int, byte> serializedTypes,
            RuntimeBlackboardNetworkScope scope)
        {
            if (_schema == null)
            {
                return;
            }

            foreach (KeyValuePair<int, byte> item in serializedTypes)
            {
                _schema.ValidateWrite(item.Key, ToValueType(item.Value));
                if (!ShouldIncludeKey(item.Key, scope))
                {
                    throw new InvalidDataException(
                        $"RuntimeBlackboard serialized key {item.Key} is not declared for {scope} sync.");
                }
            }
        }

        private void ApplySchemaDefaults()
        {
            RuntimeBlackboardSchema schema = _schema;
            if (schema == null)
            {
                return;
            }

            for (int i = 0; i < schema.Count; i++)
            {
                RuntimeBlackboardKeyDefinition entry = schema.GetEntry(i);
                if (!entry.HasDefaultValue || FindCellIndex(entry.KeyHash) >= 0)
                {
                    continue;
                }

                ApplySchemaDefault(entry);
            }
        }

        private void ApplySchemaDefault(RuntimeBlackboardKeyDefinition entry)
        {
            switch (entry.ValueType)
            {
                case RuntimeBlackboardValueType.Int:
                    SetIntCore(entry.KeyHash, entry.DefaultValue.IntValue);
                    break;
                case RuntimeBlackboardValueType.Float:
                    SetFloatCore(entry.KeyHash, entry.DefaultValue.FloatValue);
                    break;
                case RuntimeBlackboardValueType.Bool:
                    SetBoolCore(entry.KeyHash, entry.DefaultValue.BoolValue);
                    break;
                case RuntimeBlackboardValueType.Vector3:
                    SetFloat3Core(entry.KeyHash, entry.DefaultValue.Vector3Value);
                    break;
                case RuntimeBlackboardValueType.Long:
                    SetLongCore(entry.KeyHash, entry.DefaultValue.LongValue);
                    break;
                case RuntimeBlackboardValueType.Long2:
                    SetLong2Core(entry.KeyHash, entry.DefaultValue.Long2Value);
                    break;
                case RuntimeBlackboardValueType.Long3:
                    SetLong3Core(entry.KeyHash, entry.DefaultValue.Long3Value);
                    break;
                case RuntimeBlackboardValueType.Object:
                    SetObjectCore(entry.KeyHash, entry.DefaultValue.ObjectValue);
                    break;
            }
        }

        private bool MatchesSchemaDefault(RuntimeBlackboardKeyDefinition entry)
        {
            int cellIndex = FindCellIndex(entry.KeyHash);
            if (cellIndex < 0)
            {
                return false;
            }

            RuntimeBlackboardCell cell = _cells[cellIndex];
            switch (entry.ValueType)
            {
                case RuntimeBlackboardValueType.Int:
                    return cell.Type == TYPE_INT && cell.IntValue == entry.DefaultValue.IntValue;
                case RuntimeBlackboardValueType.Float:
                    return cell.Type == TYPE_FLOAT && FloatBitsEqual(cell.FloatValue, entry.DefaultValue.FloatValue);
                case RuntimeBlackboardValueType.Bool:
                    return cell.Type == TYPE_BOOL && cell.BoolValue == entry.DefaultValue.BoolValue;
                case RuntimeBlackboardValueType.Vector3:
                    return cell.Type == TYPE_VECTOR3 && VectorBitsEqual(cell.VectorValue, entry.DefaultValue.Vector3Value);
                case RuntimeBlackboardValueType.Object:
                    return cell.Type == TYPE_OBJECT && ReferenceEquals(cell.ObjectValue, entry.DefaultValue.ObjectValue);
                case RuntimeBlackboardValueType.Long:
                    return cell.Type == TYPE_LONG && cell.LongValue == entry.DefaultValue.LongValue;
                case RuntimeBlackboardValueType.Long2:
                    return cell.Type == TYPE_LONG2 && cell.Long2Value.Equals(entry.DefaultValue.Long2Value);
                case RuntimeBlackboardValueType.Long3:
                    return cell.Type == TYPE_LONG3 && cell.Long3Value.Equals(entry.DefaultValue.Long3Value);
                default:
                    return false;
            }
        }

        private void RemoveScopeValues(RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            for (int i = 0; i < _count; i++)
            {
                RuntimeBlackboardCell cell = _cells[i];
                if (cell.Type != TYPE_OBJECT && ShouldIncludeKey(cell.Key, scope))
                {
                    _sortedKeyScratch.Add(cell.Key);
                }
            }

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                int cellIndex = FindCellIndex(key);
                if (cellIndex >= 0)
                {
                    RemoveCellAt(key, cellIndex);
                }
            }
        }

        private static RuntimeBlackboardValueType ToValueType(byte type)
        {
            return type switch
            {
                TYPE_INT => RuntimeBlackboardValueType.Int,
                TYPE_FLOAT => RuntimeBlackboardValueType.Float,
                TYPE_BOOL => RuntimeBlackboardValueType.Bool,
                TYPE_VECTOR3 => RuntimeBlackboardValueType.Vector3,
                TYPE_OBJECT => RuntimeBlackboardValueType.Object,
                TYPE_LONG => RuntimeBlackboardValueType.Long,
                TYPE_LONG2 => RuntimeBlackboardValueType.Long2,
                TYPE_LONG3 => RuntimeBlackboardValueType.Long3,
                _ => throw new InvalidOperationException($"Unknown blackboard value slot type {type}.")
            };
        }

        private static BlackboardObserverCallback[] AddObserverCallback(
            BlackboardObserverCallback[] observers,
            BlackboardObserverCallback callback)
        {
            if (observers == null || observers.Length == 0)
            {
                return new[] { callback };
            }

            for (int i = 0; i < observers.Length; i++)
            {
                if (observers[i] == callback)
                {
                    return observers;
                }
            }

            var result = new BlackboardObserverCallback[observers.Length + 1];
            Array.Copy(observers, result, observers.Length);
            result[observers.Length] = callback;
            return result;
        }

        private static BlackboardObserverCallback[] RemoveObserverCallback(
            BlackboardObserverCallback[] observers,
            BlackboardObserverCallback callback)
        {
            if (observers == null || observers.Length == 0 || callback == null)
            {
                return observers;
            }

            int matchIndex = -1;
            for (int i = 0; i < observers.Length; i++)
            {
                if (observers[i] == callback)
                {
                    matchIndex = i;
                    break;
                }
            }

            if (matchIndex < 0)
            {
                return observers;
            }

            if (observers.Length == 1)
            {
                return Array.Empty<BlackboardObserverCallback>();
            }

            var result = new BlackboardObserverCallback[observers.Length - 1];
            if (matchIndex > 0)
            {
                Array.Copy(observers, 0, result, 0, matchIndex);
            }

            int tailCount = observers.Length - matchIndex - 1;
            if (tailCount > 0)
            {
                Array.Copy(observers, matchIndex + 1, result, matchIndex, tailCount);
            }

            return result;
        }

        private void FillSortedKeys(byte type, RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            for (int i = 0; i < _count; i++)
            {
                RuntimeBlackboardCell cell = _cells[i];
                if (cell.Type == type && ShouldIncludeKey(cell.Key, scope))
                {
                    _sortedKeyScratch.Add(cell.Key);
                }
            }

            _sortedKeyScratch.Sort();
        }

        private void FillSortedPrimitiveKeys(RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            for (int i = 0; i < _count; i++)
            {
                RuntimeBlackboardCell cell = _cells[i];
                if (cell.Type != TYPE_OBJECT && ShouldIncludeKey(cell.Key, scope))
                {
                    _sortedKeyScratch.Add(cell.Key);
                }
            }

            _sortedKeyScratch.Sort();
        }

        private static int ReadValidatedEntryCount(
            BinaryReader reader,
            string sectionName,
            RuntimeBlackboardSerializationLimits limits,
            ref int totalEntryCount,
            int minimumBytesPerEntry)
        {
            int count = reader.ReadInt32();
            if (count < 0)
            {
                throw new InvalidDataException($"RuntimeBlackboard {sectionName} entry count cannot be negative.");
            }

            if (count > limits.MaxEntriesPerType)
            {
                throw new InvalidDataException(
                    $"RuntimeBlackboard {sectionName} entry count {count} exceeds max entries per type {limits.MaxEntriesPerType}.");
            }

            if (count > limits.MaxTotalEntries - totalEntryCount)
            {
                throw new InvalidDataException(
                    $"RuntimeBlackboard serialized entry count exceeds max total entries {limits.MaxTotalEntries}.");
            }

            totalEntryCount += count;

            EnsureRemainingBytes(reader, count, minimumBytesPerEntry, sectionName);

            return count;
        }

        private static void EnsureRemainingBytes(
            BinaryReader reader,
            int entryCount,
            int minimumBytesPerEntry,
            string sectionName)
        {
            Stream stream = reader.BaseStream;
            if (!stream.CanSeek)
            {
                return;
            }

            long minimumBytes = checked((long)entryCount * minimumBytesPerEntry);
            long remainingBytes = stream.Length - stream.Position;
            if (remainingBytes < minimumBytes)
            {
                throw new EndOfStreamException(
                    $"RuntimeBlackboard {sectionName} section declares {entryCount} entries requiring at least {minimumBytes} bytes, but only {remainingBytes} bytes remain.");
            }
        }

        private static void AddSerializedPrimitiveKey(HashSet<int> allKeys, int key)
        {
            if (!allKeys.Add(key))
            {
                throw new InvalidDataException(
                    $"RuntimeBlackboard serialized primitive key {key} appears in more than one value slot.");
            }
        }

        private void WriteTypeSection(BinaryWriter writer, byte type, RuntimeBlackboardNetworkScope scope)
        {
            FillSortedKeys(type, scope);
            writer.Write(_sortedKeyScratch.Count);

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                RuntimeBlackboardCell cell = _cells[FindCellIndex(key)];
                writer.Write(key);
                switch (type)
                {
                    case TYPE_INT:
                        writer.Write(cell.IntValue);
                        break;
                    case TYPE_FLOAT:
                        writer.Write(cell.FloatValue);
                        break;
                    case TYPE_BOOL:
                        writer.Write(cell.BoolValue ? (byte)1 : (byte)0);
                        break;
                    case TYPE_VECTOR3:
                        writer.Write(cell.VectorValue.X);
                        writer.Write(cell.VectorValue.Y);
                        writer.Write(cell.VectorValue.Z);
                        break;
                    case TYPE_LONG:
                        writer.Write(cell.LongValue);
                        break;
                    case TYPE_LONG2:
                        writer.Write(cell.Long2Value.X);
                        writer.Write(cell.Long2Value.Y);
                        break;
                    case TYPE_LONG3:
                        writer.Write(cell.Long3Value.X);
                        writer.Write(cell.Long3Value.Y);
                        writer.Write(cell.Long3Value.Z);
                        break;
                }
            }
        }

        private void WritePrimitiveStampsOrdered(
            BinaryWriter writer,
            RuntimeBlackboardNetworkScope scope)
        {
            FillSortedPrimitiveKeys(scope);
            writer.Write(_sortedKeyScratch.Count);

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                writer.Write(key);
                writer.Write(_cells[FindCellIndex(key)].Stamp);
            }
        }

        private bool ShouldIncludeKey(int key, RuntimeBlackboardNetworkScope scope)
        {
            if (_schema == null)
            {
                return true;
            }

            return scope == RuntimeBlackboardNetworkScope.Snapshot
                ? _schema.UsesSnapshot(key)
                : _schema.IsNetworkedKey(key);
        }

        public ulong Revision
        {
            get
            {
                ThrowIfDisposed();
                if (_lock != null) _lock.EnterReadLock();
                try { return _sequenceId; }
                finally { if (_lock != null) _lock.ExitReadLock(); }
            }
        }

        internal bool TryCaptureNetworkMutation(
            int key,
            out RuntimeBlackboardMutation mutation,
            out ulong stamp)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                mutation = new RuntimeBlackboardMutation { Key = key };
                int cellIndex = FindCellIndex(key);
                if (cellIndex < 0)
                {
                    mutation.Kind = RuntimeBlackboardMutationKind.Remove;
                    stamp = 0;
                    return true;
                }

                RuntimeBlackboardCell cell = _cells[cellIndex];
                if (cell.Type == TYPE_OBJECT)
                {
                    stamp = cell.Stamp;
                    return false;
                }

                stamp = cell.Stamp;
                mutation.Kind = (RuntimeBlackboardMutationKind)cell.Type;
                switch (cell.Type)
                {
                    case TYPE_INT:
                        mutation.IntValue = cell.IntValue;
                        break;
                    case TYPE_FLOAT:
                        mutation.FloatValue = cell.FloatValue;
                        break;
                    case TYPE_BOOL:
                        mutation.BoolValue = cell.BoolValue;
                        break;
                    case TYPE_VECTOR3:
                        mutation.VectorValue = cell.VectorValue;
                        break;
                    case TYPE_LONG:
                        mutation.LongValue = cell.LongValue;
                        break;
                    case TYPE_LONG2:
                        mutation.Long2Value = cell.Long2Value;
                        break;
                    case TYPE_LONG3:
                        mutation.Long3Value = cell.Long3Value;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown blackboard value slot type {cell.Type}.");
                }

                return true;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        /// <summary>
        /// Captures one exact local value slot without following the parent chain.
        /// Object references are supported because this path never crosses a network boundary.
        /// </summary>
        internal bool TryCaptureLocalMutation(int key, out RuntimeBlackboardMutation mutation)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                mutation = new RuntimeBlackboardMutation { Key = key };
                int cellIndex = FindCellIndex(key);
                if (cellIndex < 0)
                {
                    return false;
                }

                RuntimeBlackboardCell cell = _cells[cellIndex];
                mutation.Kind = (RuntimeBlackboardMutationKind)cell.Type;
                switch (cell.Type)
                {
                    case TYPE_INT:
                        mutation.IntValue = cell.IntValue;
                        break;
                    case TYPE_FLOAT:
                        mutation.FloatValue = cell.FloatValue;
                        break;
                    case TYPE_BOOL:
                        mutation.BoolValue = cell.BoolValue;
                        break;
                    case TYPE_VECTOR3:
                        mutation.VectorValue = cell.VectorValue;
                        break;
                    case TYPE_OBJECT:
                        mutation.ObjectValue = cell.ObjectValue;
                        break;
                    case TYPE_LONG:
                        mutation.LongValue = cell.LongValue;
                        break;
                    case TYPE_LONG2:
                        mutation.Long2Value = cell.Long2Value;
                        break;
                    case TYPE_LONG3:
                        mutation.Long3Value = cell.Long3Value;
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown blackboard value slot type {cell.Type}.");
                }

                return true;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        internal bool TryCaptureMutation(
            int key,
            bool includeParentChain,
            out RuntimeBlackboardMutation mutation)
        {
            RuntimeBlackboard current = this;
            while (current != null)
            {
                if (current.TryCaptureLocalMutation(key, out mutation))
                {
                    return true;
                }

                current = includeParentChain ? current._parent : null;
            }

            mutation = new RuntimeBlackboardMutation
            {
                Key = key,
                Kind = RuntimeBlackboardMutationKind.Remove
            };
            return false;
        }

        /// <summary>
        /// Commits a validated network batch while holding one write lock. Revision mismatch
        /// aborts before mutation. Observers run after the commit and outside the storage lock;
        /// observer exceptions are propagated and never roll back the committed values.
        /// </summary>
        internal void ApplyNetworkBatch(
            RuntimeBlackboardMutation[] mutations,
            int count,
            ulong expectedRevision,
            bool requireRevisionMatch)
        {
            ApplyBatch(mutations, count, expectedRevision, requireRevisionMatch, allowObjects: false);
        }

        /// <summary>
        /// Commits a local batch atomically with respect to storage and schema validation.
        /// Observer callbacks run after all values are committed and outside the storage lock.
        /// </summary>
        internal void ApplyLocalBatch(RuntimeBlackboardMutation[] mutations, int count)
        {
            ApplyBatch(mutations, count, expectedRevision: 0, requireRevisionMatch: false, allowObjects: true);
        }

        private void ApplyBatch(
            RuntimeBlackboardMutation[] mutations,
            int count,
            ulong expectedRevision,
            bool requireRevisionMatch,
            bool allowObjects)
        {
            ThrowIfDisposed();
            if (mutations == null) throw new ArgumentNullException(nameof(mutations));
            if (count < 0 || count > mutations.Length) throw new ArgumentOutOfRangeException(nameof(count));

            int[] changedKeys = count > 0 ? ArrayPool<int>.Shared.Rent(count) : null;
            int changedCount = 0;
            int duplicateCapacity = count > 0 ? NextPowerOfTwo(count * 2) : 0;
            long[] seenKeys = count > 0 ? ArrayPool<long>.Shared.Rent(duplicateCapacity) : null;
            try
            {
                if (seenKeys != null)
                {
                    Array.Clear(seenKeys, 0, duplicateCapacity);
                }

                if (_lock != null) _lock.EnterWriteLock();
                try
                {
                    if (requireRevisionMatch && _sequenceId != expectedRevision)
                    {
                        throw new InvalidOperationException(
                            $"RuntimeBlackboard revision changed from {expectedRevision} to {_sequenceId} before network commit.");
                    }

                    for (int i = 0; i < count; i++)
                    {
                        RuntimeBlackboardMutation mutation = mutations[i];
                        // Duplicate detection is O(1) average. The token stores key<<32 | 1 so that
                        // zero means "empty" for every int key, including 0 and negative values.
                        long token = ((long)mutation.Key << 32) | 1L;
                        int mask = duplicateCapacity - 1;
                        int slot = HashKey(mutation.Key) & mask;
                        while (seenKeys[slot] != 0L)
                        {
                            if (seenKeys[slot] == token)
                            {
                                throw new InvalidDataException(
                                    $"RuntimeBlackboard batch contains duplicate destination key {mutation.Key}.");
                            }
                            slot = (slot + 1) & mask;
                        }
                        seenKeys[slot] = token;

                        if (mutation.Kind != RuntimeBlackboardMutationKind.Remove)
                        {
                            if (!allowObjects && mutation.Kind == RuntimeBlackboardMutationKind.Object)
                            {
                                throw new InvalidDataException(
                                    $"Network batch key {mutation.Key} cannot contain an object value.");
                            }

                            RuntimeBlackboardValueType valueType = ToValueType((byte)mutation.Kind);
                            ValidateSchemaWrite(mutation.Key, valueType);
                            if (!allowObjects && HasObjectValue(mutation.Key))
                            {
                                throw new InvalidDataException(
                                    $"Network primitive key {mutation.Key} conflicts with a local object value.");
                            }
                        }
                        else if (!allowObjects && HasObjectValue(mutation.Key))
                        {
                            throw new InvalidDataException(
                                $"Network removal key {mutation.Key} conflicts with a local object value.");
                        }

                        if (WouldMutationChange(mutation))
                        {
                            changedKeys[changedCount++] = mutation.Key;
                        }
                    }

                    EnsureSequenceCapacity(changedCount);
                    for (int i = 0; i < count; i++)
                    {
                        ApplyMutationCore(mutations[i]);
                    }
                }
                finally { if (_lock != null) _lock.ExitWriteLock(); }

                NotifyObserversAfterCommit(changedKeys, changedCount);
            }
            finally
            {
                if (seenKeys != null)
                {
                    ArrayPool<long>.Shared.Return(seenKeys, clearArray: false);
                }
                if (changedKeys != null)
                {
                    ArrayPool<int>.Shared.Return(changedKeys, clearArray: true);
                }
            }
        }

        private static int NextPowerOfTwo(int value)
        {
            int result = 1;
            while (result < value)
            {
                result <<= 1;
            }
            return result;
        }

        private bool WouldMutationChange(RuntimeBlackboardMutation mutation)
        {
            int cellIndex = FindCellIndex(mutation.Key);
            if (cellIndex < 0)
            {
                return mutation.Kind != RuntimeBlackboardMutationKind.Remove;
            }

            RuntimeBlackboardCell cell = _cells[cellIndex];
            if (mutation.Kind == RuntimeBlackboardMutationKind.Remove || cell.Type != (byte)mutation.Kind)
            {
                return true;
            }

            return mutation.Kind switch
            {
                RuntimeBlackboardMutationKind.Int => cell.IntValue != mutation.IntValue,
                RuntimeBlackboardMutationKind.Float => !FloatBitsEqual(cell.FloatValue, mutation.FloatValue),
                RuntimeBlackboardMutationKind.Bool => cell.BoolValue != mutation.BoolValue,
                RuntimeBlackboardMutationKind.Vector3 => !VectorBitsEqual(cell.VectorValue, mutation.VectorValue),
                RuntimeBlackboardMutationKind.Object => !ReferenceEquals(cell.ObjectValue, mutation.ObjectValue),
                RuntimeBlackboardMutationKind.Long => cell.LongValue != mutation.LongValue,
                RuntimeBlackboardMutationKind.Long2 => !cell.Long2Value.Equals(mutation.Long2Value),
                RuntimeBlackboardMutationKind.Long3 => !cell.Long3Value.Equals(mutation.Long3Value),
                _ => false
            };
        }

        private void ApplyMutationCore(RuntimeBlackboardMutation mutation)
        {
            switch (mutation.Kind)
            {
                case RuntimeBlackboardMutationKind.Int:
                    SetIntCore(mutation.Key, mutation.IntValue);
                    break;
                case RuntimeBlackboardMutationKind.Float:
                    SetFloatCore(mutation.Key, mutation.FloatValue);
                    break;
                case RuntimeBlackboardMutationKind.Bool:
                    SetBoolCore(mutation.Key, mutation.BoolValue);
                    break;
                case RuntimeBlackboardMutationKind.Vector3:
                    SetFloat3Core(mutation.Key, mutation.VectorValue);
                    break;
                case RuntimeBlackboardMutationKind.Object:
                    SetObjectCore(mutation.Key, mutation.ObjectValue);
                    break;
                case RuntimeBlackboardMutationKind.Long:
                    SetLongCore(mutation.Key, mutation.LongValue);
                    break;
                case RuntimeBlackboardMutationKind.Long2:
                    SetLong2Core(mutation.Key, mutation.Long2Value);
                    break;
                case RuntimeBlackboardMutationKind.Long3:
                    SetLong3Core(mutation.Key, mutation.Long3Value);
                    break;
                case RuntimeBlackboardMutationKind.Remove:
                    {
                        int cellIndex = FindCellIndex(mutation.Key);
                        if (cellIndex >= 0)
                        {
                            NextSequence();
                            RemoveCellAt(mutation.Key, cellIndex);
                        }
                        break;
                    }
                default:
                    throw new InvalidOperationException($"Unknown blackboard mutation kind {mutation.Kind}.");
            }
        }

        private int GetSerializedByteCountCore(RuntimeBlackboardNetworkScope scope)
        {
            long byteCount = 8L + (7L * sizeof(int)) + sizeof(int);
            int primitiveCount = 0;
            for (int i = 0; i < _count; i++)
            {
                RuntimeBlackboardCell cell = _cells[i];
                if (cell.Type == TYPE_OBJECT || !ShouldIncludeKey(cell.Key, scope)) continue;
                byteCount += SerializedEntrySize(cell.Type);
                primitiveCount++;
            }
            byteCount = checked(byteCount + primitiveCount * 12L);
            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("RuntimeBlackboard serialized payload exceeds Int32 capacity.");
            }

            return (int)byteCount;
        }

        private static long SerializedEntrySize(byte type)
        {
            switch (type)
            {
                case TYPE_INT: return 8L;
                case TYPE_FLOAT: return 8L;
                case TYPE_BOOL: return 5L;
                case TYPE_VECTOR3: return 16L;
                case TYPE_LONG: return 12L;
                case TYPE_LONG2: return 20L;
                case TYPE_LONG3: return 28L;
                default: return 0L;
            }
        }

        private static void ValidateNetworkScope(RuntimeBlackboardNetworkScope scope)
        {
            if (scope != RuntimeBlackboardNetworkScope.Snapshot &&
                scope != RuntimeBlackboardNetworkScope.Networked)
            {
                throw new ArgumentOutOfRangeException(nameof(scope));
            }
        }

        private ulong NextSequence()
        {
            if (_sequenceId == ulong.MaxValue)
            {
                throw new InvalidOperationException("RuntimeBlackboard mutation sequence is exhausted.");
            }

            return ++_sequenceId;
        }

        private void EnsureSequenceCapacity(int mutationCount)
        {
            if (mutationCount < 0 || (ulong)mutationCount > ulong.MaxValue - _sequenceId)
            {
                throw new InvalidOperationException("RuntimeBlackboard mutation sequence would overflow.");
            }
        }

        private void ClearStorageNoNotify()
        {
            Array.Clear(_buckets, 0, _buckets.Length);
            Array.Clear(_cells, 0, _count);
            _count = 0;
            _used = 0;
        }

        private void NotifyObserversAfterCommit(int[] keys, int count)
        {
            List<Exception> failures = null;
            for (int i = 0; i < count; i++)
            {
                try
                {
                    NotifyObservers(keys[i]);
                }
                catch (Exception exception)
                {
                    failures ??= new List<Exception>(1);
                    failures.Add(exception);
                }
            }

            if (failures != null)
            {
                throw new AggregateException(
                    "RuntimeBlackboard values were committed, but one or more post-commit observers failed.",
                    failures);
            }
        }

#if UNITY_EDITOR
        private static readonly Comparison<RuntimeBlackboardDebugEntry> DebugEntryComparison =
            CompareDebugEntries;

        /// <summary>
        /// Copies local values under the storage lock. The returned entries cannot mutate
        /// blackboard storage, and callers can reuse the destination list across repaints.
        /// </summary>
        public void CopyDebugEntries(List<RuntimeBlackboardDebugEntry> destination)
        {
            ThrowIfDisposed();
            if (destination == null) throw new ArgumentNullException(nameof(destination));

            if (_lock != null) _lock.EnterReadLock();
            try
            {
                destination.Clear();
                for (int i = 0; i < _count; i++)
                {
                    RuntimeBlackboardCell cell = _cells[i];
                    switch (cell.Type)
                    {
                        case TYPE_INT:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Int, intValue: cell.IntValue));
                            break;
                        case TYPE_FLOAT:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Float, floatValue: cell.FloatValue));
                            break;
                        case TYPE_BOOL:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Bool, boolValue: cell.BoolValue));
                            break;
                        case TYPE_VECTOR3:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Vector3, vectorValue: cell.VectorValue));
                            break;
                        case TYPE_OBJECT:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Object, objectValue: cell.ObjectValue));
                            break;
                        case TYPE_LONG:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Long, longValue: cell.LongValue));
                            break;
                        case TYPE_LONG2:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Long2, long2Value: cell.Long2Value));
                            break;
                        case TYPE_LONG3:
                            destination.Add(new RuntimeBlackboardDebugEntry(cell.Key, RuntimeBlackboardValueType.Long3, long3Value: cell.Long3Value));
                            break;
                    }
                }
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }

            destination.Sort(DebugEntryComparison);
        }

        private static int CompareDebugEntries(
            RuntimeBlackboardDebugEntry left,
            RuntimeBlackboardDebugEntry right)
        {
            int keyComparison = left.Key.CompareTo(right.Key);
            return keyComparison != 0
                ? keyComparison
                : left.ValueType.CompareTo(right.ValueType);
        }
#endif
    }
}
