# if PRESENT_BURST && PRESENT_ECS
using CycloneGames.Factory.ECS.Runtime;
using Unity.Burst;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;
using UnityEngine;

namespace CycloneGames.Factory.ECS.Samples
{
    public enum BulletSpawnStrategy : byte
    {
        PoolReuse = 0,
        DirectInstantiateDestroy = 1,
    }

    public struct ScreenInfo : IComponentData
    {
        public float2 ScreenMin;
        public float2 ScreenMax;
        public Unity.Mathematics.Random Random;
    }

    public struct DefaultBulletData : IComponentData
    {
        public BulletComponent Value;
    }

    public struct BulletPrefabSingleton : IComponentData
    {
        public Entity Value;
    }

    public struct BulletSpawner : IComponentData
    {
        public float NextSpawnTime;
        public float SpawnRate;
        public int InitialPoolSize;
        public int HardCapacity;
        public byte ReturnNullOnOverflow;
        public byte SpawnStrategy;
    }

    public struct BulletPoolMetrics : IComponentData
    {
        public byte SpawnStrategy;
        public int CountActive;
        public int CountInactive;
        public int CountAll;
        public int PeakCountActive;
        public int PeakCountAll;
        public int TotalCreated;
        public int TotalSpawned;
        public int TotalDespawned;
        public int RejectedSpawns;
        public int InvalidDespawns;
    }

    public class BulletSpawnerAuthoring : MonoBehaviour
    {
        public GameObject BulletPrefab;
        public float SpawnsPerSecond = 200f;
        public int InitialPoolSize = 100;
        public int HardCapacity = 10000;
        public bool ReturnNullOnOverflow = true;
        public BulletSpawnStrategy SpawnStrategy = BulletSpawnStrategy.PoolReuse;

        class Baker : Baker<BulletSpawnerAuthoring>
        {
            public override void Bake(BulletSpawnerAuthoring authoring)
            {
                var entity = GetEntity(TransformUsageFlags.None);

                DependsOn(authoring.BulletPrefab);
                var bulletPrefabEntity = GetEntity(authoring.BulletPrefab, TransformUsageFlags.Dynamic);

                AddComponent(entity, new BulletPrefabSingleton { Value = bulletPrefabEntity });
                AddComponent(entity, new BulletSpawner
                {
                    NextSpawnTime = 0.0f,
                    SpawnRate = 1.0f / authoring.SpawnsPerSecond,
                    InitialPoolSize = authoring.InitialPoolSize,
                    HardCapacity = math.max(authoring.InitialPoolSize, authoring.HardCapacity),
                    ReturnNullOnOverflow = authoring.ReturnNullOnOverflow ? (byte)1 : (byte)0,
                    SpawnStrategy = (byte)authoring.SpawnStrategy
                });
                AddComponent(entity, default(BulletPoolMetrics));
            }
        }
    }


