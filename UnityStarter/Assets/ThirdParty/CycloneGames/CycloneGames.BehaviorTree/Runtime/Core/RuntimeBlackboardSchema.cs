using System;
using System.Collections.Generic;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Core
{
    public enum RuntimeBlackboardValueType : byte
    {
        Int = 1,
        Float = 2,
        Bool = 3,
        Vector3 = 4,
        Object = 5,
        Long = 6,
        Long2 = 7,
        Long3 = 8
    }

    [Flags]
    public enum RuntimeBlackboardSyncFlags : byte
    {
        LocalOnly = 0,
        Snapshot = 1,
        Delta = 2,
        Networked = Snapshot | Delta
    }

    public readonly struct RuntimeBlackboardValue
    {
        private readonly int _intValue;
        private readonly float _floatValue;
        private readonly bool _boolValue;
        private readonly Vector3 _vector3Value;
        private readonly long _longValue;
        private readonly RuntimeBlackboardLong2 _long2Value;
        private readonly RuntimeBlackboardLong3 _long3Value;
        private readonly object _objectValue;

        private RuntimeBlackboardValue(
            RuntimeBlackboardValueType valueType,
            int intValue,
            float floatValue,
            bool boolValue,
            Vector3 vector3Value,
            long longValue,
            RuntimeBlackboardLong2 long2Value,
            RuntimeBlackboardLong3 long3Value,
            object objectValue)
        {
            ValueType = valueType;
            _intValue = intValue;
            _floatValue = floatValue;
            _boolValue = boolValue;
            _vector3Value = vector3Value;
            _longValue = longValue;
            _long2Value = long2Value;
            _long3Value = long3Value;
            _objectValue = objectValue;
        }

        public RuntimeBlackboardValueType ValueType { get; }
        public int IntValue => ValueType == RuntimeBlackboardValueType.Int ? _intValue : throw new InvalidOperationException("Blackboard value is not an int.");
        public float FloatValue => ValueType == RuntimeBlackboardValueType.Float ? _floatValue : throw new InvalidOperationException("Blackboard value is not a float.");
        public bool BoolValue => ValueType == RuntimeBlackboardValueType.Bool ? _boolValue : throw new InvalidOperationException("Blackboard value is not a bool.");
        public Vector3 Vector3Value => ValueType == RuntimeBlackboardValueType.Vector3 ? _vector3Value : throw new InvalidOperationException("Blackboard value is not a Vector3.");
        public long LongValue => ValueType == RuntimeBlackboardValueType.Long ? _longValue : throw new InvalidOperationException("Blackboard value is not a long.");
        public RuntimeBlackboardLong2 Long2Value => ValueType == RuntimeBlackboardValueType.Long2 ? _long2Value : throw new InvalidOperationException("Blackboard value is not a long2.");
        public RuntimeBlackboardLong3 Long3Value => ValueType == RuntimeBlackboardValueType.Long3 ? _long3Value : throw new InvalidOperationException("Blackboard value is not a long3.");
        public object ObjectValue => ValueType == RuntimeBlackboardValueType.Object ? _objectValue : throw new InvalidOperationException("Blackboard value is not an object.");

        public static RuntimeBlackboardValue Int(int value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Int, value, default, default, default, default, default, default, default);
        }

        public static RuntimeBlackboardValue Float(float value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Float, default, value, default, default, default, default, default, default);
        }

        public static RuntimeBlackboardValue Bool(bool value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Bool, default, default, value, default, default, default, default, default);
        }

        public static RuntimeBlackboardValue Vector3(Vector3 value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Vector3, default, default, default, value, default, default, default, default);
        }

        public static RuntimeBlackboardValue Long(long value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Long, default, default, default, default, value, default, default, default);
        }

        public static RuntimeBlackboardValue Long2(RuntimeBlackboardLong2 value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Long2, default, default, default, default, default, value, default, default);
        }

        public static RuntimeBlackboardValue Long3(RuntimeBlackboardLong3 value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Long3, default, default, default, default, default, default, value, default);
        }

        public static RuntimeBlackboardValue Object(object value)
        {
            return new RuntimeBlackboardValue(RuntimeBlackboardValueType.Object, default, default, default, default, default, default, default, value);
        }
    }

    public readonly struct RuntimeBlackboardLong2 : IEquatable<RuntimeBlackboardLong2>
    {
        public readonly long X;
        public readonly long Y;

        public RuntimeBlackboardLong2(long x, long y)
        {
            X = x;
            Y = y;
        }

        public bool Equals(RuntimeBlackboardLong2 other)
        {
            return X == other.X && Y == other.Y;
        }

        public override bool Equals(object obj)
        {
            return obj is RuntimeBlackboardLong2 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return (X.GetHashCode() * 397) ^ Y.GetHashCode();
            }
        }
    }

    public readonly struct RuntimeBlackboardLong3 : IEquatable<RuntimeBlackboardLong3>
    {
        public readonly long X;
        public readonly long Y;
        public readonly long Z;

        public RuntimeBlackboardLong3(long x, long y, long z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public bool Equals(RuntimeBlackboardLong3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj)
        {
            return obj is RuntimeBlackboardLong3 other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                return ((X.GetHashCode() * 397) ^ Y.GetHashCode()) * 397 ^ Z.GetHashCode();
            }
        }
    }

    public readonly struct RuntimeBlackboardKeyDefinition
    {
        public RuntimeBlackboardKeyDefinition(
            int keyHash,
            string name,
            RuntimeBlackboardValueType valueType,
            RuntimeBlackboardSyncFlags syncFlags,
            bool hasDefaultValue,
            RuntimeBlackboardValue defaultValue)
        {
            if (valueType == RuntimeBlackboardValueType.Object && syncFlags != RuntimeBlackboardSyncFlags.LocalOnly)
            {
                throw new ArgumentException("Object blackboard keys must be local-only.", nameof(syncFlags));
            }

            if (hasDefaultValue && defaultValue.ValueType != valueType)
            {
                throw new ArgumentException("Default value type must match the blackboard key type.", nameof(defaultValue));
            }

            KeyHash = keyHash;
            Name = name ?? string.Empty;
            ValueType = valueType;
            SyncFlags = syncFlags;
            HasDefaultValue = hasDefaultValue;
            DefaultValue = defaultValue;
        }

        public int KeyHash { get; }
        public string Name { get; }
        public RuntimeBlackboardValueType ValueType { get; }
        public RuntimeBlackboardSyncFlags SyncFlags { get; }
        public bool HasDefaultValue { get; }
        public RuntimeBlackboardValue DefaultValue { get; }
        public bool IsNetworked => SyncFlags != RuntimeBlackboardSyncFlags.LocalOnly;
        public bool UsesSnapshot => (SyncFlags & RuntimeBlackboardSyncFlags.Snapshot) != 0;
        public bool UsesDelta => (SyncFlags & RuntimeBlackboardSyncFlags.Delta) != 0;
    }

    public sealed class RuntimeBlackboardSchema
    {
        public const int DefaultContractVersion = 1;

        public static readonly RuntimeBlackboardSchema Empty =
            new RuntimeBlackboardSchema(
                Array.Empty<RuntimeBlackboardKeyDefinition>(),
                DefaultContractVersion);

        private readonly RuntimeBlackboardKeyDefinition[] _entries;
        private readonly Dictionary<int, int> _indexByHash;
        private readonly int[] _deltaKeys;

        public RuntimeBlackboardSchema(RuntimeBlackboardKeyDefinition[] entries)
            : this(entries, DefaultContractVersion)
        {
        }

        public RuntimeBlackboardSchema(
            RuntimeBlackboardKeyDefinition[] entries,
            int contractVersion)
        {
            if (contractVersion < 1)
            {
                throw new ArgumentOutOfRangeException(
                    nameof(contractVersion),
                    contractVersion,
                    "Blackboard contract version must be at least 1.");
            }

            ContractVersion = contractVersion;
            if (entries == null || entries.Length == 0)
            {
                _entries = Array.Empty<RuntimeBlackboardKeyDefinition>();
                _indexByHash = new Dictionary<int, int>(0);
                _deltaKeys = Array.Empty<int>();
                return;
            }

            _entries = new RuntimeBlackboardKeyDefinition[entries.Length];
            Array.Copy(entries, _entries, entries.Length);
            _indexByHash = new Dictionary<int, int>(_entries.Length);

            int deltaCount = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                RuntimeBlackboardKeyDefinition entry = _entries[i];
                if (_indexByHash.ContainsKey(entry.KeyHash))
                {
                    throw new ArgumentException($"Duplicate blackboard schema key hash {entry.KeyHash}.", nameof(entries));
                }

                if (entry.ValueType == RuntimeBlackboardValueType.Object && entry.SyncFlags != RuntimeBlackboardSyncFlags.LocalOnly)
                {
                    throw new ArgumentException($"Object blackboard key '{entry.Name}' must be local-only.", nameof(entries));
                }

                _indexByHash.Add(entry.KeyHash, i);
                if (entry.UsesDelta)
                {
                    deltaCount++;
                }
            }

            _deltaKeys = new int[deltaCount];
            int deltaIndex = 0;
            for (int i = 0; i < _entries.Length; i++)
            {
                if (_entries[i].UsesDelta)
                {
                    _deltaKeys[deltaIndex++] = _entries[i].KeyHash;
                }
            }
        }

        public int Count => _entries.Length;
        public int DeltaKeyCount => _deltaKeys.Length;
        public int ContractVersion { get; }

        public RuntimeBlackboardKeyDefinition GetEntry(int index)
        {
            return _entries[index];
        }

        public int GetDeltaKey(int index)
        {
            return _deltaKeys[index];
        }

        public bool TryGetDefinition(int keyHash, out RuntimeBlackboardKeyDefinition definition)
        {
            if (_indexByHash.TryGetValue(keyHash, out int index))
            {
                definition = _entries[index];
                return true;
            }

            definition = default;
            return false;
        }

        public bool IsKnownKey(int keyHash)
        {
            return _indexByHash.ContainsKey(keyHash);
        }

        public bool IsNetworkedKey(int keyHash)
        {
            return TryGetDefinition(keyHash, out RuntimeBlackboardKeyDefinition definition) && definition.IsNetworked;
        }

        public bool UsesSnapshot(int keyHash)
        {
            return TryGetDefinition(keyHash, out RuntimeBlackboardKeyDefinition definition) && definition.UsesSnapshot;
        }

        public bool UsesDelta(int keyHash)
        {
            return TryGetDefinition(keyHash, out RuntimeBlackboardKeyDefinition definition) && definition.UsesDelta;
        }

        public void ValidateWrite(int keyHash, RuntimeBlackboardValueType valueType)
        {
            if (!TryGetDefinition(keyHash, out RuntimeBlackboardKeyDefinition definition))
            {
                throw new KeyNotFoundException($"Blackboard key {keyHash} is not declared in the runtime schema.");
            }

            if (definition.ValueType != valueType)
            {
                throw new InvalidOperationException(
                    $"Blackboard key '{definition.Name}' ({keyHash}) expects {definition.ValueType}, but received {valueType}.");
            }
        }
    }

    public sealed class RuntimeBlackboardSchemaBuilder
    {
        private readonly List<RuntimeBlackboardKeyDefinition> _entries = new List<RuntimeBlackboardKeyDefinition>(16);
        private readonly HashSet<int> _keys = new HashSet<int>();
        private readonly StringHashFunction _hashFunction;

        public RuntimeBlackboardSchemaBuilder(StringHashFunction hashFunction = null)
        {
            _hashFunction = hashFunction ?? RuntimeBlackboard.DefaultStringHashFunc;
        }

        public RuntimeBlackboardSchemaBuilder AddInt(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Int, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddInt(string key, int defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Int, syncFlags, true, RuntimeBlackboardValue.Int(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddFloat(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Float, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddFloat(string key, float defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Float, syncFlags, true, RuntimeBlackboardValue.Float(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddBool(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Bool, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddBool(string key, bool defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Bool, syncFlags, true, RuntimeBlackboardValue.Bool(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddVector3(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Vector3, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddVector3(string key, Vector3 defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Vector3, syncFlags, true, RuntimeBlackboardValue.Vector3(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddLong(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Long, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddLong(string key, long defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Long, syncFlags, true, RuntimeBlackboardValue.Long(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddLong2(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Long2, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddLong2(string key, RuntimeBlackboardLong2 defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Long2, syncFlags, true, RuntimeBlackboardValue.Long2(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddLong3(string key, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Long3, syncFlags, false, default);
        }

        public RuntimeBlackboardSchemaBuilder AddLong3(string key, RuntimeBlackboardLong3 defaultValue, RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            return Add(key, RuntimeBlackboardValueType.Long3, syncFlags, true, RuntimeBlackboardValue.Long3(defaultValue));
        }

        public RuntimeBlackboardSchemaBuilder AddObject(string key, object defaultValue = null)
        {
            return Add(key, RuntimeBlackboardValueType.Object, RuntimeBlackboardSyncFlags.LocalOnly, defaultValue != null, RuntimeBlackboardValue.Object(defaultValue));
        }

        public RuntimeBlackboardSchema Build()
        {
            return new RuntimeBlackboardSchema(_entries.ToArray());
        }

        private RuntimeBlackboardSchemaBuilder Add(
            string key,
            RuntimeBlackboardValueType valueType,
            RuntimeBlackboardSyncFlags syncFlags,
            bool hasDefaultValue,
            RuntimeBlackboardValue defaultValue)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                throw new ArgumentException("Blackboard schema key cannot be null or whitespace.", nameof(key));
            }

            int keyHash = _hashFunction(key);
            if (!_keys.Add(keyHash))
            {
                throw new InvalidOperationException($"Duplicate blackboard schema key '{key}' with hash {keyHash}.");
            }

            _entries.Add(new RuntimeBlackboardKeyDefinition(keyHash, key, valueType, syncFlags, hasDefaultValue, defaultValue));
            return this;
        }
    }
}
