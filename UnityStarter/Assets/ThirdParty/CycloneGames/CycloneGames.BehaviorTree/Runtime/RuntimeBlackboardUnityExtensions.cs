using CycloneGames.BehaviorTree.Core;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Runtime
{
    /// <summary>
    /// Conversions between the engine-independent core representation and Unity types.
    /// All helpers are value-type only; none allocate.
    /// </summary>
    public static class BTFloat3UnityExtensions
    {
        public static BTFloat3 ToBTFloat3(this Vector3 value)
        {
            return new BTFloat3(value.x, value.y, value.z);
        }

        public static Vector3 ToVector3(this BTFloat3 value)
        {
            return new Vector3(value.X, value.Y, value.Z);
        }
    }

    /// <summary>
    /// Unity-facing conveniences over the engine-independent blackboard API.
    /// The core stores <see cref="BTFloat3"/>; these helpers convert to and from
    /// <see cref="Vector3"/> without allocating.
    /// </summary>
    public static class RuntimeBlackboardUnityExtensions
    {
        public static void SetVector3(this RuntimeBlackboard blackboard, int key, Vector3 value)
        {
            blackboard.SetFloat3(key, value.ToBTFloat3());
        }

        public static Vector3 GetVector3(this RuntimeBlackboard blackboard, int key, Vector3 defaultValue = default)
        {
            return blackboard.GetFloat3(key, defaultValue.ToBTFloat3()).ToVector3();
        }

        public static bool TryGetVector3(this RuntimeBlackboard blackboard, int key, out Vector3 value)
        {
            if (blackboard.TryGetFloat3(key, out BTFloat3 stored))
            {
                value = stored.ToVector3();
                return true;
            }

            value = default;
            return false;
        }

        public static void SetVector3(this RuntimeBlackboard blackboard, string key, Vector3 value)
        {
            blackboard.SetFloat3(key, value.ToBTFloat3());
        }

        public static Vector3 GetVector3(this RuntimeBlackboard blackboard, string key, Vector3 defaultValue = default)
        {
            return blackboard.GetVector3(blackboard.StringHashFunc(key), defaultValue);
        }

        public static bool TryGetVector3(this RuntimeBlackboard blackboard, string key, out Vector3 value)
        {
            return blackboard.TryGetVector3(blackboard.StringHashFunc(key), out value);
        }

        public static Vector3 GetVector3Value(this RuntimeBlackboardValue value)
        {
            return value.Vector3Value.ToVector3();
        }
    }

    /// <summary>
    /// <see cref="Vector3"/> overloads over the engine-independent schema builder, which stores
    /// <see cref="BTFloat3"/> slots.
    /// </summary>
    public static class RuntimeBlackboardSchemaBuilderUnityExtensions
    {
        public static RuntimeBlackboardSchemaBuilder AddVector3(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.SnapshotAndDelta)
        {
            return builder.AddFloat3(key, syncFlags);
        }

        public static RuntimeBlackboardSchemaBuilder AddVector3(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            Vector3 defaultValue,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.SnapshotAndDelta)
        {
            return builder.AddFloat3(key, defaultValue.ToBTFloat3(), syncFlags);
        }
    }
}
