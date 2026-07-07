using System;
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
    /// High-performance Blackboard with:
    /// - Separate typed dictionaries to avoid boxing for primitives
    /// - int-key (hash) addressing for 0GC string operations at runtime
    /// - Hierarchical parent chain for scoped blackboards (SubTree)
    /// - Stamped entries for change-detection without polling overhead
    /// - Optional thread-safe mode via ReaderWriterLockSlim
    /// - Observer system for push-based key change notifications
    /// - Unified HashSet for O(1) HasKey checks
    /// </summary>
    public delegate int StringHashFunction(string key);

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

        public RuntimeBlackboard Parent { get; set; }
        public IRuntimeBTContext Context { get; set; }
        public RuntimeBlackboardSchema Schema => _schema;

        private StringHashFunction _stringHashFunc;

        /// <summary>
        /// Default hash function for all RuntimeBlackboard instances.
        /// Set once at app startup to choose a hashing strategy:
        /// Animator.StringToHash (Unity default), BTHash.FNV1A (pure C#), or custom.
        /// Changing after trees are compiled will cause key mismatch.
        /// </summary>
        public static StringHashFunction DefaultStringHashFunc { get; set; } = Animator.StringToHash;

        /// <summary>
        /// Per-instance hash override. Falls back to DefaultStringHashFunc when null.
        /// </summary>
        public StringHashFunction StringHashFunc
        {
            get => _stringHashFunc ?? DefaultStringHashFunc;
            set => _stringHashFunc = value;
        }

        private int Hash(string key) => (_stringHashFunc ?? DefaultStringHashFunc)(key);

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
        /// Enables thread-safe read/write. Call once during setup if blackboard
        /// is shared across threads (e.g., async tasks writing back results).
        /// </summary>
        public void EnableThreadSafety()
        {
            _lock ??= new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        }

        public T GetContextOwner<T>() where T : class
        {
            if (Context != null)
            {
                var ownerFromContext = Context.GetOwner<T>();
                if (ownerFromContext != null) return ownerFromContext;
            }
            return Parent != null ? Parent.GetContextOwner<T>() : null;
        }

        public T GetService<T>() where T : class
        {
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
            if (_lock != null) _lock.EnterReadLock();
            try { return _intData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetFloat(int key, out float value)
        {
            if (_lock != null) _lock.EnterReadLock();
            try { return _floatData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetBool(int key, out bool value)
        {
            if (_lock != null) _lock.EnterReadLock();
            try { return _boolData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetVector3(int key, out Vector3 value)
        {
            if (_lock != null) _lock.EnterReadLock();
            try { return _vectorData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong(int key, out long value)
        {
            if (_lock != null) _lock.EnterReadLock();
            try { return _longData.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong2(int key, out RuntimeBlackboardLong2 value)
        {
            if (_lock != null) _lock.EnterReadLock();
            try { return _long2Data.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetLong3(int key, out RuntimeBlackboardLong3 value)
        {
            if (_lock != null) _lock.EnterReadLock();
            try { return _long3Data.TryGetValue(key, out value); }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        public bool TryGetObject<T>(int key, out T value) where T : class
        {
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
            if (_lock != null) _lock.EnterWriteLock();
            try
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
                _sequenceId = 0;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        #region Serialization (Network Sync)
        /// <summary>
        /// Serialize all primitive blackboard data to a byte buffer for network transmission.
        /// Object references are skipped (not serializable across network boundary).
        /// </summary>
        public void WriteTo(BinaryWriter writer)
        {
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                writer.Write(_sequenceId);

                WriteOrdered(_intData, writer, static (w, value) => w.Write(value));
                WriteOrdered(_floatData, writer, static (w, value) => w.Write(value));
                WriteOrdered(_boolData, writer, static (w, value) => w.Write(value ? (byte)1 : (byte)0));
                WriteOrdered(_vectorData, writer, static (w, value) =>
                {
                    w.Write(value.x);
                    w.Write(value.y);
                    w.Write(value.z);
                });
                WriteOrdered(_longData, writer, static (w, value) => w.Write(value));
                WriteOrdered(_long2Data, writer, static (w, value) =>
                {
                    w.Write(value.X);
                    w.Write(value.Y);
                });
                WriteOrdered(_long3Data, writer, static (w, value) =>
                {
                    w.Write(value.X);
                    w.Write(value.Y);
                    w.Write(value.Z);
                });
                WritePrimitiveStampsOrdered(writer);
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        /// <summary>
        /// Restore blackboard state from a serialized byte buffer.
        /// Typically called on client side after receiving server snapshot.
        /// </summary>
        public void ReadFrom(BinaryReader reader)
        {
            ReadFrom(reader, RuntimeBlackboardSerializationLimits.Default);
        }

        public void ReadFrom(BinaryReader reader, RuntimeBlackboardSerializationLimits limits)
        {
            ulong sequenceId = reader.ReadUInt64();
            int totalEntryCount = 0;

            int intCount = ReadValidatedEntryCount(reader, "int", limits, ref totalEntryCount);
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

            int floatCount = ReadValidatedEntryCount(reader, "float", limits, ref totalEntryCount);
            var floatData = new Dictionary<int, float>(floatCount);
            for (int i = 0; i < floatCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_FLOAT;
                floatData[key] = reader.ReadSingle();
            }

            int boolCount = ReadValidatedEntryCount(reader, "bool", limits, ref totalEntryCount);
            var boolData = new Dictionary<int, bool>(boolCount);
            for (int i = 0; i < boolCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_BOOL;
                boolData[key] = reader.ReadByte() != 0;
            }

            int vecCount = ReadValidatedEntryCount(reader, "vector3", limits, ref totalEntryCount);
            var vectorData = new Dictionary<int, Vector3>(vecCount);
            for (int i = 0; i < vecCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_VECTOR3;
                vectorData[key] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
            }

            int longCount = ReadValidatedEntryCount(reader, "long", limits, ref totalEntryCount);
            var longData = new Dictionary<int, long>(longCount);
            for (int i = 0; i < longCount; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_LONG;
                longData[key] = reader.ReadInt64();
            }

            int long2Count = ReadValidatedEntryCount(reader, "long2", limits, ref totalEntryCount);
            var long2Data = new Dictionary<int, RuntimeBlackboardLong2>(long2Count);
            for (int i = 0; i < long2Count; i++)
            {
                int key = reader.ReadInt32();
                AddSerializedPrimitiveKey(allKeys, key);
                typeByKey[key] = TYPE_LONG2;
                long2Data[key] = new RuntimeBlackboardLong2(reader.ReadInt64(), reader.ReadInt64());
            }

            int long3Count = ReadValidatedEntryCount(reader, "long3", limits, ref totalEntryCount);
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

                stamps[key] = reader.ReadUInt64();
            }

            if (stamps.Count != allKeys.Count)
            {
                throw new InvalidDataException("RuntimeBlackboard serialized stamps must match all serialized primitive keys.");
            }

            ValidateSerializedKeysAgainstSchema(typeByKey);

            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                if (_schema == null)
                {
                    _sequenceId = sequenceId;
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
                else
                {
                    _sequenceId = Math.Max(_sequenceId, sequenceId);
                    RemoveSnapshotValues();
                }

                foreach (KeyValuePair<int, int> item in intData)
                {
                    _intData[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, float> item in floatData)
                {
                    _floatData[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, bool> item in boolData)
                {
                    _boolData[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, Vector3> item in vectorData)
                {
                    _vectorData[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, long> item in longData)
                {
                    _longData[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, RuntimeBlackboardLong2> item in long2Data)
                {
                    _long2Data[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, RuntimeBlackboardLong3> item in long3Data)
                {
                    _long3Data[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, byte> item in typeByKey)
                {
                    _typeByKey[item.Key] = item.Value;
                }

                foreach (KeyValuePair<int, ulong> item in stamps)
                {
                    _stamps[item.Key] = item.Value;
                }
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// FNV-1a hash of all primitive blackboard data for fast desync detection.
        /// Compare hashes between server and client — if mismatch, do full sync.
        /// </summary>
        public ulong ComputeHash()
        {
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                const ulong FNV_OFFSET = Fnv1a64.OffsetBasis;
                const ulong FNV_PRIME = Fnv1a64.Prime;
                ulong hash = FNV_OFFSET;

                FillSortedKeys(_intData, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = (hash ^ (uint)_intData[key]) * FNV_PRIME;
                }

                FillSortedKeys(_floatData, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    var u = new FloatUintUnion { FloatValue = _floatData[key] };
                    hash = (hash ^ u.UintValue) * FNV_PRIME;
                }

                FillSortedKeys(_boolData, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = (hash ^ (_boolData[key] ? 1u : 0u)) * FNV_PRIME;
                }

                FillSortedKeys(_vectorData, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    Vector3 value = _vectorData[key];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    var ux = new FloatUintUnion { FloatValue = value.x };
                    var uy = new FloatUintUnion { FloatValue = value.y };
                    var uz = new FloatUintUnion { FloatValue = value.z };
                    hash = (hash ^ ux.UintValue) * FNV_PRIME;
                    hash = (hash ^ uy.UintValue) * FNV_PRIME;
                    hash = (hash ^ uz.UintValue) * FNV_PRIME;
                }

                FillSortedKeys(_longData, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, _longData[key]);
                }

                FillSortedKeys(_long2Data, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardLong2 value = _long2Data[key];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, value.X);
                    hash = MixInt64(hash, value.Y);
                }

                FillSortedKeys(_long3Data, networkedOnly: true);
                for (int i = 0; i < _sortedKeyScratch.Count; i++)
                {
                    int key = _sortedKeyScratch[i];
                    RuntimeBlackboardLong3 value = _long3Data[key];
                    hash = (hash ^ (uint)key) * FNV_PRIME;
                    hash = MixInt64(hash, value.X);
                    hash = MixInt64(hash, value.Y);
                    hash = MixInt64(hash, value.Z);
                }

                return hash;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
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
        #endregion

        #region Observer System
        /// <summary>
        /// Register an observer for a specific blackboard key.
        /// Callback fires immediately after the key is Set or Removed.
        /// Compatible with AIPerception and any external system.
        /// </summary>
        public void AddObserver(int keyHash, BlackboardObserverCallback callback)
        {
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

            if (keyObservers != null)
            {
                for (int i = 0; i < keyObservers.Length; i++)
                {
                    keyObservers[i](keyHash, this);
                }
            }

            if (globalObservers == null)
            {
                return;
            }

            for (int i = 0; i < globalObservers.Length; i++)
            {
                globalObservers[i](keyHash, this);
            }
        }
        #endregion

        #region IDisposable
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            Clear();
            lock (_observerGate)
            {
                _keyObservers?.Clear();
                _globalObservers = null;
            }
            _lock?.Dispose();
            _lock = null;
        }
        #endregion

        private bool SetIntCore(int key, int value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Int);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_INT && _intData.TryGetValue(key, out int oldValue) && oldValue == value)
                {
                    return false;
                }

                if (oldType != TYPE_INT)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _intData[key] = value;
            _typeByKey[key] = TYPE_INT;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetFloatCore(int key, float value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Float);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_FLOAT && _floatData.TryGetValue(key, out float oldValue) && oldValue.Equals(value))
                {
                    return false;
                }

                if (oldType != TYPE_FLOAT)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _floatData[key] = value;
            _typeByKey[key] = TYPE_FLOAT;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetBoolCore(int key, bool value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Bool);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_BOOL && _boolData.TryGetValue(key, out bool oldValue) && oldValue == value)
                {
                    return false;
                }

                if (oldType != TYPE_BOOL)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _boolData[key] = value;
            _typeByKey[key] = TYPE_BOOL;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetVector3Core(int key, Vector3 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Vector3);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_VECTOR3 && _vectorData.TryGetValue(key, out Vector3 oldValue) && oldValue.Equals(value))
                {
                    return false;
                }

                if (oldType != TYPE_VECTOR3)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _vectorData[key] = value;
            _typeByKey[key] = TYPE_VECTOR3;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetLongCore(int key, long value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_LONG && _longData.TryGetValue(key, out long oldValue) && oldValue == value)
                {
                    return false;
                }

                if (oldType != TYPE_LONG)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _longData[key] = value;
            _typeByKey[key] = TYPE_LONG;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetLong2Core(int key, RuntimeBlackboardLong2 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long2);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_LONG2 && _long2Data.TryGetValue(key, out RuntimeBlackboardLong2 oldValue) && oldValue.Equals(value))
                {
                    return false;
                }

                if (oldType != TYPE_LONG2)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _long2Data[key] = value;
            _typeByKey[key] = TYPE_LONG2;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetLong3Core(int key, RuntimeBlackboardLong3 value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Long3);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_LONG3 && _long3Data.TryGetValue(key, out RuntimeBlackboardLong3 oldValue) && oldValue.Equals(value))
                {
                    return false;
                }

                if (oldType != TYPE_LONG3)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _long3Data[key] = value;
            _typeByKey[key] = TYPE_LONG3;
            _stamps[key] = ++_sequenceId;
            return true;
        }

        private bool SetObjectCore(int key, object value)
        {
            ValidateSchemaWrite(key, RuntimeBlackboardValueType.Object);
            if (_typeByKey.TryGetValue(key, out byte oldType))
            {
                if (oldType == TYPE_OBJECT && _objectData.TryGetValue(key, out object oldValue) && ReferenceEquals(oldValue, value))
                {
                    return false;
                }

                if (oldType != TYPE_OBJECT)
                {
                    RemoveTypedValue(key, oldType);
                }
            }

            _objectData[key] = value;
            _typeByKey[key] = TYPE_OBJECT;
            _stamps[key] = ++_sequenceId;
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

        private void ValidateSerializedKeysAgainstSchema(Dictionary<int, byte> serializedTypes)
        {
            if (_schema == null)
            {
                return;
            }

            foreach (KeyValuePair<int, byte> item in serializedTypes)
            {
                _schema.ValidateWrite(item.Key, ToValueType(item.Value));
                if (!_schema.UsesSnapshot(item.Key))
                {
                    throw new InvalidDataException($"RuntimeBlackboard serialized key {item.Key} is not declared for snapshot sync.");
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

        private void RemoveSnapshotValues()
        {
            _sortedKeyScratch.Clear();
            foreach (KeyValuePair<int, byte> item in _typeByKey)
            {
                if (_schema != null && _schema.UsesSnapshot(item.Key))
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

        private void FillSortedKeys<T>(Dictionary<int, T> source, bool networkedOnly = false)
        {
            _sortedKeyScratch.Clear();
            foreach (var kvp in source)
            {
                if (networkedOnly && !ShouldHashKey(kvp.Key))
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
            ref int totalEntryCount)
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

            totalEntryCount += count;
            if (totalEntryCount > limits.MaxTotalEntries)
            {
                throw new InvalidDataException(
                    $"RuntimeBlackboard serialized entry count {totalEntryCount} exceeds max total entries {limits.MaxTotalEntries}.");
            }

            return count;
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
            Action<BinaryWriter, T> writeValue)
        {
            FillSnapshotKeys(source);
            writer.Write(_sortedKeyScratch.Count);

            for (int i = 0; i < _sortedKeyScratch.Count; i++)
            {
                int key = _sortedKeyScratch[i];
                writer.Write(key);
                writeValue(writer, source[key]);
            }
        }

        private void FillSnapshotKeys<T>(Dictionary<int, T> source)
        {
            _sortedKeyScratch.Clear();
            foreach (KeyValuePair<int, T> item in source)
            {
                if (ShouldSerializeSnapshotKey(item.Key))
                {
                    _sortedKeyScratch.Add(item.Key);
                }
            }

            _sortedKeyScratch.Sort();
        }

        private void WritePrimitiveStampsOrdered(BinaryWriter writer)
        {
            _sortedKeyScratch.Clear();
            foreach (KeyValuePair<int, byte> item in _typeByKey)
            {
                if (item.Value != TYPE_OBJECT && ShouldSerializeSnapshotKey(item.Key))
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

        private bool ShouldSerializeSnapshotKey(int key)
        {
            return _schema == null || _schema.UsesSnapshot(key);
        }

        private bool ShouldHashKey(int key)
        {
            return _schema == null || _schema.IsNetworkedKey(key);
        }

#if UNITY_EDITOR
        public Dictionary<int, int> DebugIntData => _intData;
        public Dictionary<int, float> DebugFloatData => _floatData;
        public Dictionary<int, bool> DebugBoolData => _boolData;
        public Dictionary<int, Vector3> DebugVectorData => _vectorData;
        public Dictionary<int, long> DebugLongData => _longData;
        public Dictionary<int, RuntimeBlackboardLong2> DebugLong2Data => _long2Data;
        public Dictionary<int, RuntimeBlackboardLong3> DebugLong3Data => _long3Data;
        public Dictionary<int, object> DebugObjectData => _objectData;
#endif
    }
}
