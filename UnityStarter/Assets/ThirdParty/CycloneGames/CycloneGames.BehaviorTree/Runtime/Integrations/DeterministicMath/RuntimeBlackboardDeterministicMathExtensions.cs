using System;
using System.Runtime.CompilerServices;
using CycloneGames.BehaviorTree.Runtime.Core;
using CycloneGames.DeterministicMath;

namespace CycloneGames.BehaviorTree.Integrations.DeterministicMath
{
    public static class RuntimeBlackboardDeterministicMathExtensions
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFPInt64(this RuntimeBlackboard blackboard, int key, FPInt64 value)
        {
            EnsureBlackboard(blackboard);
            blackboard.SetLong(key, value.RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFPInt64(this RuntimeBlackboard blackboard, string key, FPInt64 value)
        {
            SetFPInt64(blackboard, Hash(blackboard, key), value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 GetFPInt64(this RuntimeBlackboard blackboard, int key)
        {
            return GetFPInt64(blackboard, key, default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 GetFPInt64(this RuntimeBlackboard blackboard, int key, FPInt64 defaultValue)
        {
            EnsureBlackboard(blackboard);
            return FPInt64.FromRaw(blackboard.GetLong(key, defaultValue.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 GetFPInt64(this RuntimeBlackboard blackboard, string key)
        {
            return GetFPInt64(blackboard, Hash(blackboard, key), default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPInt64 GetFPInt64(this RuntimeBlackboard blackboard, string key, FPInt64 defaultValue)
        {
            return GetFPInt64(blackboard, Hash(blackboard, key), defaultValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFPInt64(this RuntimeBlackboard blackboard, int key, out FPInt64 value)
        {
            EnsureBlackboard(blackboard);
            if (blackboard.TryGetLong(key, out long rawValue))
            {
                value = FPInt64.FromRaw(rawValue);
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFPInt64(this RuntimeBlackboard blackboard, string key, out FPInt64 value)
        {
            return TryGetFPInt64(blackboard, Hash(blackboard, key), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFPVector2(this RuntimeBlackboard blackboard, int key, FPVector2 value)
        {
            EnsureBlackboard(blackboard);
            blackboard.SetLong2(key, new RuntimeBlackboardLong2(value.X.RawValue, value.Y.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFPVector2(this RuntimeBlackboard blackboard, string key, FPVector2 value)
        {
            SetFPVector2(blackboard, Hash(blackboard, key), value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 GetFPVector2(this RuntimeBlackboard blackboard, int key)
        {
            return GetFPVector2(blackboard, key, default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 GetFPVector2(this RuntimeBlackboard blackboard, int key, FPVector2 defaultValue)
        {
            EnsureBlackboard(blackboard);
            RuntimeBlackboardLong2 raw = blackboard.GetLong2(
                key,
                new RuntimeBlackboardLong2(defaultValue.X.RawValue, defaultValue.Y.RawValue));
            return new FPVector2(FPInt64.FromRaw(raw.X), FPInt64.FromRaw(raw.Y));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 GetFPVector2(this RuntimeBlackboard blackboard, string key)
        {
            return GetFPVector2(blackboard, Hash(blackboard, key), default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector2 GetFPVector2(this RuntimeBlackboard blackboard, string key, FPVector2 defaultValue)
        {
            return GetFPVector2(blackboard, Hash(blackboard, key), defaultValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFPVector2(this RuntimeBlackboard blackboard, int key, out FPVector2 value)
        {
            EnsureBlackboard(blackboard);
            if (blackboard.TryGetLong2(key, out RuntimeBlackboardLong2 raw))
            {
                value = new FPVector2(FPInt64.FromRaw(raw.X), FPInt64.FromRaw(raw.Y));
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFPVector2(this RuntimeBlackboard blackboard, string key, out FPVector2 value)
        {
            return TryGetFPVector2(blackboard, Hash(blackboard, key), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFPVector3(this RuntimeBlackboard blackboard, int key, FPVector3 value)
        {
            EnsureBlackboard(blackboard);
            blackboard.SetLong3(key, new RuntimeBlackboardLong3(value.X.RawValue, value.Y.RawValue, value.Z.RawValue));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void SetFPVector3(this RuntimeBlackboard blackboard, string key, FPVector3 value)
        {
            SetFPVector3(blackboard, Hash(blackboard, key), value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 GetFPVector3(this RuntimeBlackboard blackboard, int key)
        {
            return GetFPVector3(blackboard, key, default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 GetFPVector3(this RuntimeBlackboard blackboard, int key, FPVector3 defaultValue)
        {
            EnsureBlackboard(blackboard);
            RuntimeBlackboardLong3 raw = blackboard.GetLong3(
                key,
                new RuntimeBlackboardLong3(defaultValue.X.RawValue, defaultValue.Y.RawValue, defaultValue.Z.RawValue));
            return new FPVector3(FPInt64.FromRaw(raw.X), FPInt64.FromRaw(raw.Y), FPInt64.FromRaw(raw.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 GetFPVector3(this RuntimeBlackboard blackboard, string key)
        {
            return GetFPVector3(blackboard, Hash(blackboard, key), default);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPVector3 GetFPVector3(this RuntimeBlackboard blackboard, string key, FPVector3 defaultValue)
        {
            return GetFPVector3(blackboard, Hash(blackboard, key), defaultValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFPVector3(this RuntimeBlackboard blackboard, int key, out FPVector3 value)
        {
            EnsureBlackboard(blackboard);
            if (blackboard.TryGetLong3(key, out RuntimeBlackboardLong3 raw))
            {
                value = new FPVector3(FPInt64.FromRaw(raw.X), FPInt64.FromRaw(raw.Y), FPInt64.FromRaw(raw.Z));
                return true;
            }

            value = default;
            return false;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool TryGetFPVector3(this RuntimeBlackboard blackboard, string key, out FPVector3 value)
        {
            return TryGetFPVector3(blackboard, Hash(blackboard, key), out value);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeBlackboardSchemaBuilder AddFPInt64(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            EnsureBuilder(builder);
            return builder.AddLong(key, syncFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeBlackboardSchemaBuilder AddFPInt64(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            FPInt64 defaultValue,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            EnsureBuilder(builder);
            return builder.AddLong(key, defaultValue.RawValue, syncFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeBlackboardSchemaBuilder AddFPVector2(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            EnsureBuilder(builder);
            return builder.AddLong2(key, syncFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeBlackboardSchemaBuilder AddFPVector2(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            FPVector2 defaultValue,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            EnsureBuilder(builder);
            return builder.AddLong2(
                key,
                new RuntimeBlackboardLong2(defaultValue.X.RawValue, defaultValue.Y.RawValue),
                syncFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeBlackboardSchemaBuilder AddFPVector3(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            EnsureBuilder(builder);
            return builder.AddLong3(key, syncFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static RuntimeBlackboardSchemaBuilder AddFPVector3(
            this RuntimeBlackboardSchemaBuilder builder,
            string key,
            FPVector3 defaultValue,
            RuntimeBlackboardSyncFlags syncFlags = RuntimeBlackboardSyncFlags.Networked)
        {
            EnsureBuilder(builder);
            return builder.AddLong3(
                key,
                new RuntimeBlackboardLong3(defaultValue.X.RawValue, defaultValue.Y.RawValue, defaultValue.Z.RawValue),
                syncFlags);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int Hash(RuntimeBlackboard blackboard, string key)
        {
            EnsureBlackboard(blackboard);
            return blackboard.StringHashFunc(key);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureBlackboard(RuntimeBlackboard blackboard)
        {
            if (blackboard == null)
            {
                throw new ArgumentNullException(nameof(blackboard));
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void EnsureBuilder(RuntimeBlackboardSchemaBuilder builder)
        {
            if (builder == null)
            {
                throw new ArgumentNullException(nameof(builder));
            }
        }
    }

    public sealed class DeterministicMathRandomProvider : IRuntimeBTRandomProvider
    {
        private DeterministicRandom _random;

        public DeterministicMathRandomProvider(ulong seed)
        {
            _random = DeterministicRandom.Create(seed);
        }

        public float Range(float minInclusive, float maxInclusive)
        {
            if (maxInclusive <= minInclusive)
            {
                return minInclusive;
            }

            FPInt64 min = FPInt64.FromFloatUnsafe(minInclusive);
            FPInt64 max = FPInt64.FromFloatUnsafe(maxInclusive);
            return _random.NextFP(min, max).ToFloat();
        }

        public int RangeInt(int minInclusive, int maxExclusive)
        {
            if (maxExclusive <= minInclusive)
            {
                return minInclusive;
            }

            return _random.NextInt(minInclusive, maxExclusive);
        }

        public DeterministicRandomState SaveState()
        {
            return _random.SaveState();
        }

        public void RestoreState(DeterministicRandomState state)
        {
            _random.RestoreState(state);
        }
    }
}
