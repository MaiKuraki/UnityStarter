using System;

namespace CycloneGames.Factory.Runtime
{
    public enum PoolOverflowPolicy
    {
        Throw = 0,
        ReturnNull = 1,
    }

    public enum PoolTrimPolicy
    {
        Manual = 0,
        TrimOnDespawn = 1,
    }

    public enum PoolLifecycleState
    {
        Ready = 0,
        Disposing = 1,
        Disposed = 2,
    }

    public readonly struct PoolCapacitySettings : IEquatable<PoolCapacitySettings>
    {
        /// <summary>
        /// Inactive retention target used by <see cref="PoolTrimPolicy.TrimOnDespawn"/>, and the sizing
        /// hint for tracking storage. Not a creation budget.
        /// </summary>
        public int SoftCapacity { get; }

        /// <summary>
        /// Maximum items owned by the pool. <c>-1</c> means unbounded.
        /// </summary>
        public int HardCapacity { get; }
        public PoolOverflowPolicy OverflowPolicy { get; }
        public PoolTrimPolicy TrimPolicy { get; }

        /// <summary>
        /// Items created during pool construction. <c>-1</c> inherits <see cref="SoftCapacity"/>.
        /// </summary>
        public int InitialPrewarm { get; }

        /// <summary>Effective construction prewarm count.</summary>
        public int PrewarmCount => InitialPrewarm >= 0 ? InitialPrewarm : SoftCapacity;

        public PoolCapacitySettings(
            int softCapacity = 0,
            int hardCapacity = -1,
            PoolOverflowPolicy overflowPolicy = PoolOverflowPolicy.Throw,
            PoolTrimPolicy trimPolicy = PoolTrimPolicy.Manual,
            int initialPrewarm = -1)
        {
            if (softCapacity < 0)
            {
                throw new ArgumentOutOfRangeException(nameof(softCapacity));
            }

            if (hardCapacity == 0 || hardCapacity < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(hardCapacity));
            }

            if (hardCapacity > 0 && softCapacity > hardCapacity)
            {
                throw new ArgumentException("Soft capacity cannot exceed hard capacity.");
            }

            if (initialPrewarm < -1)
            {
                throw new ArgumentOutOfRangeException(nameof(initialPrewarm));
            }

            if (hardCapacity > 0 && initialPrewarm > hardCapacity)
            {
                throw new ArgumentException("Initial prewarm cannot exceed hard capacity.");
            }

            SoftCapacity = softCapacity;
            HardCapacity = hardCapacity;
            OverflowPolicy = overflowPolicy;
            TrimPolicy = trimPolicy;
            InitialPrewarm = initialPrewarm;
        }

        public bool Equals(PoolCapacitySettings other)
        {
            return SoftCapacity == other.SoftCapacity
                && HardCapacity == other.HardCapacity
                && OverflowPolicy == other.OverflowPolicy
                && TrimPolicy == other.TrimPolicy
                && InitialPrewarm == other.InitialPrewarm;
        }

        public override bool Equals(object obj)
        {
            return obj is PoolCapacitySettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                uint hash = (uint)SoftCapacity;
                hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
                hash ^= (uint)HardCapacity * 0x9e3779b9;
                hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
                hash ^= (uint)OverflowPolicy * 0x27d4eb2d;
                hash ^= (uint)TrimPolicy * 0x9e3779b9;
                hash = ((hash >> 16) ^ hash) * 0x45d9f3b;
                hash ^= (uint)InitialPrewarm * 0x27d4eb2d;
                hash = (hash >> 16) ^ hash;
                return (int)hash;
            }
        }
    }

    public readonly struct PoolDiagnostics
    {
        public int PeakCountActive { get; }
        public int PeakCountAll { get; }
        public long TotalCreated { get; }
        public long TotalSpawned { get; }
        public long TotalDespawned { get; }
        public long FailedSpawnRollbacks { get; }
        public long RejectedSpawns { get; }
        public long InvalidDespawns { get; }
        public long TotalDestroyed { get; }
        public long CallbackFailures { get; }
        public long QuarantinedItems { get; }
        public long InvalidatedInactiveItems { get; }

        /// <summary>Leases that ended because the item was already destroyed outside the pool.</summary>
        public long InvalidatedActiveItems { get; }

        public PoolDiagnostics(
            int peakCountActive,
            int peakCountAll,
            long totalCreated,
            long totalSpawned,
            long totalDespawned,
            long failedSpawnRollbacks,
            long rejectedSpawns,
            long invalidDespawns,
            long totalDestroyed,
            long callbackFailures,
            long quarantinedItems,
            long invalidatedInactiveItems,
            long invalidatedActiveItems = 0)
        {
            PeakCountActive = peakCountActive;
            PeakCountAll = peakCountAll;
            TotalCreated = totalCreated;
            TotalSpawned = totalSpawned;
            TotalDespawned = totalDespawned;
            FailedSpawnRollbacks = failedSpawnRollbacks;
            RejectedSpawns = rejectedSpawns;
            InvalidDespawns = invalidDespawns;
            TotalDestroyed = totalDestroyed;
            CallbackFailures = callbackFailures;
            QuarantinedItems = quarantinedItems;
            InvalidatedInactiveItems = invalidatedInactiveItems;
            InvalidatedActiveItems = invalidatedActiveItems;
        }
    }

    public readonly struct PoolProfile
    {
        public int CountAll { get; }
        public int CountActive { get; }
        public int CountInactive { get; }
        public PoolLifecycleState LifecycleState { get; }
        public PoolCapacitySettings CapacitySettings { get; }
        public PoolDiagnostics Diagnostics { get; }

        public PoolProfile(
            int countAll,
            int countActive,
            int countInactive,
            PoolLifecycleState lifecycleState,
            PoolCapacitySettings capacitySettings,
            PoolDiagnostics diagnostics)
        {
            CountAll = countAll;
            CountActive = countActive;
            CountInactive = countInactive;
            LifecycleState = lifecycleState;
            CapacitySettings = capacitySettings;
            Diagnostics = diagnostics;
        }
    }

    public interface IMemoryPool : IDisposable
    {
        int CountAll { get; }
        int CountActive { get; }
        int CountInactive { get; }
        int TrackingCapacity { get; }
        Type ItemType { get; }
        PoolLifecycleState LifecycleState { get; }
        PoolCapacitySettings CapacitySettings { get; }
        PoolDiagnostics Diagnostics { get; }
        PoolProfile Profile { get; }

        void EnsureCapacity(int capacity);
        void Prewarm(int count);
        int WarmupStep(int maxItems);
        void TrimInactive(int targetInactiveCount);
        void DespawnAll();
        int DespawnStep(int maxItems);
        void Clear();
    }

    public interface IDespawnableMemoryPool<in TValue> : IMemoryPool
    {
        bool Contains(TValue item);
        bool Despawn(TValue item);
    }

    public interface IMemoryPool<TValue> : IDespawnableMemoryPool<TValue>
    {
        TValue Spawn();
        bool TrySpawn(out TValue item);
        void ForEachActive(Action<TValue> action);
        void ForEachActive<TState>(TState state, Action<TValue, TState> action);
    }

    public interface IMemoryPool<in TParam1, TValue> : IDespawnableMemoryPool<TValue>
    {
        TValue Spawn(TParam1 param);
        bool TrySpawn(TParam1 param, out TValue item);
        void ForEachActive(Action<TValue> action);
        void ForEachActive<TState>(TState state, Action<TValue, TState> action);
    }
}
