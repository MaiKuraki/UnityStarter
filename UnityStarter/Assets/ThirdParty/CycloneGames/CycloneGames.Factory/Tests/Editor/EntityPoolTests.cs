#if PRESENT_BURST && PRESENT_ECS
using CycloneGames.Factory.ECS.Runtime;
using NUnit.Framework;
using Unity.Entities;
using Unity.Mathematics;
using Unity.Transforms;

namespace CycloneGames.Factory.Tests.Editor
{
    public sealed class EntityPoolTests
    {
        private World _world;
        private EntityManager _entityManager;

        [SetUp]
        public void SetUp()
        {
            _world = new World("CycloneGames.Factory.Tests");
            _entityManager = _world.EntityManager;
        }

        [TearDown]
        public void TearDown()
        {
            _world.Dispose();
        }

        [Test]
        public void InitialCapacity_CreatesDisabledPooledEntities()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 2);

            Assert.That(pool.GetActiveEntities().Count, Is.Zero);

            var query = _entityManager.CreateEntityQuery(ComponentType.ReadOnly<TestData>(), ComponentType.ReadOnly<PooledEntity>());
            using var entities = query.ToEntityArray(Unity.Collections.Allocator.Temp);
            Assert.That(entities.Length, Is.EqualTo(2));
            Assert.That(_entityManager.IsEnabled(entities[0]), Is.False);
            Assert.That(_entityManager.IsEnabled(entities[1]), Is.False);
        }

        [Test]
        public void Spawn_ReusesInactiveEntity_EnablesIt_AndUpdatesData()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 1);
            var entity = pool.Spawn(new TestData { Value = 123 });

            Assert.That(_entityManager.Exists(entity), Is.True);
            Assert.That(_entityManager.IsEnabled(entity), Is.True);
            Assert.That(_entityManager.GetComponentData<TestData>(entity).Value, Is.EqualTo(123));
            Assert.That(pool.GetActiveEntities().Count, Is.EqualTo(1));
        }

        [Test]
        public void Despawn_WithECB_DisablesEntity_ResetsTransform_AndPreventsDuplicateReturn()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 1);
            var entity = pool.Spawn(new TestData { Value = 9 });

            _entityManager.SetComponentData(entity, LocalTransform.FromPosition(new float3(3f, 4f, 5f)));
            using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            pool.Despawn(entity, ecb);
            pool.Despawn(entity, ecb);
            ecb.Playback(_entityManager);

            Assert.That(_entityManager.IsEnabled(entity), Is.False);
            var transform = _entityManager.GetComponentData<LocalTransform>(entity);
            Assert.That(transform.Position.x, Is.Zero);
            Assert.That(transform.Position.y, Is.Zero);
            Assert.That(transform.Position.z, Is.Zero);
            Assert.That(transform.Scale, Is.Zero);
            Assert.That(pool.GetActiveEntities().Count, Is.Zero);
        }

        [Test]
        public void Spawn_WithECB_ReusesPrewarmedEntity_EnablesIt_AndUpdatesData()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 1);
            Assert.That(pool.CountInactive, Is.EqualTo(1));

            using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            var entity = pool.Spawn(ecb, new TestData { Value = 77 });
            ecb.Playback(_entityManager);

            Assert.That(_entityManager.Exists(entity), Is.True);
            Assert.That(_entityManager.HasComponent<PooledEntity>(entity), Is.True);
            Assert.That(_entityManager.IsEnabled(entity), Is.True);
            Assert.That(_entityManager.GetComponentData<TestData>(entity).Value, Is.EqualTo(77));
        }

        [Test]
        public void Spawn_WithECB_RejectsWhenPoolIsEmpty()
        {
            var pool = new EntityPool<TestData>(
                _entityManager,
                new TestEntityFactory(_entityManager),
                default,
                new EntityPoolCapacitySettings(0, -1, EntityPoolOverflowPolicy.ReturnNull));

            using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            Assert.That(pool.TrySpawn(ecb, new TestData { Value = 99 }, out var entity), Is.False);
            Assert.That(entity, Is.EqualTo(Entity.Null));
            Assert.That(pool.Diagnostics.RejectedSpawns, Is.EqualTo(1));
        }

        [Test]
        public void DespawnAll_MovesAllActiveEntitiesBackToInactive()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 0);
            var a = pool.Spawn(new TestData { Value = 1 });
            var b = pool.Spawn(new TestData { Value = 2 });

            using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            pool.DespawnAll(ecb);
            ecb.Playback(_entityManager);

            Assert.That(pool.GetActiveEntities().Count, Is.Zero);
            Assert.That(_entityManager.IsEnabled(a), Is.False);
            Assert.That(_entityManager.IsEnabled(b), Is.False);
        }

        [Test]
        public void EmptyPool_HighFrequencySpawnDespawnAllLoop_RemainsStable()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 0);

            for (int cycle = 0; cycle < 5; cycle++)
            {
                for (int i = 0; i < 32; i++)
                {
                    pool.Spawn(new TestData { Value = cycle * 100 + i });
                }

                Assert.That(pool.GetActiveEntities().Count, Is.EqualTo(32));

                using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
                pool.DespawnAll(ecb);
                ecb.Playback(_entityManager);

                Assert.That(pool.GetActiveEntities().Count, Is.Zero);
            }
        }

        [Test]
        public void SpawnWithDelayedPlayback_TracksActiveSetUntilECBPlaybackCompletes()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 1);
            Assert.That(pool.CountInactive, Is.EqualTo(1));

            using var spawnEcb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            var entity = pool.Spawn(spawnEcb, new TestData { Value = 55 });
            Assert.That(pool.GetActiveEntities().Count, Is.EqualTo(1));
            Assert.That(_entityManager.Exists(entity), Is.True); // reused entity already exists

            spawnEcb.Playback(_entityManager);
            Assert.That(_entityManager.Exists(entity), Is.True);
            Assert.That(_entityManager.IsEnabled(entity), Is.True);

            using var despawnEcb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            pool.Despawn(entity, despawnEcb);
            Assert.That(pool.GetActiveEntities().Count, Is.Zero);
            despawnEcb.Playback(_entityManager);
            Assert.That(_entityManager.IsEnabled(entity), Is.False);
        }

        [Test]
        public void Despawn_IgnoresInvalidOrForeignEntities()
        {
            var pool = new EntityPool<TestData>(_entityManager, new TestEntityFactory(_entityManager), default, 0);
            var activeEntity = pool.Spawn(new TestData { Value = 1 });
            var foreignEntity = _entityManager.CreateEntity(typeof(TestData));
            _entityManager.SetComponentData(foreignEntity, new TestData { Value = 999 });

            using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            pool.Despawn(Entity.Null, ecb);
            pool.Despawn(foreignEntity, ecb);
            pool.Despawn(activeEntity, ecb);
            ecb.Playback(_entityManager);

            Assert.That(pool.GetActiveEntities().Count, Is.Zero);
            Assert.That(_entityManager.Exists(foreignEntity), Is.True);
            Assert.That(_entityManager.HasComponent<PooledEntity>(foreignEntity), Is.False);
        }

        [Test]
        public void CapacitySettings_RejectSpawnWhenHardLimitIsReached()
        {
            var pool = new EntityPool<TestData>(
                _entityManager,
                new TestEntityFactory(_entityManager),
                default,
                new EntityPoolCapacitySettings(softCapacity: 0, hardCapacity: 1, overflowPolicy: EntityPoolOverflowPolicy.ReturnNull));

            Assert.That(pool.TrySpawn(new TestData { Value = 1 }, out var first), Is.True);
            Assert.That(first, Is.Not.EqualTo(Entity.Null));
            Assert.That(pool.TrySpawn(new TestData { Value = 2 }, out var second), Is.False);
            Assert.That(second, Is.EqualTo(Entity.Null));
            Assert.That(pool.Diagnostics.RejectedSpawns, Is.EqualTo(1));
        }

        [Test]
        public void Prewarm_AndDiagnostics_TrackEntityCounts()
        {
            var pool = new EntityPool<TestData>(
                _entityManager,
                new TestEntityFactory(_entityManager),
                new TestData { Value = 8 },
                new EntityPoolCapacitySettings(softCapacity: 3, hardCapacity: 8));

            Assert.That(pool.CountInactive, Is.EqualTo(3));
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.Diagnostics.TotalCreated, Is.EqualTo(3));
            Assert.That(pool.Diagnostics.PeakCountActive, Is.Zero);
            Assert.That(pool.Diagnostics.PeakCountAll, Is.EqualTo(3));

            var entity = pool.Spawn(new TestData { Value = 42 });
            using var ecb = new EntityCommandBuffer(Unity.Collections.Allocator.Temp);
            pool.Despawn(entity, ecb);
            ecb.Playback(_entityManager);

            Assert.That(pool.Diagnostics.TotalSpawned, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.TotalDespawned, Is.EqualTo(1));
            Assert.That(pool.Profile.CountAll, Is.EqualTo(pool.CountAll));
            Assert.That(pool.Profile.CapacitySettings.HardCapacity, Is.EqualTo(8));
            Assert.That(pool.Profile.Diagnostics.PeakCountActive, Is.EqualTo(1));
        }

        private struct TestData : IComponentData
        {
            public int Value;
        }

        private sealed class TestEntityFactory : IEntityFactory<TestData>
        {
            private readonly EntityManager _entityManager;

            public TestEntityFactory(EntityManager entityManager)
            {
                _entityManager = entityManager;
            }

            public Entity Create(TestData component)
            {
                var entity = _entityManager.CreateEntity(typeof(TestData), typeof(LocalTransform));
                _entityManager.SetComponentData(entity, component);
                _entityManager.SetComponentData(entity, LocalTransform.FromPosition(new float3(0f, 0f, 0f)));
                return entity;
            }

            public Entity Create(EntityCommandBuffer ecb, TestData component)
            {
                var entity = ecb.CreateEntity();
                ecb.AddComponent(entity, component);
                ecb.AddComponent(entity, LocalTransform.FromPosition(new float3(0f, 0f, 0f)));
                return entity;
            }
        }
    }
}
#endif
