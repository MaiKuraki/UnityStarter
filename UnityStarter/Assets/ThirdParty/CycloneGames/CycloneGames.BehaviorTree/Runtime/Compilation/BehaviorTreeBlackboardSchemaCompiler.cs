using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using CycloneGames.BehaviorTree.Runtime.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime.Compilation
{
    /// <summary>
    /// Converts bounded Unity authoring data into the immutable runtime schema. This is a compile
    /// path and is never called from behavior-tree Tick execution.
    /// </summary>
    internal static class BehaviorTreeBlackboardSchemaCompiler
    {
        internal const int MaxKeyCount = RuntimeBlackboardSerializationLimits.DEFAULT_MAX_ENTRIES_PER_TYPE;
        internal const int MaxKeyNameLength = 256;

        private static readonly Comparison<RuntimeBlackboardKeyDefinition> DefinitionComparison =
            CompareDefinitions;

        internal static bool TryCompile(
            int formatVersion,
            int contractVersion,
            IReadOnlyList<BehaviorTreeBlackboardKey> keys,
            out RuntimeBlackboardSchema schema,
            out string error)
        {
            schema = null;
            error = null;

            if (formatVersion != BehaviorTree.CurrentBlackboardSchemaFormatVersion)
            {
                error =
                    $"Blackboard schema format version {formatVersion} is unsupported; expected " +
                    $"{BehaviorTree.CurrentBlackboardSchemaFormatVersion}.";
                return false;
            }

            if (contractVersion < 1)
            {
                error = "Blackboard contract version must be at least 1.";
                return false;
            }

            if (keys == null)
            {
                error = "Blackboard schema key list is missing.";
                return false;
            }

            int count = keys.Count;
            if (count > MaxKeyCount)
            {
                error = $"Blackboard schema contains {count} keys; the hard limit is {MaxKeyCount}.";
                return false;
            }

            if (count == 0)
            {
                schema = contractVersion == RuntimeBlackboardSchema.DefaultContractVersion
                    ? RuntimeBlackboardSchema.Empty
                    : new RuntimeBlackboardSchema(
                        Array.Empty<RuntimeBlackboardKeyDefinition>(),
                        contractVersion);
                return true;
            }

            var definitions = new RuntimeBlackboardKeyDefinition[count];
            var indexByHash = new Dictionary<int, int>(count);
            for (int i = 0; i < count; i++)
            {
                BehaviorTreeBlackboardKey key = keys[i];
                if (key == null)
                {
                    error = $"Blackboard schema key[{i}] is null.";
                    return false;
                }

                string name = key.Name;
                if (string.IsNullOrWhiteSpace(name))
                {
                    error = $"Blackboard schema key[{i}] requires a non-whitespace name.";
                    return false;
                }

                if (name.Length > MaxKeyNameLength)
                {
                    error =
                        $"Blackboard schema key[{i}] name contains {name.Length} UTF-16 code units; " +
                        $"the hard limit is {MaxKeyNameLength}.";
                    return false;
                }

                if (char.IsWhiteSpace(name[0]) || char.IsWhiteSpace(name[name.Length - 1]))
                {
                    error = $"Blackboard schema key[{i}] '{name}' cannot start or end with whitespace.";
                    return false;
                }

                RuntimeBlackboardValueType valueType = key.ValueType;
                if ((uint)((int)valueType - (int)RuntimeBlackboardValueType.Int) >
                    (uint)((int)RuntimeBlackboardValueType.Long3 - (int)RuntimeBlackboardValueType.Int))
                {
                    error = $"Blackboard schema key[{i}] '{name}' has unsupported value type {(int)valueType}.";
                    return false;
                }

                RuntimeBlackboardSyncFlags syncFlags = key.SyncFlags;
                if ((((byte)syncFlags) & ~(byte)RuntimeBlackboardSyncFlags.Networked) != 0)
                {
                    error = $"Blackboard schema key[{i}] '{name}' has unsupported sync flags {(byte)syncFlags}.";
                    return false;
                }

                if (valueType == RuntimeBlackboardValueType.Object)
                {
                    if (syncFlags != RuntimeBlackboardSyncFlags.LocalOnly)
                    {
                        error = $"Blackboard schema Object key '{name}' must be LocalOnly.";
                        return false;
                    }

                    if (key.HasDefaultValue)
                    {
                        error =
                            $"Blackboard schema Object key '{name}' cannot own an authoring default; " +
                            "inject instance objects through BTRunnerComponent Initial Objects or code-first setup.";
                        return false;
                    }
                }

                if (key.HasDefaultValue && !ValidateFiniteDefault(key, out error))
                {
                    error = $"Blackboard schema key[{i}] '{name}': {error}";
                    return false;
                }

                int keyHash = RuntimeBlackboard.DefaultStringHashFunc(name);
                if (keyHash == 0)
                {
                    error = $"Blackboard schema key[{i}] '{name}' hashes to the reserved zero value.";
                    return false;
                }

                if (indexByHash.TryGetValue(keyHash, out int earlierIndex))
                {
                    string earlierName = keys[earlierIndex].Name;
                    error = string.Equals(earlierName, name, StringComparison.Ordinal)
                        ? $"Blackboard schema contains duplicate key '{name}' at indexes {earlierIndex} and {i}."
                        : $"Blackboard schema keys '{earlierName}' and '{name}' collide at hash {keyHash}.";
                    return false;
                }

                indexByHash.Add(keyHash, i);
                RuntimeBlackboardValue defaultValue = key.HasDefaultValue
                    ? CreateDefaultValue(key)
                    : default;
                definitions[i] = new RuntimeBlackboardKeyDefinition(
                    keyHash,
                    name,
                    valueType,
                    syncFlags,
                    key.HasDefaultValue,
                    defaultValue);
            }

            Array.Sort(definitions, DefinitionComparison);
            schema = new RuntimeBlackboardSchema(definitions, contractVersion);
            return true;
        }

        internal static bool IsExactSubset(
            RuntimeBlackboardSchema child,
            RuntimeBlackboardSchema root,
            out string error)
        {
            error = null;
            if (child == null || root == null)
            {
                error = "Strict subtree compatibility requires both schemas.";
                return false;
            }

            for (int i = 0; i < child.Count; i++)
            {
                RuntimeBlackboardKeyDefinition childEntry = child.GetEntry(i);
                if (!root.TryGetDefinition(childEntry.KeyHash, out RuntimeBlackboardKeyDefinition rootEntry))
                {
                    error = $"Subtree key '{childEntry.Name}' is not declared by the root schema.";
                    return false;
                }

                if (!DefinitionsEqual(childEntry, rootEntry))
                {
                    error =
                        $"Subtree key '{childEntry.Name}' does not exactly match the root definition " +
                        "(name, type, sync flags, and default must agree).";
                    return false;
                }
            }

            return true;
        }

        private static bool ValidateFiniteDefault(
            BehaviorTreeBlackboardKey key,
            out string error)
        {
            error = null;
            if (key.ValueType == RuntimeBlackboardValueType.Float && !IsFinite(key.FloatDefaultValue))
            {
                error = "Float default must be finite.";
                return false;
            }

            if (key.ValueType == RuntimeBlackboardValueType.Vector3)
            {
                Vector3 value = key.Vector3DefaultValue;
                if (!IsFinite(value.x) || !IsFinite(value.y) || !IsFinite(value.z))
                {
                    error = "Vector3 default components must be finite.";
                    return false;
                }
            }

            return true;
        }

        private static RuntimeBlackboardValue CreateDefaultValue(BehaviorTreeBlackboardKey key)
        {
            switch (key.ValueType)
            {
                case RuntimeBlackboardValueType.Int:
                    return RuntimeBlackboardValue.Int(key.IntDefaultValue);
                case RuntimeBlackboardValueType.Float:
                    return RuntimeBlackboardValue.Float(key.FloatDefaultValue);
                case RuntimeBlackboardValueType.Bool:
                    return RuntimeBlackboardValue.Bool(key.BoolDefaultValue);
                case RuntimeBlackboardValueType.Vector3:
                    return RuntimeBlackboardValue.Vector3(key.Vector3DefaultValue);
                case RuntimeBlackboardValueType.Long:
                    return RuntimeBlackboardValue.Long(key.LongDefaultValue);
                case RuntimeBlackboardValueType.Long2:
                    return RuntimeBlackboardValue.Long2(key.Long2DefaultValue);
                case RuntimeBlackboardValueType.Long3:
                    return RuntimeBlackboardValue.Long3(key.Long3DefaultValue);
                default:
                    throw new InvalidOperationException(
                        $"Value type {key.ValueType} cannot provide an authoring default.");
            }
        }

        private static bool DefinitionsEqual(
            RuntimeBlackboardKeyDefinition left,
            RuntimeBlackboardKeyDefinition right)
        {
            if (left.KeyHash != right.KeyHash ||
                !string.Equals(left.Name, right.Name, StringComparison.Ordinal) ||
                left.ValueType != right.ValueType ||
                left.SyncFlags != right.SyncFlags ||
                left.HasDefaultValue != right.HasDefaultValue)
            {
                return false;
            }

            if (!left.HasDefaultValue)
            {
                return true;
            }

            switch (left.ValueType)
            {
                case RuntimeBlackboardValueType.Int:
                    return left.DefaultValue.IntValue == right.DefaultValue.IntValue;
                case RuntimeBlackboardValueType.Float:
                    return FloatBitsEqual(left.DefaultValue.FloatValue, right.DefaultValue.FloatValue);
                case RuntimeBlackboardValueType.Bool:
                    return left.DefaultValue.BoolValue == right.DefaultValue.BoolValue;
                case RuntimeBlackboardValueType.Vector3:
                    Vector3 leftVector = left.DefaultValue.Vector3Value;
                    Vector3 rightVector = right.DefaultValue.Vector3Value;
                    return FloatBitsEqual(leftVector.x, rightVector.x) &&
                           FloatBitsEqual(leftVector.y, rightVector.y) &&
                           FloatBitsEqual(leftVector.z, rightVector.z);
                case RuntimeBlackboardValueType.Object:
                    return ReferenceEquals(left.DefaultValue.ObjectValue, right.DefaultValue.ObjectValue);
                case RuntimeBlackboardValueType.Long:
                    return left.DefaultValue.LongValue == right.DefaultValue.LongValue;
                case RuntimeBlackboardValueType.Long2:
                    return left.DefaultValue.Long2Value.Equals(right.DefaultValue.Long2Value);
                case RuntimeBlackboardValueType.Long3:
                    return left.DefaultValue.Long3Value.Equals(right.DefaultValue.Long3Value);
                default:
                    return false;
            }
        }

        private static int CompareDefinitions(
            RuntimeBlackboardKeyDefinition left,
            RuntimeBlackboardKeyDefinition right)
        {
            int hashComparison = left.KeyHash.CompareTo(right.KeyHash);
            return hashComparison != 0
                ? hashComparison
                : string.CompareOrdinal(left.Name, right.Name);
        }

        private static bool IsFinite(float value)
        {
            return !float.IsNaN(value) && !float.IsInfinity(value);
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
    }
}
