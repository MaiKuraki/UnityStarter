# if PRESENT_BURST && PRESENT_ECS
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CycloneGames.Factory.ECS.Runtime
{
    public struct PooledEntity : IComponentData { }

    public interface IEntityPool<TData> where TData : unmanaged, IComponentData
    {
        int CountAll { get; }
        int CountActive { get; }
        int CountInactive { get; }
        EntityPoolCapacitySettings CapacitySettings { get; }
        EntityPoolDiagnostics Diagnostics { get; }
        EntityPoolProfile Profile { get; }

        Entity Spawn(TData data);
        bool TrySpawn(TData data, out Entity entity);
        Entity Spawn(EntityCommandBuffer ecb, TData data);
        bool TrySpawn(EntityCommandBuffer ecb, TData data, out Entity entity);
        Entity SpawnReuseOnly(EntityCommandBuffer ecb, TData data);
        bool TrySpawnReuseOnly(EntityCommandBuffer ecb, TData data, out Entity entity);
        void Despawn(Entity entity, EntityCommandBuffer ecb);
        void DespawnAll(EntityCommandBuffer ecb);
        void Prewarm(int count);
        int ExpandInactive(int count);
        int EnsureInactive(int targetInactiveCount);
        void TrimInactive(int targetInactiveCount);
        void TrimInactive(int targetInactiveCount, EntityCommandBuffer ecb);
        void DestroyAll();
        void DestroyAll(EntityCommandBuffer ecb);
        IReadOnlyList<Entity> GetActiveEntities();
    }

    public enum EntityPoolOverflowPolicy
    {
        Throw = 0,
        ReturnNull = 1,
    }

    public readonly struct EntityPoolCapacitySettings
    {
        public int SoftCapacity { get; }
        public int HardCapacity { get; }
        public EntityPoolOverflowPolicy OverflowPolicy { get; }

        public EntityPoolCapacitySettings(int softCapacity = 0, int hardCapacity = -1, EntityPoolOverflowPolicy overflowPolicy = EntityPoolOverflowPolicy.Throw)
        {
            if (softCapacity < 0) throw new System.ArgumentOutOfRangeException(nameof(softCapacity));
            if (hardCapacity == 0 || hardCapacity < -1) throw new System.ArgumentOutOfRangeException(nameof(hardCapacity));
            if (hardCapacity > 0 && softCapacity > hardCapacity) throw new System.ArgumentException("Soft capacity cannot exceed hard capacity.");

            SoftCapacity = softCapacity;
            HardCapacity = hardCapacity;
            OverflowPolicy = overflowPolicy;
        }
    }

    public readonly struct EntityPoolProfile
    {
        public int CountAll { get; }
        public int CountActive { get; }
        public int CountInactive { get; }
        public EntityPoolCapacitySettings CapacitySettings { get; }
        public EntityPoolDiagnostics Diagnostics { get; }

        public EntityPoolProfile(
            int countAll,
            int countActive,
            int countInactive,
            EntityPoolCapacitySettings capacitySettings,
            EntityPoolDiagnostics diagnostics)
        {
            CountAll = countAll;
            CountActive = countActive;
            CountInactive = countInactive;
            CapacitySettings = capacitySettings;
            Diagnostics = diagnostics;
        }
    }

    public readonly struct EntityPoolDiagnostics
    {
        public int PeakCountActive { get; }
        public int PeakCountAll { get; }
        public int TotalCreated { get; }
        public int TotalSpawned { get; }
        public int TotalDespawned { get; }
        public int RejectedSpawns { get; }
        public int InvalidDespawns { get; }
        public int DestroyedOnTrim { get; }

        public EntityPoolDiagnostics(
            int peakCountActive,
            int peakCountAll,
            int totalCreated,
            int totalSpawned,
            int totalDespawned,
            int rejectedSpawns,
            int invalidDespawns,
            int destroyedOnTrim)
        {
            PeakCountActive = peakCountActive;
            PeakCountAll = peakCountAll;
            TotalCreated = totalCreated;
            TotalSpawned = totalSpawned;
            TotalDespawned = totalDespawned;
            RejectedSpawns = rejectedSpawns;
            InvalidDespawns = invalidDespawns;
            DestroyedOnTrim = destroyedOnTrim;
        }
    }

    public class EntityPool<TData> : IEntityPool<TData> where TData : unmanaged, IComponentData
    {
        private readonly EntityManager entityManager;
        private readonly IEntityFactory<TData> factory;
        private readonly Stack<Entity> inactiveEntities;
        private readonly List<Entity> _activeEntities;
        private readonly Dictionary<Entity, int> _activeEntityIndices;
        private readonly ReadOnlyCollection<Entity> _activeEntitiesReadOnly;
        private readonly TData defaultData;
        private readonly bool hasDefaultData;

        private int softCapacity;
        private int hardCapacity;
        private EntityPoolOverflowPolicy overflowPolicy;

        private int peakCountActive;
        private int peakCountAll;
        private int totalCreated;
        private int totalSpawned;
        private int totalDespawned;
        private int rejectedSpawns;
        private int invalidDespawns;
        private int destroyedOnTrim;

        public int CountAll => CountActive + CountInactive;
        public int CountActive => _activeEntities.Count;
        public int CountInactive => inactiveEntities.Count;
        public EntityPoolCapacitySettings CapacitySettings => new(softCapacity, hardCapacity, overflowPolicy);
        public EntityPoolDiagnostics Diagnostics => new(
            peakCountActive,
            peakCountAll,
            totalCreated,
            totalSpawned,
            totalDespawned,
            rejectedSpawns,
            invalidDespawns,
            destroyedOnTrim);
        public EntityPoolProfile Profile => new(CountAll, CountActive, CountInactive, CapacitySettings, Diagnostics);

        public EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, int initialCapacity = 0)
            : this(manager, entityFactory, default, new EntityPoolCapacitySettings(initialCapacity, -1), false)
        {
        }

        public EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, TData defaultData, int initialCapacity = 0)
            : this(manager, entityFactory, defaultData, new EntityPoolCapacitySettings(initialCapacity, -1), true)
        {
        }

        public EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, EntityPoolCapacitySettings capacitySettings)
            : this(manager, entityFactory, default, capacitySettings, false)
        {
        }

        public EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, TData defaultData, EntityPoolCapacitySettings capacitySettings)
            : this(manager, entityFactory, defaultData, capacitySettings, true)
        {
        }

        private EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, TData initialDefaultData, EntityPoolCapacitySettings capacitySettings, bool hasInitialDefaultData)
        {
            entityManager = manager;
            factory = entityFactory;
            defaultData = initialDefaultData;
            hasDefaultData = hasInitialDefaultData;
            softCapacity = capacitySettings.SoftCapacity;
            hardCapacity = capacitySettings.HardCapacity;
            overflowPolicy = capacitySettings.OverflowPolicy;
            int initialCollectionCapacity = GetInitialCollectionCapacity(softCapacity, hardCapacity);
            inactiveEntities = new Stack<Entity>(initialCollectionCapacity);
            _activeEntities = new List<Entity>(initialCollectionCapacity);
            _activeEntityIndices = new Dictionary<Entity, int>(initialCollectionCapacity);
            _activeEntitiesReadOnly = new ReadOnlyCollection<Entity>(_activeEntities);

            if (softCapacity > 0)
            {
                Prewarm(softCapacity);
            }
        }

        /// <summary>
        /// Synchronously spawns an entity. Can cause structural changes if the pool is empty.
        /// Use with caution inside a system's OnUpdate.
        /// </summary>
        public Entity Spawn(TData data)
        {
            TrySpawnInternal(data, throwOnFailure: true, out var entity);
            return entity;
        }

        public bool TrySpawn(TData data, out Entity entity)
        {
            return TrySpawnInternal(data, throwOnFailure: false, out entity);
        }

        /// <summary>
        /// Spawns an entity using an EntityCommandBuffer for deferred structural changes.
        /// If the inactive pool has reusable entities, no structural change occurs.
        /// If a new entity must be created, the pool falls back to EntityManager
        /// to obtain a real Entity for tracking (structural change is unavoidable).
        /// </summary>
        public Entity Spawn(EntityCommandBuffer ecb, TData data)
        {
            TrySpawnInternal(ecb, data, throwOnFailure: true, reuseOnly: false, out var entity);
            return entity;
        }

        public bool TrySpawn(EntityCommandBuffer ecb, TData data, out Entity entity)
        {
            return TrySpawnInternal(ecb, data, throwOnFailure: false, reuseOnly: false, out entity);
        }

        public Entity SpawnReuseOnly(EntityCommandBuffer ecb, TData data)
        {
            TrySpawnInternal(ecb, data, throwOnFailure: true, reuseOnly: true, out var entity);
            return entity;
        }

        public bool TrySpawnReuseOnly(EntityCommandBuffer ecb, TData data, out Entity entity)
        {
            return TrySpawnInternal(ecb, data, throwOnFailure: false, reuseOnly: true, out entity);
        }

        public void Despawn(Entity entity, EntityCommandBuffer ecb)
        {
            if (entity == Entity.Null)
            {
                invalidDespawns++;
                return;
            }

            if (!_activeEntityIndices.ContainsKey(entity))
            {
                invalidDespawns++;
                return;
            }

            if (!entityManager.Exists(entity) || !entityManager.HasComponent<PooledEntity>(entity))
            {
                // Entity was destroyed or PooledEntity tag removed externally — clean up active tracking.
                RemoveActiveEntity(entity);
                invalidDespawns++;
                return;
            }

            ecb.SetEnabled(entity, false);
            if (entityManager.HasComponent<LocalTransform>(entity))
            {
                var transform = entityManager.GetComponentData<LocalTransform>(entity);
                transform.Position = new float3(0, 0, 0);
                transform.Scale = 0;
                transform.Rotation = quaternion.identity;
                ecb.SetComponent(entity, transform);
            }

            RemoveActiveEntity(entity);
            inactiveEntities.Push(entity);
            totalDespawned++;
        }

        public void DespawnAll(EntityCommandBuffer ecb)
        {
            for (int i = _activeEntities.Count - 1; i >= 0; i--)
            {
                var entity = _activeEntities[i];
                if (entityManager.Exists(entity))
                {
                    ecb.SetEnabled(entity, false);
                    if (entityManager.HasComponent<LocalTransform>(entity))
                    {
                        var transform = entityManager.GetComponentData<LocalTransform>(entity);
                        transform.Position = new float3(0, 0, 0);
                        transform.Scale = 0;
                        transform.Rotation = quaternion.identity;
                        ecb.SetComponent(entity, transform);
                    }

                    inactiveEntities.Push(entity);
                }
            }
            totalDespawned += _activeEntities.Count;
            _activeEntities.Clear();
            _activeEntityIndices.Clear();
        }

        public void Prewarm(int count)
        {
            if (count <= 0) return;

            int capacityLeft = GetRemainingCapacity();
            if (capacityLeft == 0) return;

            int createCount = hardCapacity > 0 ? math.min(count, capacityLeft) : count;
            TData seed = hasDefaultData ? defaultData : default;

            for (int i = 0; i < createCount; i++)
            {
                var entity = factory.Create(seed);
                totalCreated++;
                EnsurePooledState(entity);
                inactiveEntities.Push(entity);
                UpdatePeaks();
            }
        }

        /// <summary>
        /// Grows the inactive pool by creating up to <paramref name="count"/> new entities.
        /// This performs structural changes and should not be called from hot ECS OnUpdate paths.
        /// Returns the number of entities actually created.
        /// </summary>
        public int ExpandInactive(int count)
        {
            if (count <= 0)
            {
                return 0;
            }

            int capacityLeft = GetRemainingCapacity();
            if (capacityLeft == 0)
            {
                return 0;
            }

            int createCount = hardCapacity > 0 ? math.min(count, capacityLeft) : count;
            TData seed = hasDefaultData ? defaultData : default;

            int created = 0;
            for (int i = 0; i < createCount; i++)
            {
                var entity = factory.Create(seed);
                totalCreated++;
                EnsurePooledState(entity);
                inactiveEntities.Push(entity);
                created++;
            }

            UpdatePeaks();
            return created;
        }

        /// <summary>
        /// Ensures at least <paramref name="targetInactiveCount"/> entities are available in inactive pool.
        /// Returns how many were created.
        /// </summary>
        public int EnsureInactive(int targetInactiveCount)
        {
            if (targetInactiveCount <= CountInactive)
            {
                return 0;
            }

            return ExpandInactive(targetInactiveCount - CountInactive);
        }

        /// <summary>
        /// Trims inactive entities by directly destroying them via EntityManager.
        /// This causes structural changes. Do NOT call from within a system's OnUpdate.
        /// Use the <see cref="TrimInactive(int, EntityCommandBuffer)"/> overload instead.
        /// </summary>
        public void TrimInactive(int targetInactiveCount)
        {
            if (targetInactiveCount < 0) throw new System.ArgumentOutOfRangeException(nameof(targetInactiveCount));

            while (inactiveEntities.Count > targetInactiveCount)
            {
                var entity = inactiveEntities.Pop();
                if (!entityManager.Exists(entity))
                {
                    continue;
                }

                entityManager.DestroyEntity(entity);
                destroyedOnTrim++;
            }
        }

        /// <summary>
        /// Trims inactive entities via an EntityCommandBuffer, safe for use inside systems.
        /// </summary>
        public void TrimInactive(int targetInactiveCount, EntityCommandBuffer ecb)
        {
            if (targetInactiveCount < 0) throw new System.ArgumentOutOfRangeException(nameof(targetInactiveCount));

            while (inactiveEntities.Count > targetInactiveCount)
            {
                var entity = inactiveEntities.Pop();
                if (!entityManager.Exists(entity))
                {
                    continue;
                }

                ecb.DestroyEntity(entity);
                destroyedOnTrim++;
            }
        }

        /// <summary>
        /// Destroys all pooled entities (both active and inactive) via EntityManager.
        /// Causes structural changes. Do NOT call from within a system's OnUpdate.
        /// </summary>
        public void DestroyAll()
        {
            for (int i = _activeEntities.Count - 1; i >= 0; i--)
            {
                var entity = _activeEntities[i];
                if (entityManager.Exists(entity))
                    entityManager.DestroyEntity(entity);
            }
            _activeEntities.Clear();
            _activeEntityIndices.Clear();

            while (inactiveEntities.Count > 0)
            {
                var entity = inactiveEntities.Pop();
                if (entityManager.Exists(entity))
                    entityManager.DestroyEntity(entity);
            }
        }

        /// <summary>
        /// Destroys all pooled entities (both active and inactive) via an EntityCommandBuffer.
        /// Safe for use inside systems.
        /// </summary>
        public void DestroyAll(EntityCommandBuffer ecb)
        {
            for (int i = _activeEntities.Count - 1; i >= 0; i--)
            {
                var entity = _activeEntities[i];
                if (entityManager.Exists(entity))
                    ecb.DestroyEntity(entity);
            }
            _activeEntities.Clear();
            _activeEntityIndices.Clear();

            while (inactiveEntities.Count > 0)
            {
                var entity = inactiveEntities.Pop();
                if (entityManager.Exists(entity))
                    ecb.DestroyEntity(entity);
            }
        }

        public IReadOnlyList<Entity> GetActiveEntities()
        {
            return _activeEntitiesReadOnly;
        }

        private bool TrySpawnInternal(TData data, bool throwOnFailure, out Entity entity)
        {
            if (!TryGetReusableEntity(throwOnFailure, out entity))
            {
                return false;
            }

            if (entityManager.Exists(entity))
            {
                entityManager.SetComponentData(entity, data);
            }
            else
            {
                entity = factory.Create(data);
                totalCreated++;
                EnsurePooledTag(entity);
            }

            entityManager.SetEnabled(entity, true);
            TrackActiveEntity(entity);
            totalSpawned++;
            UpdatePeaks();
            return true;
        }

        /// <summary>
        /// ECB spawn path: reuses inactive entities only — never creates new entities.
        /// This guarantees zero structural changes, making it safe to call during OnUpdate.
        /// If the pool has no inactive entities, the spawn is rejected.
        /// Callers should Prewarm the pool or use the non-ECB <see cref="Spawn(TData)"/> overload to grow the pool.
        /// </summary>
        private bool TrySpawnInternal(EntityCommandBuffer ecb, TData data, bool throwOnFailure, bool reuseOnly, out Entity entity)
        {
            while (inactiveEntities.Count > 0)
            {
                entity = inactiveEntities.Pop();
                if (entity == Entity.Null || !entityManager.Exists(entity))
                    continue;

                ecb.SetComponent(entity, data);
                ecb.SetEnabled(entity, true);
                TrackActiveEntity(entity);
                totalSpawned++;
                UpdatePeaks();
                return true;
            }

            // No inactive entities — cannot create via ECB without structural changes.
            if (!reuseOnly)
            {
                if (hardCapacity > 0 && CountAll >= hardCapacity)
                {
                    rejectedSpawns++;
                    if (overflowPolicy == EntityPoolOverflowPolicy.ReturnNull && !throwOnFailure)
                    {
                        entity = Entity.Null;
                        return false;
                    }

                    throw new System.InvalidOperationException($"EntityPool for {typeof(TData).Name} reached max capacity {hardCapacity}.");
                }

                entity = factory.Create(data);
                totalCreated++;
                EnsurePooledTag(entity);
                entityManager.SetEnabled(entity, true);
                TrackActiveEntity(entity);
                totalSpawned++;
                UpdatePeaks();
                return true;
            }

            rejectedSpawns++;
            if (throwOnFailure)
                throw new System.InvalidOperationException(
                    $"EntityPool<{typeof(TData).Name}> has no inactive entities to reuse via ECB. " +
                    "Prewarm first or use the non-reuse-only ECB spawn overload.");

            entity = Entity.Null;
            return false;
        }

        private bool TryGetReusableEntity(bool throwOnFailure, out Entity entity)
        {
            while (inactiveEntities.Count > 0)
            {
                entity = inactiveEntities.Pop();
                if (entity == Entity.Null || !entityManager.Exists(entity))
                {
                    continue;
                }
                return true;
            }

            if (hardCapacity > 0 && CountAll >= hardCapacity)
            {
                rejectedSpawns++;
                if (overflowPolicy == EntityPoolOverflowPolicy.ReturnNull && !throwOnFailure)
                {
                    entity = Entity.Null;
                    return false;
                }

                throw new System.InvalidOperationException($"EntityPool for {typeof(TData).Name} reached max capacity {hardCapacity}.");
            }

            entity = Entity.Null;
            return true;
        }

        private int GetRemainingCapacity()
        {
            if (hardCapacity <= 0)
            {
                return int.MaxValue;
            }

            return math.max(0, hardCapacity - CountAll);
        }

        private static int GetInitialCollectionCapacity(int softCapacity, int hardCapacity)
        {
            if (hardCapacity > 0)
            {
                return math.max(16, math.min(softCapacity, hardCapacity));
            }

            return math.max(16, softCapacity);
        }

        private void UpdatePeaks()
        {
            if (CountActive > peakCountActive)
            {
                peakCountActive = CountActive;
            }

            if (CountAll > peakCountAll)
            {
                peakCountAll = CountAll;
            }
        }

        private void TrackActiveEntity(Entity entity)
        {
            int index = _activeEntities.Count;
            _activeEntities.Add(entity);
            _activeEntityIndices[entity] = index;
        }

        private bool RemoveActiveEntity(Entity entity)
        {
            if (!_activeEntityIndices.TryGetValue(entity, out int index))
            {
                return false;
            }

            int lastIndex = _activeEntities.Count - 1;
            Entity lastEntity = _activeEntities[lastIndex];

            _activeEntities[index] = lastEntity;
            _activeEntities.RemoveAt(lastIndex);
            _activeEntityIndices.Remove(entity);

            if (index != lastIndex)
            {
                _activeEntityIndices[lastEntity] = index;
            }

            return true;
        }

        private void EnsurePooledState(Entity entity)
        {
            EnsurePooledTag(entity);
            ResetTransform(entity);
            entityManager.SetEnabled(entity, false);
        }

        private void EnsurePooledTag(Entity entity)
        {
            if (!entityManager.HasComponent<PooledEntity>(entity))
            {
                entityManager.AddComponent<PooledEntity>(entity);
            }
        }

        private void ResetTransform(Entity entity)
        {
            if (!entityManager.HasComponent<LocalTransform>(entity))
            {
                return;
            }

            var transform = entityManager.GetComponentData<LocalTransform>(entity);
            transform.Position = new float3(0, 0, 0);
            transform.Scale = 0;
            transform.Rotation = quaternion.identity;
            entityManager.SetComponentData(entity, transform);
        }
    }
}
#endif // PRESENT_BURST && PRESENT_ECS