    /// <summary>
    /// Manages the lifecycle of the bullet pool using the generic EntityPool class.
    /// This system handles spawning new bullets and despawning those marked for removal.
    /// </summary>
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BulletLifetimeCheckSystem))]
    public partial class BulletPoolManagerSystem : SystemBase
    {
        private EntityPool<BulletComponent> bulletPool;
        private EntityQuery despawnQuery;
        private Entity prefabEntity;
        private int pendingPrewarmCount;
        private bool firstSpawnDiagLogged;
        private int directActiveCount;
        private int directPeakCountActive;
        private int directCreated;
        private int directSpawned;
        private int directDespawned;
        private int directRejected;

        protected override void OnCreate()
        {
            RequireForUpdate<BulletSpawner>();
            despawnQuery = GetEntityQuery(typeof(DespawnTag));
        }

        protected override void OnStartRunning()
        {
            var spawnerEntity = SystemAPI.GetSingletonEntity<BulletSpawner>();
            var masterPrefabEntity = EntityManager.GetComponentData<BulletPrefabSingleton>(spawnerEntity).Value;
            var spawner = SystemAPI.GetSingleton<BulletSpawner>();

            if (masterPrefabEntity == Entity.Null)
            {
                Debug.LogError("Bullet Prefab has not been assigned in the BulletSpawnerAuthoring component.");
                return;
            }

            // Get the default bullet data from the master prefab
            var defaultBulletData = EntityManager.GetComponentData<BulletComponent>(masterPrefabEntity);
            prefabEntity = masterPrefabEntity;
            Debug.Log($"Default bullet data - Velocity: {defaultBulletData.Velocity}, Lifetime: {defaultBulletData.Lifetime}");

            if ((BulletSpawnStrategy)spawner.SpawnStrategy == BulletSpawnStrategy.PoolReuse)
            {
                var factory = new PrefabEntityFactory<BulletComponent>(EntityManager, masterPrefabEntity);
                // Create pool WITHOUT auto-prewarm (softCapacity = 0).
                // Prewarm is deferred to the first OnUpdate frame to avoid structural changes
                // that would invalidate SystemAPI type handles used later in the same Update() call.
                var capacitySettings = new EntityPoolCapacitySettings(
                    0,
                    spawner.HardCapacity,
                    spawner.ReturnNullOnOverflow != 0 ? EntityPoolOverflowPolicy.ReturnNull : EntityPoolOverflowPolicy.Throw);
                bulletPool = new EntityPool<BulletComponent>(EntityManager, factory, defaultBulletData, capacitySettings);
                pendingPrewarmCount = spawner.InitialPoolSize;
            }
            else
            {
                bulletPool = null;
                directActiveCount = 0;
                directPeakCountActive = 0;
                directCreated = 0;
                directSpawned = 0;
                directDespawned = 0;
                directRejected = 0;
            }

            // Now set the singleton with the default data obtained earlier
            if (!SystemAPI.HasSingleton<DefaultBulletData>())
            {
                EntityManager.CreateEntity(typeof(DefaultBulletData));
            }
            SystemAPI.SetSingleton(new DefaultBulletData { Value = defaultBulletData });
            UpdateMetrics();

            if ((BulletSpawnStrategy)spawner.SpawnStrategy == BulletSpawnStrategy.PoolReuse)
            {
                Debug.Log($"BulletPool created (prewarm deferred to first OnUpdate). Pending={pendingPrewarmCount}");
            }
            else
            {
                Debug.Log($"Direct ECS spawn/destroy mode initialized. HardCapacity={spawner.HardCapacity}");
            }

            // Check if there are any other Bullet entities that shouldn't exist
            var allBulletQuery = GetEntityQuery(typeof(BulletComponent));
            var allBullets = allBulletQuery.ToEntityArray(Unity.Collections.Allocator.TempJob);

            // Also check disabled entities
            var allBulletQueryWithDisabled = GetEntityQuery(ComponentType.ReadOnly<BulletComponent>());
            var allBulletsWithDisabled = allBulletQueryWithDisabled.ToEntityArray(Unity.Collections.Allocator.TempJob);
            // Debug.Log($"Total Bullet entities after pool initialization: {allBullets.Length}\nTotal Bullet entities (including disabled): {allBulletsWithDisabled.Length}");

            allBullets.Dispose();
            allBulletsWithDisabled.Dispose();
        }

        protected override void OnUpdate()
        {
            // Deferred prewarm: structural changes (EntityManager.CreateEntity) must happen
            // at the start of OnUpdate so that SystemAPI handles are re-acquired on the next frame.
            if (pendingPrewarmCount > 0)
            {
                bulletPool?.Prewarm(pendingPrewarmCount);
                Debug.Log($"BulletPool prewarmed with {pendingPrewarmCount} entities. " +
                          $"CountActive={bulletPool?.CountActive}, CountInactive={bulletPool?.CountInactive}");
                pendingPrewarmCount = 0;
                firstSpawnDiagLogged = false;
                return; // Skip this frame — type handles are now stale after structural changes.
            }

            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(World.Unmanaged);

            if (!despawnQuery.IsEmpty)
            {
                var spawnStrategy = (BulletSpawnStrategy)SystemAPI.GetSingleton<BulletSpawner>().SpawnStrategy;
                using (var entitiesToDespawn = despawnQuery.ToEntityArray(Unity.Collections.Allocator.TempJob))
                {
                    foreach (var entity in entitiesToDespawn)
                    {
                        if (spawnStrategy == BulletSpawnStrategy.PoolReuse)
                        {
                            bulletPool.Despawn(entity, ecb);
                            ecb.RemoveComponent<DespawnTag>(entity);
                        }
                        else
                        {
                            ecb.DestroyEntity(entity);
                            directActiveCount = math.max(0, directActiveCount - 1);
                            directDespawned++;
                        }
                    }
                }
            }

            var spawner = SystemAPI.GetSingletonRW<BulletSpawner>();
            if (spawner.ValueRO.SpawnRate <= 0)
            {
                UpdateMetrics();
                return;
            }

            var screenInfo = SystemAPI.GetSingleton<ScreenInfo>();
            if (screenInfo.ScreenMax.x == 0)
            {
                if (!firstSpawnDiagLogged)
                {
                    Debug.LogWarning($"[Factory][ECS] ScreenInfo.ScreenMax is zero — Camera.main may be null. " +
                                     $"ScreenMin={screenInfo.ScreenMin}, ScreenMax={screenInfo.ScreenMax}");
                    firstSpawnDiagLogged = true;
                }
                UpdateMetrics();
                return;
            }

            var defaultBulletData = SystemAPI.GetSingleton<DefaultBulletData>();
            float currentTime = (float)SystemAPI.Time.ElapsedTime;

            int bulletsToSpawn = 0;
            while (currentTime >= spawner.ValueRO.NextSpawnTime)
            {
                bulletsToSpawn++;
                spawner.ValueRW.NextSpawnTime += spawner.ValueRO.SpawnRate;
            }

            if (bulletsToSpawn == 0) return;

            if (!firstSpawnDiagLogged)
            {
                Debug.Log($"[Factory][ECS] First spawn frame: bulletsToSpawn={bulletsToSpawn}, " +
                          $"PoolInactive={bulletPool?.CountInactive}, PoolActive={bulletPool?.CountActive}, " +
                          $"ScreenMax={screenInfo.ScreenMax}, SpawnRate={spawner.ValueRO.SpawnRate}");
                firstSpawnDiagLogged = true;
            }

            var random = SystemAPI.GetSingletonRW<ScreenInfo>();

            int spawnedCount = 0;
            int rejectedCount = 0;
            for (int i = 0; i < bulletsToSpawn; i++)
            {
                Entity newBullet;
                bool spawned;
                if ((BulletSpawnStrategy)spawner.ValueRO.SpawnStrategy == BulletSpawnStrategy.PoolReuse)
                {
                    spawned = bulletPool.TrySpawn(ecb, defaultBulletData.Value, out newBullet);
                }
                else
                {
                    if (directActiveCount >= spawner.ValueRO.HardCapacity)
                    {
                        directRejected++;
                        continue;
                    }

                    newBullet = ecb.Instantiate(prefabEntity);
                    ecb.SetComponent(newBullet, defaultBulletData.Value);
                    spawned = true;
                    directActiveCount++;
                    directCreated++;
                    directSpawned++;
                    if (directActiveCount > directPeakCountActive)
                    {
                        directPeakCountActive = directActiveCount;
                    }
                }

                if (!spawned)
                {
                    rejectedCount++;
                    continue;
                }

                spawnedCount++;
                float randomX = random.ValueRW.Random.NextFloat(random.ValueRO.ScreenMin.x, random.ValueRO.ScreenMax.x);
                float randomY = random.ValueRW.Random.NextFloat(random.ValueRO.ScreenMin.y, random.ValueRO.ScreenMax.y);

                var newTransform = LocalTransform.FromPosition(randomX, randomY, 0);
                ecb.SetComponent(newBullet, newTransform);
            }

            if (!firstSpawnDiagLogged || (spawnedCount + rejectedCount > 0 && rejectedCount > 0))
            {
                Debug.Log($"[Factory][ECS] Spawn result: requested={bulletsToSpawn}, spawned={spawnedCount}, rejected={rejectedCount}, " +
                          $"PoolActive={bulletPool?.CountActive}, PoolInactive={bulletPool?.CountInactive}");
                firstSpawnDiagLogged = true;
            }

            UpdateMetrics();
        }

        private void UpdateMetrics()
        {
            if (!SystemAPI.HasSingleton<BulletPoolMetrics>())
            {
                return;
            }

            var spawner = SystemAPI.GetSingleton<BulletSpawner>();
            if ((BulletSpawnStrategy)spawner.SpawnStrategy == BulletSpawnStrategy.PoolReuse)
            {
                var diagnostics = bulletPool.Diagnostics;
                SystemAPI.SetSingleton(new BulletPoolMetrics
                {
                    SpawnStrategy = spawner.SpawnStrategy,
                    CountActive = bulletPool.CountActive,
                    CountInactive = bulletPool.CountInactive,
                    CountAll = bulletPool.CountAll,
                    PeakCountActive = diagnostics.PeakCountActive,
                    PeakCountAll = diagnostics.PeakCountAll,
                    TotalCreated = diagnostics.TotalCreated,
                    TotalSpawned = diagnostics.TotalSpawned,
                    TotalDespawned = diagnostics.TotalDespawned,
                    RejectedSpawns = diagnostics.RejectedSpawns,
                    InvalidDespawns = diagnostics.InvalidDespawns
                });
                return;
            }

            SystemAPI.SetSingleton(new BulletPoolMetrics
            {
                SpawnStrategy = spawner.SpawnStrategy,
                CountActive = directActiveCount,
                CountInactive = 0,
                CountAll = directActiveCount,
                PeakCountActive = directPeakCountActive,
                PeakCountAll = directPeakCountActive,
                TotalCreated = directCreated,
                TotalSpawned = directSpawned,
                TotalDespawned = directDespawned,
                RejectedSpawns = directRejected,
                InvalidDespawns = 0
            });
        }
    }

    /// <summary>
    /// This system calculates the screen boundaries in world space.
    /// </summary>
    [UpdateInGroup(typeof(InitializationSystemGroup))]
    public partial class ScreenInfoSystem : SystemBase
    {
        protected override void OnCreate() { EntityManager.CreateEntity(typeof(ScreenInfo)); }
        protected override void OnUpdate()
        {
            var camera = Camera.main;
            if (camera == null) return;

            float3 screenMin3D = camera.ScreenToWorldPoint(new Vector3(0, 0, 10));
            float3 screenMax3D = camera.ScreenToWorldPoint(new Vector3(Screen.width, Screen.height, 10));
            var screenInfo = SystemAPI.GetSingletonRW<ScreenInfo>();
            screenInfo.ValueRW.ScreenMin = screenMin3D.xy;
            screenInfo.ValueRW.ScreenMax = screenMax3D.xy;
            if (screenInfo.ValueRO.Random.state == 0)
            {
                screenInfo.ValueRW.Random = Unity.Mathematics.Random.CreateFromIndex((uint)System.DateTime.Now.Ticks);
            }
        }
    }

    /// <summary>
    /// Moves bullets based on their velocity.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    public partial struct BulletMovementSystem : ISystem
    {
        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            float deltaTime = SystemAPI.Time.DeltaTime;
            int bulletCount = 0;
            foreach (var (transform, bullet) in SystemAPI.Query<RefRW<LocalTransform>, RefRO<BulletComponent>>())
            {
                transform.ValueRW.Position += bullet.ValueRO.Velocity * deltaTime;
                bulletCount++;
            }
        }
    }

    /// <summary>
    /// Checks bullet lifetime and tags them for despawning.
    /// </summary>
    [BurstCompile]
    [UpdateInGroup(typeof(SimulationSystemGroup))]
    [UpdateAfter(typeof(BulletMovementSystem))]
    public partial struct BulletLifetimeCheckSystem : ISystem
    {
        [BurstCompile]
        public void OnCreate(ref SystemState state)
        {
            state.RequireForUpdate<EndSimulationEntityCommandBufferSystem.Singleton>();
        }

        [BurstCompile]
        public void OnUpdate(ref SystemState state)
        {
            var ecb = SystemAPI.GetSingleton<EndSimulationEntityCommandBufferSystem.Singleton>().CreateCommandBuffer(state.WorldUnmanaged);
            foreach (var (transform, bullet, entity) in SystemAPI.Query<RefRO<LocalTransform>, RefRW<BulletComponent>>().WithEntityAccess())
            {
                bullet.ValueRW.Lifetime -= SystemAPI.Time.DeltaTime;
                if (bullet.ValueRO.Lifetime <= 0)
                {
                    ecb.AddComponent<DespawnTag>(entity);
                }
            }
        }
    }

    // /// <summary>
    // /// Debug system to help identify and fix orphaned pooled entities
    // /// </summary>
    // [UpdateInGroup(typeof(SimulationSystemGroup))]
    // public partial class BulletDebugSystem : SystemBase
    // {
    //     private double lastSummaryTime = 0;
    //     private int totalFixedCount = 0;
    //     private int lastEntityCount = 0;
    //     private int frameCounter = 0;

    //     protected override void OnCreate()
    //     {
    //         RequireForUpdate<BulletComponent>();
    //     }

    //     protected override void OnUpdate()
    //     {
    //         frameCounter++;

    //         // Only check every 10 frames to improve performance
    //         if (frameCounter % 10 != 0) return;

    //         var bulletQuery = GetEntityQuery(ComponentType.ReadOnly<BulletComponent>(), ComponentType.ReadOnly<LocalTransform>());
    //         var entities = bulletQuery.ToEntityArray(Unity.Collections.Allocator.TempJob);

    //         if (entities.Length > 0)
    //         {
    //             int fixedThisFrame = 0;
    //             int enabledCount = 0;
    //             int pooledCount = 0;

    //             for (int i = 0; i < entities.Length; i++)
    //             {
    //                 var entity = entities[i];
    //                 if (EntityManager.IsEnabled(entity)) enabledCount++;
    //                 if (EntityManager.HasComponent<PooledEntity>(entity)) pooledCount++;
    //             }

    //             // // Only log summary every 10 seconds to reduce spam
    //             // bool shouldLogSummary = SystemAPI.Time.ElapsedTime - lastSummaryTime > 10.0;
    //             // if (shouldLogSummary)
    //             // {
    //             //     Debug.Log($"BulletDebugSystem Summary: Total={entities.Length}, Enabled={enabledCount}, Pooled={pooledCount}, Fixed in last 10s={totalFixedCount}");
    //             //     lastSummaryTime = SystemAPI.Time.ElapsedTime;
    //             //     totalFixedCount = 0;
    //             // }

    //             // Check for new entities (potential orphans) - only log if significant increase
    //             if (entities.Length > lastEntityCount)
    //             {
    //                 int newEntities = entities.Length - lastEntityCount;
    //                 if (newEntities > 50) // Only log if more than 50 new entities
    //                 {
    //                     // Debug.LogWarning($"MASSIVE entity creation detected: {newEntities} new entities added (Total: {entities.Length})");
    //                 }
    //             }
    //             lastEntityCount = entities.Length;

    //             // Only check for orphaned entities, don't log every fix
    //             for (int i = 0; i < entities.Length; i++)
    //             {
    //                 var entity = entities[i];
    //                 var transform = EntityManager.GetComponentData<LocalTransform>(entity);
    //                 var isEnabled = EntityManager.IsEnabled(entity);
    //                 var hasPooledEntity = EntityManager.HasComponent<PooledEntity>(entity);

    //                 // Check if any bullet is near the center (within 1 unit of origin)
    //                 if (math.length(transform.Position.xy) < 1.0f)
    //                 {
    //                     // Fix any entity that's near center and has PooledEntity but shouldn't be active
    //                     if (hasPooledEntity && isEnabled)
    //                     {
    //                         // Move it far off-screen and disable it
    //                         var fixedTransform = transform;
    //                         fixedTransform.Position = new float3(0, 0, -1000);
    //                         fixedTransform.Scale = 0;
    //                         fixedTransform.Rotation = quaternion.identity;
    //                         EntityManager.SetComponentData(entity, fixedTransform);
    //                         EntityManager.SetEnabled(entity, false);

    //                         fixedThisFrame++;
    //                         totalFixedCount++;
    //                     }
    //                 }

    //                 // Also check for any bullet at exactly (0,0,0) - this is always an error
    //                 // But only if it's enabled and has PooledEntity (meaning it should be active)
    //                 if (transform.Position.x == 0 && transform.Position.y == 0 && transform.Position.z == 0)
    //                 {
    //                     if (isEnabled && hasPooledEntity)
    //                     {
    //                         Debug.LogError($"CRITICAL: Active bullet at origin: Entity={entity}, Enabled={isEnabled}, HasPooledEntity={hasPooledEntity}");
    //                     }
    //                     else if (isEnabled && !hasPooledEntity)
    //                     {
    //                         Debug.LogWarning($"WARNING: Non-pooled bullet at origin: Entity={entity}, Enabled={isEnabled}, HasPooledEntity={hasPooledEntity}");
    //                     }
    //                 }
    //             }

    //             // Only log if we fixed something this frame
    //             if (fixedThisFrame > 0)
    //             {
    //                 // Debug.LogWarning($"BulletDebugSystem fixed {fixedThisFrame} orphaned entities this frame");
    //             }
    //         }

    //         entities.Dispose();
    //     }
    // }

}
#endif // PRESENT_BURST && PRESENT_ECS
