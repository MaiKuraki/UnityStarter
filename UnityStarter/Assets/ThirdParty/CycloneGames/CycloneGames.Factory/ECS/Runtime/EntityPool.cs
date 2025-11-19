# if PRESENT_BURST && PRESENT_ECS
using System.Collections.Generic;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CycloneGames.Factory.ECS.Runtime
{
    public struct PooledEntity : IComponentData { }

    public class EntityPool<TData> where TData : unmanaged, IComponentData
    {
        private readonly EntityManager entityManager;
        private readonly IEntityFactory<TData> factory;
        private readonly Stack<Entity> inactiveEntities = new Stack<Entity>();
        private readonly HashSet<Entity> activeEntities = new HashSet<Entity>();

        public EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, int initialCapacity = 0)
        {
            entityManager = manager;
            factory = entityFactory;

            for (int i = 0; i < initialCapacity; i++)
            {
                var entity = factory.Create(default(TData));
                entityManager.AddComponent<PooledEntity>(entity);

                if (entityManager.HasComponent<LocalTransform>(entity))
                {
                    var transform = entityManager.GetComponentData<LocalTransform>(entity);
                    entityManager.SetEnabled(entity, false);
                    transform.Position = new float3(0, 0, 0);
                    transform.Scale = 0;
                    transform.Rotation = quaternion.identity;
                    entityManager.SetComponentData(entity, transform);
                }
                entityManager.SetEnabled(entity, false);

                inactiveEntities.Push(entity);
            }
        }

        public EntityPool(EntityManager manager, IEntityFactory<TData> entityFactory, TData defaultData, int initialCapacity = 0)
        {
            entityManager = manager;
            factory = entityFactory;

            for (int i = 0; i < initialCapacity; i++)
            {
                var entity = factory.Create(defaultData);
                entityManager.AddComponent<PooledEntity>(entity);

                if (entityManager.HasComponent<LocalTransform>(entity))
                {
                    var transform = entityManager.GetComponentData<LocalTransform>(entity);
                    transform.Position = new float3(0, 0, 0);
                    transform.Scale = 0;
                    transform.Rotation = quaternion.identity;
                    entityManager.SetComponentData(entity, transform);
                }

                entityManager.SetEnabled(entity, false);

                if (entityManager.IsEnabled(entity))
                {
                    Debug.LogError($"CRITICAL: Failed to disable entity {entity} during pool initialization! This will cause visual artifacts.");
                    entityManager.SetEnabled(entity, false);
                }

                inactiveEntities.Push(entity);
            }

            Debug.Log($"EntityPool initialized with {initialCapacity} entities. All entities should be disabled and off-screen.");
        }

        /// <summary>
        /// Synchronously spawns an entity. Can cause structural changes if the pool is empty.
        /// Use with caution inside a system's OnUpdate.
        /// </summary>
        public Entity Spawn(TData data)
        {
            Entity entity;
            if (inactiveEntities.Count > 0)
            {
                entity = inactiveEntities.Pop();
                entityManager.SetComponentData(entity, data);
            }
            else
            {
                entity = factory.Create(data);
                entityManager.AddComponent<PooledEntity>(entity);
            }

            activeEntities.Add(entity);
            return entity;
        }

        /// <summary>
        /// Asynchronously spawns an entity using an EntityCommandBuffer.
        /// This is the recommended method for spawning from within a system.
        /// </summary>
        public Entity Spawn(EntityCommandBuffer ecb, TData data)
        {
            Entity entity;
            if (inactiveEntities.Count > 0)
            {
                entity = inactiveEntities.Pop();
                ecb.SetComponent(entity, data);
                ecb.AddComponent<PooledEntity>(entity);
            }
            else
            {
                entity = factory.Create(ecb, data);
                ecb.AddComponent<PooledEntity>(entity);
                ecb.SetEnabled(entity, false);
            }
            activeEntities.Add(entity);

            return entity;
        }

        public void Despawn(Entity entity, EntityCommandBuffer ecb)
        {
            if (!entityManager.Exists(entity) || !entityManager.HasComponent<PooledEntity>(entity))
            {
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

            activeEntities.Remove(entity);
            inactiveEntities.Push(entity);
        }

        public void DespawnAll(EntityCommandBuffer ecb)
        {
            foreach (var entity in activeEntities)
            {
                if (entityManager.Exists(entity))
                {
                    ecb.SetEnabled(entity, false);
                    if (entityManager.HasComponent<LocalTransform>(entity))
                    {
                        ecb.SetEnabled(entity, false);
                        var transform = entityManager.GetComponentData<LocalTransform>(entity);
                        transform.Position = new float3(0, 0, 0);
                        transform.Scale = 0;
                        transform.Rotation = quaternion.identity;
                        ecb.SetComponent(entity, transform);
                    }

                    inactiveEntities.Push(entity);
                }
            }
            activeEntities.Clear();
        }

        public IReadOnlyCollection<Entity> GetActiveEntities()
        {
            return activeEntities;
        }
    }
}
#endif // PRESENT_BURST && PRESENT_ECS