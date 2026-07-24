using System;
using System.Buffers;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;
using CycloneGames.Hash.Core;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    /// <summary>
    /// Observer callback for blackboard key changes.
    /// keyHash: the changed key, bb: the blackboard instance.
    /// </summary>
    public delegate void BlackboardObserverCallback(int keyHash, RuntimeBlackboard bb);

    /// <summary>
    /// Typed runtime Blackboard with:
    /// - Separate typed dictionaries to avoid boxing for primitives
    /// - int-key (hash) addressing for 0GC string operations at runtime
    /// - Hierarchical parent chain for scoped blackboards (SubTree)
    /// - Stamped entries for change-detection without polling overhead
    /// - Optional concurrent storage access via ReaderWriterLockSlim
    /// - Observer system for push-based key change notifications
    /// - A single type index for average O(1) local key-existence checks
    /// </summary>
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
        public Vector3 VectorValue;
        public object ObjectValue;
        public long LongValue;
        public RuntimeBlackboardLong2 Long2Value;
        public RuntimeBlackboardLong3 Long3Value;
    }

#if UNITY_EDITOR
    /// <summary>
    /// Immutable editor-facing copy of one local blackboard value.
    /// </summary>
    public readonly struct RuntimeBlackboardDebugEntry
    {
        internal RuntimeBlackboardDebugEntry(
            int key,
            RuntimeBlackboardValueType valueType,
            int intValue = default,
            float floatValue = default,
            bool boolValue = default,
            Vector3 vectorValue = default,
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
        public Vector3 VectorValue { get; }
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

    public class RuntimeBlackboard : IDisposable
    {
        private readonly Dictionary<int, int> _intData;
        private readonly Dictionary<int, float> _floatData;
        private readonly Dictionary<int, bool> _boolData;
        private readonly Dictionary<int, Vector3> _vectorData;
        private readonly Dictionary<int, long> _longData;
        private readonly Dictionary<int, RuntimeBlackboardLong2> _long2Data;
        private readonly Dictionary<int, RuntimeBlackboardLong3> _long3Data;
        private readonly Dictionary<int, object> _objectData;

        private const byte TYPE_INT = 1;
        private const byte TYPE_FLOAT = 2;
        private const byte TYPE_BOOL = 3;
        private const byte TYPE_VECTOR3 = 4;
        private const byte TYPE_OBJECT = 5;
        private const byte TYPE_LONG = 6;
        private const byte TYPE_LONG2 = 7;
        private const byte TYPE_LONG3 = 8;

        // O(1) existence and current value-slot lookup.
        private readonly Dictionary<int, byte> _typeByKey;

        // Monotonic sequence counter for change detection (per-blackboard)
        private ulong _sequenceId;
        private readonly Dictionary<int, ulong> _stamps;
        private readonly List<int> _sortedKeyScratch;

        // Observer system: key-specific and global observers
        private readonly object _observerGate = new object();
        private Dictionary<int, BlackboardObserverCallback[]> _keyObservers;
        private BlackboardObserverCallback[] _globalObservers;

        // Thread-safety: null when single-threaded (default), allocated on demand
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

        /// <summary>
        /// Stable default hash function for RuntimeBlackboard and authoring compilation.
        /// Use StringHashFunc for an explicit per-instance legacy or project-specific contract.
        /// </summary>
        public static StringHashFunction DefaultStringHashFunc { get; } = BTHash.FNV1A;

        /// <summary>
        /// Per-instance hash override. Falls back to DefaultStringHashFunc when null.
        /// </summary>
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
            _intData = new Dictionary<int, int>(initialCapacity);
            _floatData = new Dictionary<int, float>(initialCapacity);
            _boolData = new Dictionary<int, bool>(initialCapacity);
            _vectorData = new Dictionary<int, Vector3>(initialCapacity);
            _longData = new Dictionary<int, long>(initialCapacity);
            _long2Data = new Dictionary<int, RuntimeBlackboardLong2>(initialCapacity);
            _long3Data = new Dictionary<int, RuntimeBlackboardLong3>(initialCapacity);
            _objectData = new Dictionary<int, object>(initialCapacity);
            _stamps = new Dictionary<int, ulong>(initialCapacity);
            _typeByKey = new Dictionary<int, byte>(initialCapacity);
            _sortedKeyScratch = new List<int>(initialCapacity);
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
        /// Protects typed value storage for concurrent reads and writes.
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

        #region Int-Key Methods (0GC)
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
                if (_intData.TryGetValue(key, out var val)) return val;
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
                if (_floatData.TryGetValue(key, out var val)) return val;
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
                if (_boolData.TryGetValue(key, out var val)) return val;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetBool(key, defaultValue) : defaultValue;
        }

        public void SetVector3(int key, Vector3 value)
        {
            ThrowIfDisposed();
            bool changed;
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                changed = SetVector3Core(key, value);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }

            if (changed)
            {
                NotifyObservers(key);
            }
        }

        public Vector3 GetVector3(int key, Vector3 defaultValue = default)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                if (_vectorData.TryGetValue(key, out var val)) return val;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetVector3(key, defaultValue) : defaultValue;
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
                if (_longData.TryGetValue(key, out long val)) return val;
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
                if (_long2Data.TryGetValue(key, out RuntimeBlackboardLong2 val)) return val;
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
                if (_long3Data.TryGetValue(key, out RuntimeBlackboardLong3 val)) return val;
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
                if (_objectData.TryGetValue(key, out var val) && val is T tVal) return tVal;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null ? Parent.GetObject<T>(key) : default;
        }

        /// <summary>
        /// Returns the stamp (sequence ID) for a given key, or 0 if not found.
        /// Use to detect whether a value has changed since last read.
        /// </summary>
        public ulong GetStamp(int key)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                if (_stamps.TryGetValue(key, out var stamp)) return stamp;
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
                if (_typeByKey.ContainsKey(key)) return true;
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
                if (!_typeByKey.TryGetValue(key, out byte oldType))
                {
                    changed = false;
                }
                else
                {
                    NextSequence();
                    RemoveTypedValue(key, oldType);
                    _typeByKey.Remove(key);
                    _stamps.Remove(key);
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

        public void SetVector3(string key, Vector3 value) => SetVector3(Hash(key), value);
        public Vector3 GetVector3(string key, Vector3 defaultValue = default) => GetVector3(Hash(key), defaultValue);

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

        #region TryGet Methods (precise type probing, 0GC)
        /// <summary>
        /// Try to get an int value from THIS blackboard only (no parent chain).
        /// Returns true if the key exists in the int dictionary.
        /// </summary>
        public bool TryGetInt(int key, out int value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _intData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetFloat(int key, out float value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _floatData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetBool(int key, out bool value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _boolData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetVector3(int key, out Vector3 value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _vectorData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong(int key, out long value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _longData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong2(int key, out RuntimeBlackboardLong2 value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _long2Data.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong3(int key, out RuntimeBlackboardLong3 value)
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try { return _long3Data.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetObject<T>(int key, out T value) where T : class
        {
            ThrowIfDisposed();
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                if (_objectData.TryGetValue(key, out var obj) && obj is T tVal)
                {
                    value = tVal;
                    return true;
                }
                value = null;
                return false;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        // String-key overloads
        public bool TryGetInt(string key, out int value) => TryGetInt(Hash(key), out value);
        public bool TryGetFloat(string key, out float value) => TryGetFloat(Hash(key), out value);
        public bool TryGetBool(string key, out bool value) => TryGetBool(Hash(key), out value);
        public bool TryGetVector3(string key, out Vector3 value) => TryGetVector3(Hash(key), out value);
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
                removedCount = _typeByKey.Count;
                if (removedCount > 0)
                {
                    NextSequence();
                    removedKeys = ArrayPool<int>.Shared.Rent(removedCount);
                    int index = 0;
                    foreach (int key in _typeByKey.Keys)
                    {
                        removedKeys[index++] = key;
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
                    int candidateCapacity = checked(_typeByKey.Count + schemaCount);
                    _sortedKeyScratch.Clear();
                    foreach (int key in _typeByKey.Keys)
                    {
                        _sortedKeyScratch.Add(key);
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
                        bool hasExistingValue = _typeByKey.ContainsKey(key);
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

                        if (_typeByKey.TryGetValue(key, out byte oldType))
                        {
                            NextSequence();
                            RemoveTypedValue(key, oldType);
                            _typeByKey.Remove(key);
                            _stamps.Remove(key);
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
        /// <summary>
        /// Serialize all primitive blackboard data to a byte buffer for network transmission.
        /// Object references are skipped (not serializable across network boundary).
        /// </summary>
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
            // must be excluded even though the value dictionaries are not modified.
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

                WriteOrdered(_intData, writer, scope, static (w, value) => w.Write(value));
                WriteOrdered(_floatData, writer, scope, static (w, value) => w.Write(value));
                WriteOrdered(_boolData, writer, scope, static (w, value) => w.Write(value ? (byte)1 : (byte)0));
                WriteOrdered(_vectorData, writer, scope, static (w, value) =>
                {
                    w.Write(value.x);
                    w.Write(value.y);
                    w.Write(value.z);
                });
                WriteOrdered(_longData, writer, scope, static (w, value) => w.Write(value));
                WriteOrdered(_long2Data, writer, scope, static (w, value) =>
                {
                    w.Write(value.X);
                    w.Write(value.Y);
                });
                WriteOrdered(_long3Data, writer, scope, static (w, value) =>
                {
                    w.Write(value.X);
                    w.Write(value.Y);
                    w.Write(value.Z);
                });
                WritePrimitiveStampsOrdered(writer, scope);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// Restore blackboard state from a serialized byte buffer.
        /// Typically called on client side after receiving server snapshot.
        /// </summary>
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
            var vectorData = new Dictionary<int, Vector3>(vecCount);
            for (int i = 0; i < vecCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_VECTOR3;
                vectorData[key] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
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

                foreach (int key in allKeys)
                {
                    if (_typeByKey.TryGetValue(key, out byte existingType) && existingType == TYPE_OBJECT)
                    {
                        throw new InvalidDataException(
                            $"RuntimeBlackboard serialized primitive key {key} conflicts with a local object value.");
                    }
                }

                var changedKeySet = new HashSet<int>(allKeys);
                foreach (KeyValuePair<int, byte> item in _typeByKey)
                {
                    if (item.Value != TYPE_OBJECT && ShouldIncludeKey(item.Key, scope))
                    {
                        changedKeySet.Add(item.Key);
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
                {
                    _intData[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_INT;
                    _stamps[item.Key] = NextSequence();
                }

                foreach (KeyValuePair<int, float> item in floatData)
                {
                    _floatData[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_FLOAT;
                    _stamps[item.Key] = NextSequence();
                }

                foreach (KeyValuePair<int, bool> item in boolData)
                {
                    _boolData[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_BOOL;
                    _stamps[item.Key] = NextSequence();
                }

                foreach (KeyValuePair<int, Vector3> item in vectorData)
                {
                    _vectorData[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_VECTOR3;
                    _stamps[item.Key] = NextSequence();
                }

                foreach (KeyValuePair<int, long> item in longData)
                {
                    _longData[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_LONG;
                    _stamps[item.Key] = NextSequence();
                }

                foreach (KeyValuePair<int, RuntimeBlackboardLong2> item in long2Data)
                {
                    _long2Data[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_LONG2;
                    _stamps[item.Key] = NextSequence();
                }

                foreach (KeyValuePair<int, RuntimeBlackboardLong3> item in long3Data)
                {
                    _long3Data[item.Key] = item.Value;
                    _typeByKey[item.Key] = TYPE_LONG3;
                    _stamps[item.Key] = NextSequence();
                }
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

        /// <summary>
        /// FNV-1a hash of all primitive blackboard data for fast desync detection.
        /// Compare hashes between server and client — if mismatch, do full sync.
        /// </summary>
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

                FillSortedKeys(_intData, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ TYPE_INT) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = (hash ^ (uint)_intData[key]) * FNV_PRIME;
                }

                FillSortedKeys(_floatData, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ TYPE_FLOAT) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    var u = new FloatUintUnion { FloatValue = _floatData[key] };
                    hash = (hash ^ u.UintValue) * FNV_PRIME;
                }

                FillSortedKeys(_boolData, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ TYPE_BOOL) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = (hash ^ (_boolData[key] ? 1u : 0u)) * FNV_PRIME;
                }

                FillSortedKeys(_vectorData, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    Vector3 value = _vectorData[key];
                    hash = (hash ^ TYPE_VECTOR3) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    var ux = new FloatUintUnion { FloatValue = value.x };
                    var uy = new FloatUintUnion { FloatValue = value.y };
                    var uz = new FloatUintUnion { FloatValue = value.z };
                    hash = (hash ^ ux.UintValue) * FNV_PRIME;
                    hash = (hash ^ uy.UintValue) * FNV_PRIME;
                    hash = (hash ^ uz.UintValue) * FNV_PRIME;
                }

                FillSortedKeys(_longData, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ TYPE_LONG) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, _longData[key]);
                }

                FillSortedKeys(_long2Data, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardLong2 value = _long2Data[key];
                    hash = (hash ^ TYPE_LONG2) * FNV_PRIME;
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, value.X);
                    hash = MixInt64(hash, value.Y);
                }

                FillSortedKeys(_long3Data, scope);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardLong3 value = _long3Data[key];
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

        private static bool VectorBitsEqual(Vector3 left, Vector3 right)
        {
            return FloatBitsEqual(left.x, right.x) &&
                   FloatBitsEqual(left.y, right.y) &&
                   FloatBitsEqual(left.z, right.z);
        }
        #endregion

        #region Observer System
        /// <summary>
        /// Register an observer for a specific blackboard key.
        /// Callback fires immediately after the key is Set or Removed.
        /// Compatible with AIPerception and any external system.
        /// </summary>
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
                    return;
                }

                _keyObservers[keyHash] = AddObserverCallback(observers, callback);
            }
        }

        /// <summary>
        /// Register an observer for a specific blackboard key using string name.
        /// </summary>
        public void AddObserver(string key, BlackboardObserverCallback callback)
        {
            AddObserver(Hash(key), callback);
        }

        /// <summary>
        /// Remove a specific observer for a key.
        /// </summary>
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
                    observers = RemoveObserverCallback(observers, callback);
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

        /// <summary>
        /// Register a global observer that fires on ANY key change.
        /// Useful for network sync, debug logging, or AI perception bridges.
        /// </summary>
        public void AddGlobalObserver(BlackboardObserverCallback callback)
        {
            ThrowIfDisposed();
            if (callback == null)
            {
                return;
            }

            lock (_observerGate)
            {
                _globalObservers = AddObserverCallback(_globalObservers, callback);
            }
        }

        public void RemoveGlobalObserver(BlackboardObserverCallback callback)
        {
            ThrowIfDisposed();
            lock (_observerGate)
            {
                _globalObservers = RemoveObserverCallback(_globalObservers, callback);
            }
        }

        /// <summary>
        /// Remove all observers (key-specific and global).
        /// </summary>
        public void ClearAllObservers()
        {
            ThrowIfDisposed();
            lock (_observerGate)
            {
                _keyObservers?.Clear();
                _globalObservers = null;
            }
        }

        private void NotifyObservers(int keyHash)
        {
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

        private bool SetIntCore(int key, int value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Int);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_INT && _intData.TryGetValue(key, out int oldValue) && oldValue == value)
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_INT) RemoveTypedValue(key, oldType);
            _intData[key] = value;
            _typeByKey[key] = TYPE_INT;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetFloatCore(int key, float value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Float);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_FLOAT && _floatData.TryGetValue(key, out float oldValue) && FloatBitsEqual(oldValue, value))
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_FLOAT) RemoveTypedValue(key, oldType);
            _floatData[key] = value;
            _typeByKey[key] = TYPE_FLOAT;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetBoolCore(int key, bool value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Bool);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_BOOL && _boolData.TryGetValue(key, out bool oldValue) && oldValue == value)
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_BOOL) RemoveTypedValue(key, oldType);
            _boolData[key] = value;
            _typeByKey[key] = TYPE_BOOL;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetVector3Core(int key, Vector3 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Vector3);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_VECTOR3 && _vectorData.TryGetValue(key, out Vector3 oldValue) && VectorBitsEqual(oldValue, value))
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_VECTOR3) RemoveTypedValue(key, oldType);
            _vectorData[key] = value;
            _typeByKey[key] = TYPE_VECTOR3;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetLongCore(int key, long value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_LONG && _longData.TryGetValue(key, out long oldValue) && oldValue == value)
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_LONG) RemoveTypedValue(key, oldType);
            _longData[key] = value;
            _typeByKey[key] = TYPE_LONG;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetLong2Core(int key, RuntimeBlackboardLong2 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long2);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_LONG2 && _long2Data.TryGetValue(key, out RuntimeBlackboardLong2 oldValue) && oldValue.Equals(value))
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_LONG2) RemoveTypedValue(key, oldType);
            _long2Data[key] = value;
            _typeByKey[key] = TYPE_LONG2;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetLong3Core(int key, RuntimeBlackboardLong3 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long3);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_LONG3 && _long3Data.TryGetValue(key, out RuntimeBlackboardLong3 oldValue) && oldValue.Equals(value))
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_LONG3) RemoveTypedValue(key, oldType);
            _long3Data[key] = value;
            _typeByKey[key] = TYPE_LONG3;
            _stamps[key] = NextSequence();
            return true;
        }

        private bool SetObjectCore(int key, object value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Object);
            bool hasOldValue = _typeByKey.TryGetValue(key, out byte oldType);
            if (hasOldValue)
            {
                if (oldType == TYPE_OBJECT && _objectData.TryGetValue(key, out object oldValue) && ReferenceEquals(oldValue, value))
                {
                    return false;
                }
            }

            EnsureSequenceCapacity(1);
            if (hasOldValue && oldType != TYPE_OBJECT) RemoveTypedValue(key, oldType);
            _objectData[key] = value;
            _typeByKey[key] = TYPE_OBJECT;
            _stamps[key] = NextSequence();
            return true;
        }

        private void RemoveTypedValue(int key, byte type)
        {
            switch (type)
            {
                case TYPE_INT:
                    _intData.Remove(key);
                    break;
                case TYPE_FLOAT:
                    _floatData.Remove(key);
                    break;
                case TYPE_BOOL:
                    _boolData.Remove(key);
                    break;
                case TYPE_VECTOR3:
                    _vectorData.Remove(key);
                    break;
                case TYPE_LONG:
                    _longData.Remove(key);
                    break;
                case TYPE_LONG2:
                    _long2Data.Remove(key);
                    break;
                case TYPE_LONG3:
                    _long3Data.Remove(key);
                    break;
                case TYPE_OBJECT:
                    _objectData.Remove(key);
                    break;
            }
        }

        private void ValidateSchemaWrite(int key, RuntimeBlackboardValueType valueType)
        {
            _schema?.ValidateWrite(key, valueType);
        }

        private void ValidateExistingValuesAgainstSchema(RuntimeBlackboardSchema schema)
        {
            foreach (KeyValuePair<int, byte> item in _typeByKey)
            {
                schema.ValidateWrite(item.Key, ToValueType(item.Value));
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
                if (!entry.HasDefaultValue || _typeByKey.ContainsKey(entry.KeyHash))
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
                    SetVector3Core(entry.KeyHash, entry.DefaultValue.Vector3Value);
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
            if (!_typeByKey.TryGetValue(entry.KeyHash, out byte currentType))
            {
                return false;
            }

            switch (entry.ValueType)
            {
                case RuntimeBlackboardValueType.Int:
                    return currentType == TYPE_INT &&
                           _intData.TryGetValue(entry.KeyHash, out int intValue) &&
                           intValue == entry.DefaultValue.IntValue;
                case RuntimeBlackboardValueType.Float:
                    return currentType == TYPE_FLOAT &&
                           _floatData.TryGetValue(entry.KeyHash, out float floatValue) &&
                           FloatBitsEqual(floatValue, entry.DefaultValue.FloatValue);
                case RuntimeBlackboardValueType.Bool:
                    return currentType == TYPE_BOOL &&
                           _boolData.TryGetValue(entry.KeyHash, out bool boolValue) &&
                           boolValue == entry.DefaultValue.BoolValue;
                case RuntimeBlackboardValueType.Vector3:
                    return currentType == TYPE_VECTOR3 &&
                           _vectorData.TryGetValue(entry.KeyHash, out Vector3 vectorValue) &&
                           VectorBitsEqual(vectorValue, entry.DefaultValue.Vector3Value);
                case RuntimeBlackboardValueType.Object:
                    return currentType == TYPE_OBJECT &&
                           _objectData.TryGetValue(entry.KeyHash, out object objectValue) &&
                           ReferenceEquals(objectValue, entry.DefaultValue.ObjectValue);
                case RuntimeBlackboardValueType.Long:
                    return currentType == TYPE_LONG &&
                           _longData.TryGetValue(entry.KeyHash, out long longValue) &&
                           longValue == entry.DefaultValue.LongValue;
                case RuntimeBlackboardValueType.Long2:
                    return currentType == TYPE_LONG2 &&
                           _long2Data.TryGetValue(entry.KeyHash, out RuntimeBlackboardLong2 long2Value) &&
                           long2Value.Equals(entry.DefaultValue.Long2Value);
                case RuntimeBlackboardValueType.Long3:
                    return currentType == TYPE_LONG3 &&
                           _long3Data.TryGetValue(entry.KeyHash, out RuntimeBlackboardLong3 long3Value) &&
                           long3Value.Equals(entry.DefaultValue.Long3Value);
                default:
                    return false;
            }
        }

        private void RemoveScopeValues(RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            foreach (KeyValuePair<int, byte> item in _typeByKey)
            {
                if (item.Value != TYPE_OBJECT && ShouldIncludeKey(item.Key, scope))
                {
                    _sortedKeyScratch.Add(item.Key);
                }
            }

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                if (_typeByKey.TryGetValue(key, out byte type))
                {
                    RemoveTypedValue(key, type);
                    _typeByKey.Remove(key);
                    _stamps.Remove(key);
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

        private void FillSortedKeys<T>(Dictionary<int, T> source, RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            foreach (var kvp in source)
            {
                if (!ShouldIncludeKey(kvp.Key, scope))
                {
                    continue;
                }

                _sortedKeyScratch.Add(kvp.Key);
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

        private void WriteOrdered<T>(Dictionary<int, T> source, BinaryWriter writer,
            RuntimeBlackboardNetworkScope scope,
            Action<BinaryWriter, T> writeValue)
        {
            FillNetworkScopeKeys(source, scope);
            writer.Write(_sortedKeyScratch.Count);

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                writer.Write(key);
                writeValue(writer, source[key]);
            }
        }

        private void FillNetworkScopeKeys<T>(
            Dictionary<int, T> source,
            RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            foreach (KeyValuePair<int, T> item in source)
            {
                if (ShouldIncludeKey(item.Key, scope))
                {
                    _sortedKeyScratch.Add(item.Key);
                }
            }

            _sortedKeyScratch.Sort();
        }

        private void WritePrimitiveStampsOrdered(
            BinaryWriter writer,
            RuntimeBlackboardNetworkScope scope)
        {
            _sortedKeyScratch.Clear();
            foreach (KeyValuePair<int, byte> item in _typeByKey)
            {
                if (item.Value != TYPE_OBJECT && ShouldIncludeKey(item.Key, scope))
                {
                    _sortedKeyScratch.Add(item.Key);
                }
            }

            _sortedKeyScratch.Sort();
            writer.Write(_sortedKeyScratch.Count);

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                if (!_stamps.TryGetValue(key, out ulong stamp))
                {
                    throw new InvalidOperationException($"RuntimeBlackboard key {key} is missing a change stamp.");
                }

                writer.Write(key);
                writer.Write(stamp);
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
                if (!_typeByKey.TryGetValue(key, out byte type))
                {
                    mutation.Kind = RuntimeBlackboardMutationKind.Remove;
                    stamp = 0;
                    return true;
                }

                if (type == TYPE_OBJECT)
                {
                    stamp = _stamps.TryGetValue(key, out ulong objectStamp) ? objectStamp : 0;
                    return false;
                }

                stamp = _stamps.TryGetValue(key, out ulong valueStamp) ? valueStamp : 0;
                mutation.Kind = (RuntimeBlackboardMutationKind)type;
                switch (type)
                {
                    case TYPE_INT:
                        mutation.IntValue = _intData[key];
                        break;
                    case TYPE_FLOAT:
                        mutation.FloatValue = _floatData[key];
                        break;
                    case TYPE_BOOL:
                        mutation.BoolValue = _boolData[key];
                        break;
                    case TYPE_VECTOR3:
                        mutation.VectorValue = _vectorData[key];
                        break;
                    case TYPE_LONG:
                        mutation.LongValue = _longData[key];
                        break;
                    case TYPE_LONG2:
                        mutation.Long2Value = _long2Data[key];
                        break;
                    case TYPE_LONG3:
                        mutation.Long3Value = _long3Data[key];
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown blackboard value slot type {type}.");
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
                if (!_typeByKey.TryGetValue(key, out byte type))
                {
                    return false;
                }

                mutation.Kind = (RuntimeBlackboardMutationKind)type;
                switch (type)
                {
                    case TYPE_INT:
                        mutation.IntValue = _intData[key];
                        break;
                    case TYPE_FLOAT:
                        mutation.FloatValue = _floatData[key];
                        break;
                    case TYPE_BOOL:
                        mutation.BoolValue = _boolData[key];
                        break;
                    case TYPE_VECTOR3:
                        mutation.VectorValue = _vectorData[key];
                        break;
                    case TYPE_OBJECT:
                        mutation.ObjectValue = _objectData[key];
                        break;
                    case TYPE_LONG:
                        mutation.LongValue = _longData[key];
                        break;
                    case TYPE_LONG2:
                        mutation.Long2Value = _long2Data[key];
                        break;
                    case TYPE_LONG3:
                        mutation.Long3Value = _long3Data[key];
                        break;
                    default:
                        throw new InvalidOperationException($"Unknown blackboard value slot type {type}.");
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
            try
            {
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
                        for (int earlierIndex = 0; earlierIndex < i; earlierIndex++)
                        {
                            if (mutations[earlierIndex].Key == mutation.Key)
                            {
                                throw new InvalidDataException(
                                    $"RuntimeBlackboard batch contains duplicate destination key {mutation.Key}.");
                            }
                        }

                        if (mutation.Kind != RuntimeBlackboardMutationKind.Remove)
                        {
                            if (!allowObjects && mutation.Kind == RuntimeBlackboardMutationKind.Object)
                            {
                                throw new InvalidDataException(
                                    $"Network batch key {mutation.Key} cannot contain an object value.");
                            }

                            RuntimeBlackboardValueType valueType = ToValueType((byte)mutation.Kind);
                            ValidateSchemaWrite(mutation.Key, valueType);
                            if (!allowObjects &&
                                _typeByKey.TryGetValue(mutation.Key, out byte existingType) &&
                                existingType == TYPE_OBJECT)
                            {
                                throw new InvalidDataException(
                                    $"Network primitive key {mutation.Key} conflicts with a local object value.");
                            }
                        }
                        else if (!allowObjects &&
                                 _typeByKey.TryGetValue(mutation.Key, out byte existingType) &&
                                 existingType == TYPE_OBJECT)
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
                if (changedKeys != null)
                {
                    ArrayPool<int>.Shared.Return(changedKeys, clearArray: true);
                }
            }
        }

        private bool WouldMutationChange(RuntimeBlackboardMutation mutation)
        {
            if (!_typeByKey.TryGetValue(mutation.Key, out byte existingType))
            {
                return mutation.Kind != RuntimeBlackboardMutationKind.Remove;
            }

            if (mutation.Kind == RuntimeBlackboardMutationKind.Remove || existingType != (byte)mutation.Kind)
            {
                return true;
            }

            return mutation.Kind switch
            {
                RuntimeBlackboardMutationKind.Int => _intData[mutation.Key] != mutation.IntValue,
                RuntimeBlackboardMutationKind.Float => !FloatBitsEqual(_floatData[mutation.Key], mutation.FloatValue),
                RuntimeBlackboardMutationKind.Bool => _boolData[mutation.Key] != mutation.BoolValue,
                RuntimeBlackboardMutationKind.Vector3 => !VectorBitsEqual(_vectorData[mutation.Key], mutation.VectorValue),
                RuntimeBlackboardMutationKind.Object => !ReferenceEquals(_objectData[mutation.Key], mutation.ObjectValue),
                RuntimeBlackboardMutationKind.Long => _longData[mutation.Key] != mutation.LongValue,
                RuntimeBlackboardMutationKind.Long2 => !_long2Data[mutation.Key].Equals(mutation.Long2Value),
                RuntimeBlackboardMutationKind.Long3 => !_long3Data[mutation.Key].Equals(mutation.Long3Value),
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
                    SetVector3Core(mutation.Key, mutation.VectorValue);
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
                    if (_typeByKey.TryGetValue(mutation.Key, out byte type))
                    {
                        NextSequence();
                        RemoveTypedValue(mutation.Key, type);
                        _typeByKey.Remove(mutation.Key);
                        _stamps.Remove(mutation.Key);
                    }
                    break;
                default:
                    throw new InvalidOperationException($"Unknown blackboard mutation kind {mutation.Kind}.");
            }
        }

        private int GetSerializedByteCountCore(RuntimeBlackboardNetworkScope scope)
        {
            long byteCount = 8L + (7L * sizeof(int)) + sizeof(int);
            int primitiveCount = 0;
            byteCount += CountSerializedBytes(_intData, scope, 8, ref primitiveCount);
            byteCount += CountSerializedBytes(_floatData, scope, 8, ref primitiveCount);
            byteCount += CountSerializedBytes(_boolData, scope, 5, ref primitiveCount);
            byteCount += CountSerializedBytes(_vectorData, scope, 16, ref primitiveCount);
            byteCount += CountSerializedBytes(_longData, scope, 12, ref primitiveCount);
            byteCount += CountSerializedBytes(_long2Data, scope, 20, ref primitiveCount);
            byteCount += CountSerializedBytes(_long3Data, scope, 28, ref primitiveCount);
            byteCount += primitiveCount * 12L;
            if (byteCount > int.MaxValue)
            {
                throw new InvalidOperationException("RuntimeBlackboard serialized payload exceeds Int32 capacity.");
            }

            return (int)byteCount;
        }

        private long CountSerializedBytes<T>(
            Dictionary<int, T> source,
            RuntimeBlackboardNetworkScope scope,
            int bytesPerEntry,
            ref int primitiveCount)
        {
            int count = 0;
            foreach (int key in source.Keys)
            {
                if (ShouldIncludeKey(key, scope))
                {
                    count++;
                }
            }

            primitiveCount = checked(primitiveCount + count);
            return count * (long)bytesPerEntry;
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
            _intData.Clear();
            _floatData.Clear();
            _boolData.Clear();
            _vectorData.Clear();
            _longData.Clear();
            _long2Data.Clear();
            _long3Data.Clear();
            _objectData.Clear();
            _typeByKey.Clear();
            _stamps.Clear();
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
                foreach (KeyValuePair<int, int> item in _intData)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Int, intValue: item.Value));
                foreach (KeyValuePair<int, float> item in _floatData)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Float, floatValue: item.Value));
                foreach (KeyValuePair<int, bool> item in _boolData)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Bool, boolValue: item.Value));
                foreach (KeyValuePair<int, Vector3> item in _vectorData)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Vector3, vectorValue: item.Value));
                foreach (KeyValuePair<int, object> item in _objectData)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Object, objectValue: item.Value));
                foreach (KeyValuePair<int, long> item in _longData)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Long, longValue: item.Value));
                foreach (KeyValuePair<int, RuntimeBlackboardLong2> item in _long2Data)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Long2, long2Value: item.Value));
                foreach (KeyValuePair<int, RuntimeBlackboardLong3> item in _long3Data)
                    destination.Add(new RuntimeBlackboardDebugEntry(item.Key, RuntimeBlackboardValueType.Long3, long3Value: item.Value));
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
