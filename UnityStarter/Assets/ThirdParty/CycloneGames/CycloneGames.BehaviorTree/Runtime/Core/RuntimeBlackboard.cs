using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Threading;
using UnityEngine;

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
    public class RuntimeBlackboard : IDisposable
    {
        private readonly Dictionary<int, int> _intData;
        private readonly Dictionary<int, float> _floatData;
        private readonly Dictionary<int, bool> _boolData;
        private readonly Dictionary<int, Vector3> _vectorData;
        private readonly Dictionary<int, object> _objectData;

        // O(1) existence check across all typed dictionaries
        private readonly HashSet<int> _allKeys;

        // Monotonic sequence counter for change detection (per-blackboard)
        private ulong _sequenceId;
        private readonly Dictionary<int, ulong> _stamps;

        // Observer system: key-specific and global observers
        private Dictionary<int, List<BlackboardObserverCallback>> _keyObservers;
        private List<BlackboardObserverCallback> _globalObservers;

        // Thread-safety: null when single-threaded (default), allocated on demand
        private ReaderWriterLockSlim _lock;

        public RuntimeBlackboard Parent { get; set; }
        public IRuntimeBTContext Context { get; set; }

        public RuntimeBlackboard(RuntimeBlackboard parent = null, int initialCapacity = 8)
        {
            Parent = parent;
            _intData = new Dictionary<int, int>(initialCapacity);
            _floatData = new Dictionary<int, float>(initialCapacity);
            _boolData = new Dictionary<int, bool>(initialCapacity);
            _vectorData = new Dictionary<int, Vector3>(initialCapacity);
            _objectData = new Dictionary<int, object>(initialCapacity);
            _stamps = new Dictionary<int, ulong>(initialCapacity);
            _allKeys = new HashSet<int>(initialCapacity);
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
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _intData[key] = value;
                _allKeys.Add(key);
                _stamps[key] = ++_sequenceId;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
            NotifyObservers(key);
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
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _floatData[key] = value;
                _allKeys.Add(key);
                _stamps[key] = ++_sequenceId;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
            NotifyObservers(key);
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
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _boolData[key] = value;
                _allKeys.Add(key);
                _stamps[key] = ++_sequenceId;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
            NotifyObservers(key);
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
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _vectorData[key] = value;
                _allKeys.Add(key);
                _stamps[key] = ++_sequenceId;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
            NotifyObservers(key);
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

        public void SetObject(int key, object value)
        {
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _objectData[key] = value;
                _allKeys.Add(key);
                _stamps[key] = ++_sequenceId;
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
            NotifyObservers(key);
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
                if (_allKeys.Contains(key)) return true;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
            return Parent != null && Parent.HasKey(key);
        }

        public void Remove(int key)
        {
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _intData.Remove(key);
                _floatData.Remove(key);
                _boolData.Remove(key);
                _vectorData.Remove(key);
                _objectData.Remove(key);
                _allKeys.Remove(key);
                _stamps.Remove(key);
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
            NotifyObservers(key);
        }
        #endregion

        #region String-Key Convenience Methods
        public void SetInt(string key, int value) => SetInt(Animator.StringToHash(key), value);
        public int GetInt(string key, int defaultValue = 0) => GetInt(Animator.StringToHash(key), defaultValue);

        public void SetFloat(string key, float value) => SetFloat(Animator.StringToHash(key), value);
        public float GetFloat(string key, float defaultValue = 0f) => GetFloat(Animator.StringToHash(key), defaultValue);

        public void SetBool(string key, bool value) => SetBool(Animator.StringToHash(key), value);
        public bool GetBool(string key, bool defaultValue = false) => GetBool(Animator.StringToHash(key), defaultValue);

        public void SetVector3(string key, Vector3 value) => SetVector3(Animator.StringToHash(key), value);
        public Vector3 GetVector3(string key, Vector3 defaultValue = default) => GetVector3(Animator.StringToHash(key), defaultValue);

        public void SetObject(string key, object value) => SetObject(Animator.StringToHash(key), value);
        public T GetObject<T>(string key) => GetObject<T>(Animator.StringToHash(key));

        public bool HasKey(string key) => HasKey(Animator.StringToHash(key));
        public void Remove(string key) => Remove(Animator.StringToHash(key));
        public ulong GetStamp(string key) => GetStamp(Animator.StringToHash(key));
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
        public bool TryGetInt(string key, out int value) => TryGetInt(Animator.StringToHash(key), out value);
        public bool TryGetFloat(string key, out float value) => TryGetFloat(Animator.StringToHash(key), out value);
        public bool TryGetBool(string key, out bool value) => TryGetBool(Animator.StringToHash(key), out value);
        public bool TryGetVector3(string key, out Vector3 value) => TryGetVector3(Animator.StringToHash(key), out value);
        public bool TryGetObject<T>(string key, out T value) where T : class => TryGetObject(Animator.StringToHash(key), out value);
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
                _objectData.Clear();
                _allKeys.Clear();
                _stamps.Clear();
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        #region Serialization (Network Sync)
        /// <summary>
        /// Serialize all primitive blackboard data to a byte buffer for network transmission.
        /// Object references are skipped (not serializable across network boundary).
        /// </summary>
        public void WriteTo(System.IO.BinaryWriter writer)
        {
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                writer.Write(_sequenceId);

                writer.Write(_intData.Count);
                foreach (var kv in _intData) { writer.Write(kv.Key); writer.Write(kv.Value); }

                writer.Write(_floatData.Count);
                foreach (var kv in _floatData) { writer.Write(kv.Key); writer.Write(kv.Value); }

                writer.Write(_boolData.Count);
                foreach (var kv in _boolData) { writer.Write(kv.Key); writer.Write(kv.Value ? (byte)1 : (byte)0); }

                writer.Write(_vectorData.Count);
                foreach (var kv in _vectorData)
                {
                    writer.Write(kv.Key);
                    writer.Write(kv.Value.x); writer.Write(kv.Value.y); writer.Write(kv.Value.z);
                }

                writer.Write(_stamps.Count);
                foreach (var kv in _stamps) { writer.Write(kv.Key); writer.Write(kv.Value); }
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
        }

        /// <summary>
        /// Restore blackboard state from a serialized byte buffer.
        /// Typically called on client side after receiving server snapshot.
        /// </summary>
        public void ReadFrom(System.IO.BinaryReader reader)
        {
            if (_lock != null) _lock.EnterWriteLock();
            try
            {
                _sequenceId = reader.ReadUInt64();

                _intData.Clear();
                _allKeys.Clear();
                int intCount = reader.ReadInt32();
                for (int i = 0; i < intCount; i++)
                {
                    int key = reader.ReadInt32();
                    _intData[key] = reader.ReadInt32();
                    _allKeys.Add(key);
                }

                _floatData.Clear();
                int floatCount = reader.ReadInt32();
                for (int i = 0; i < floatCount; i++)
                {
                    int key = reader.ReadInt32();
                    _floatData[key] = reader.ReadSingle();
                    _allKeys.Add(key);
                }

                _boolData.Clear();
                int boolCount = reader.ReadInt32();
                for (int i = 0; i < boolCount; i++)
                {
                    int key = reader.ReadInt32();
                    _boolData[key] = reader.ReadByte() != 0;
                    _allKeys.Add(key);
                }

                _vectorData.Clear();
                int vecCount = reader.ReadInt32();
                for (int i = 0; i < vecCount; i++)
                {
                    int key = reader.ReadInt32();
                    _vectorData[key] = new Vector3(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
                    _allKeys.Add(key);
                }

                _stamps.Clear();
                int stampCount = reader.ReadInt32();
                for (int i = 0; i < stampCount; i++) _stamps[reader.ReadInt32()] = reader.ReadUInt64();
            }
            finally { if (_lock != null) _lock.ExitWriteLock(); }
        }

        /// <summary>
        /// FNV-1a hash of all primitive blackboard data for fast desync detection.
        /// Compare hashes between server and client — if mismatch, do full sync.
        /// </summary>
        public uint ComputeHash()
        {
            if (_lock != null) _lock.EnterReadLock();
            try
            {
                const uint FNV_OFFSET = 2166136261u;
                const uint FNV_PRIME = 16777619u;
                uint hash = FNV_OFFSET;

                foreach (var kv in _intData)
                {
                    hash = (hash ^ (uint)kv.Key) * FNV_PRIME;
                    hash = (hash ^ (uint)kv.Value) * FNV_PRIME;
                }
                foreach (var kv in _floatData)
                {
                    hash = (hash ^ (uint)kv.Key) * FNV_PRIME;
                    var u = new FloatUintUnion { FloatValue = kv.Value };
                    hash = (hash ^ u.UintValue) * FNV_PRIME;
                }
                foreach (var kv in _boolData)
                {
                    hash = (hash ^ (uint)kv.Key) * FNV_PRIME;
                    hash = (hash ^ (kv.Value ? 1u : 0u)) * FNV_PRIME;
                }
                foreach (var kv in _vectorData)
                {
                    hash = (hash ^ (uint)kv.Key) * FNV_PRIME;
                    var ux = new FloatUintUnion { FloatValue = kv.Value.x };
                    var uy = new FloatUintUnion { FloatValue = kv.Value.y };
                    var uz = new FloatUintUnion { FloatValue = kv.Value.z };
                    hash = (hash ^ ux.UintValue) * FNV_PRIME;
                    hash = (hash ^ uy.UintValue) * FNV_PRIME;
                    hash = (hash ^ uz.UintValue) * FNV_PRIME;
                }
                return hash;
            }
            finally { if (_lock != null) _lock.ExitReadLock(); }
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
            if (callback == null) return;
            _keyObservers ??= new Dictionary<int, List<BlackboardObserverCallback>>(4);
            if (!_keyObservers.TryGetValue(keyHash, out var list))
            {
                list = new List<BlackboardObserverCallback>(2);
                _keyObservers[keyHash] = list;
            }
            list.Add(callback);
        }

        /// <summary>
        /// Register an observer for a specific blackboard key using string name.
        /// </summary>
        public void AddObserver(string key, BlackboardObserverCallback callback)
        {
            AddObserver(Animator.StringToHash(key), callback);
        }

        /// <summary>
        /// Remove a specific observer for a key.
        /// </summary>
        public void RemoveObserver(int keyHash, BlackboardObserverCallback callback)
        {
            if (_keyObservers == null) return;
            if (_keyObservers.TryGetValue(keyHash, out var list))
            {
                list.Remove(callback);
                if (list.Count == 0) _keyObservers.Remove(keyHash);
            }
        }

        public void RemoveObserver(string key, BlackboardObserverCallback callback)
        {
            RemoveObserver(Animator.StringToHash(key), callback);
        }

        /// <summary>
        /// Register a global observer that fires on ANY key change.
        /// Useful for network sync, debug logging, or AI perception bridges.
        /// </summary>
        public void AddGlobalObserver(BlackboardObserverCallback callback)
        {
            if (callback == null) return;
            _globalObservers ??= new List<BlackboardObserverCallback>(2);
            _globalObservers.Add(callback);
        }

        public void RemoveGlobalObserver(BlackboardObserverCallback callback)
        {
            _globalObservers?.Remove(callback);
        }

        /// <summary>
        /// Remove all observers (key-specific and global).
        /// </summary>
        public void ClearAllObservers()
        {
            _keyObservers?.Clear();
            _globalObservers?.Clear();
        }

        private void NotifyObservers(int keyHash)
        {
            // Key-specific observers
            if (_keyObservers != null && _keyObservers.TryGetValue(keyHash, out var list))
            {
                for (int i = 0; i < list.Count; i++)
                {
                    list[i](keyHash, this);
                }
            }

            // Global observers
            if (_globalObservers != null)
            {
                for (int i = 0; i < _globalObservers.Count; i++)
                {
                    _globalObservers[i](keyHash, this);
                }
            }
        }
        #endregion

        #region IDisposable
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            _keyObservers?.Clear();
            _globalObservers?.Clear();
            _lock?.Dispose();
            _lock = null;
        }
        #endregion

#if UNITY_EDITOR
        public Dictionary<int, int> DebugIntData => _intData;
        public Dictionary<int, float> DebugFloatData => _floatData;
        public Dictionary<int, bool> DebugBoolData => _boolData;
        public Dictionary<int, Vector3> DebugVectorData => _vectorData;
        public Dictionary<int, object> DebugObjectData => _objectData;
#endif
    }
}
