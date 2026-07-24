using System;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime
{
    /// <summary>
    /// Serialized authoring data for one blackboard key. Runtime code consumes the immutable
    /// RuntimeBlackboardKeyDefinition produced by the behavior tree compiler.
    /// </summary>
    [Serializable]
    internal sealed class BehaviorTreeBlackboardKey
    {
        [SerializeField] private string _name = string.Empty;
        [SerializeField] private RuntimeBlackboardValueType _valueType = RuntimeBlackboardValueType.Int;
        [SerializeField] private RuntimeBlackboardSyncFlags _syncFlags = RuntimeBlackboardSyncFlags.LocalOnly;
        [SerializeField] private bool _hasDefaultValue;
        [SerializeField] private int _intDefaultValue;
        [SerializeField] private float _floatDefaultValue;
        [SerializeField] private bool _boolDefaultValue;
        [SerializeField] private Vector3 _vector3DefaultValue;
        [SerializeField] private long _longDefaultValue;
        [SerializeField] private long _long2X;
        [SerializeField] private long _long2Y;
        [SerializeField] private long _long3X;
        [SerializeField] private long _long3Y;
        [SerializeField] private long _long3Z;

        internal string Name => _name;
        internal RuntimeBlackboardValueType ValueType => _valueType;
        internal RuntimeBlackboardSyncFlags SyncFlags => _syncFlags;
        internal bool HasDefaultValue => _hasDefaultValue;
        internal int IntDefaultValue => _intDefaultValue;
        internal float FloatDefaultValue => _floatDefaultValue;
        internal bool BoolDefaultValue => _boolDefaultValue;
        internal Vector3 Vector3DefaultValue => _vector3DefaultValue;
        internal long LongDefaultValue => _longDefaultValue;
        internal RuntimeBlackboardLong2 Long2DefaultValue => new RuntimeBlackboardLong2(_long2X, _long2Y);
        internal RuntimeBlackboardLong3 Long3DefaultValue => new RuntimeBlackboardLong3(_long3X, _long3Y, _long3Z);
    }
}
