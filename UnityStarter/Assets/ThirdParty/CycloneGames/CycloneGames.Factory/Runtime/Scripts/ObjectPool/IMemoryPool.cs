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

    public readonly struct PoolCapacitySettings : IEquatable<PoolCapacitySettings>
    {
        public int SoftCapacity { get; }
        public int HardCapacity { get; }
        public PoolOverflowPolicy OverflowPolicy { get; }
        public PoolTrimPolicy TrimPolicy { get; }

        public PoolCapacitySettings(
            int softCapacity = 0,
            int hardCapacity = -1,
            PoolOverflowPolicy overflowPolicy = PoolOverflowPolicy.Throw,
            PoolTrimPolicy trimPolicy = PoolTrimPolicy.Manual)
        {
            if (softCapacity < 0) throw new ArgumentOutOfRangeException(nameof(softCapacity));
            if (hardCapacity == 0 || hardCapacity < -1) throw new ArgumentOutOfRangeException(nameof(hardCapacity));
            if (hardCapacity > 0 && softCapacity > hardCapacity) throw new ArgumentException("Soft capacity cannot exceed hard capacity.");

            SoftCapacity = softCapacity;
            HardCapacity = hardCapacity;
            OverflowPolicy = overflowPolicy;
            TrimPolicy = trimPolicy;
        }

        public bool Equals(PoolCapacitySettings other)
        {
            return SoftCapacity == other.SoftCapacity
                && HardCapacity == other.HardCapacity
                && OverflowPolicy == other.OverflowPolicy
                && TrimPolicy == other.TrimPolicy;
        }

        public override bool Equals(object obj)
        {
            return obj is PoolCapacitySettings other && Equals(other);
        }

        public override int GetHashCode()
        {
            unchecked
            {
                uint h = (uint)SoftCapacity;
                h = ((h >> 16) ^ h) * 0x45d9f3b;
                h ^= (uint)HardCapacity * 0x9e3779b9;
                h = ((h >> 16) ^ h) * 0x45d9f3b;
                h ^= (uint)OverflowPolicy * 0x27d4eb2d;
                h ^= (uint)TrimPolicy * 0x9e3779b9;
                h = (h >> 16) ^ h;
                return (int)h;
            }
        }
    }

    public readonly struct PoolDiagnostics
    {
        public int PeakCountActive { get; }
        public int PeakCountAll { get; }
        public int TotalCreated { get; }
        public int TotalSpawned { get; }
        public int TotalDespawned { get; }
        public int FailedSpawnRollbacks { get; }
        public int RejectedSpawns { get; }
        public int InvalidDespawns { get; }
        public int DestroyedOnTrim { get; }

        public PoolDiagnostics(
            int peakCountActive,
            int peakCountAll,
            int totalCreated,
            int totalSpawned,
            int totalDespawned,
            int failedSpawnRollbacks,
            int rejectedSpawns,
            int invalidDespawns,
            int destroyedOnTrim)
        {
            PeakCountActive = peakCountActive;
            PeakCountAll = peakCountAll;
            TotalCreated = totalCreated;
            TotalSpawned = totalSpawned;
            TotalDespawned = totalDespawned;
            FailedSpawnRollbacks = failedSpawnRollbacks;
            RejectedSpawns = rejectedSpawns;
            InvalidDespawns = invalidDespawns;
            DestroyedOnTrim = destroyedOnTrim;
        }
    }

    public readonly struct PoolProfile
    {
        public int CountAll { get; }
        public int CountActive { get; }
        public int CountInactive { get; }
        public PoolCapacitySettings CapacitySettings { get; }
        public PoolDiagnostics Diagnostics { get; }

        public PoolProfile(
            int countAll,
            int countActive,
            int countInactive,
            PoolCapacitySettings capacitySettings,
            PoolDiagnostics diagnostics)
        {
            CountAll = countAll;
            CountActive = countActive;
            CountInactive = countInactive;
            CapacitySettings = capacitySettings;
            Diagnostics = diagnostics;
        }
    }

    public interface IMemoryPool
    {
        int CountAll { get; }
        int CountActive { get; }
        int CountInactive { get; }
        Type ItemType { get; }
        PoolCapacitySettings CapacitySettings { get; }
        PoolDiagnostics Diagnostics { get; }
        PoolProfile Profile { get; }

        void Clear();
        void DespawnAll();
        int DespawnStep(int maxItems);
        void Prewarm(int count);
        int WarmupStep(int maxItems);
        void TrimInactive(int targetInactiveCount);
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
