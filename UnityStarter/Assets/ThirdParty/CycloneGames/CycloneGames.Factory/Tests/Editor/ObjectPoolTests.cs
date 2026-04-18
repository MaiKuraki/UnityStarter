using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Editor
{
    public sealed class ObjectPoolTests
    {
        [Test]
        public void Prewarm_UsesSoftCapacityAndTracksCounts()
        {
            var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(softCapacity: 4, hardCapacity: 8));

            Assert.That(pool.CountAll, Is.EqualTo(4));
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(4));
            Assert.That(pool.Diagnostics.TotalCreated, Is.EqualTo(4));
            Assert.That(pool.Profile.CountAll, Is.EqualTo(4));
            Assert.That(pool.Profile.CapacitySettings.SoftCapacity, Is.EqualTo(4));
        }

        [Test]
        public void Despawn_RejectsForeignAndDuplicateItems()
        {
            var pool = new ObjectPool<int, TestPoolable>(new TestPoolableFactory(), new PoolCapacitySettings(0, 4));
            var item = pool.Spawn(7);

            Assert.That(pool.Despawn(new TestPoolable()), Is.False);
            Assert.That(pool.Despawn(item), Is.True);
            Assert.That(pool.Despawn(item), Is.False);
            Assert.That(pool.Diagnostics.InvalidDespawns, Is.EqualTo(2));
        }

        [Test]
        public void TrySpawn_ReturnsFalseWhenHardCapacityReachedAndPolicyAllowsGracefulFailure()
        {
            var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 1, overflowPolicy: PoolOverflowPolicy.ReturnNull));

            Assert.That(pool.TrySpawn(1, out var first), Is.True);
            Assert.That(first, Is.Not.Null);
            Assert.That(pool.TrySpawn(2, out var second), Is.False);
            Assert.That(second, Is.Null);
            Assert.That(pool.Diagnostics.RejectedSpawns, Is.EqualTo(1));
        }

        [Test]
        public void TrimOnDespawn_RespectsSoftCapacity()
        {
            var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(softCapacity: 1, hardCapacity: 8, trimPolicy: PoolTrimPolicy.TrimOnDespawn));

            var a = pool.Spawn(1);
            var b = pool.Spawn(2);
            var c = pool.Spawn(3);

            Assert.That(pool.Despawn(a), Is.True);
            Assert.That(pool.Despawn(b), Is.True);
            Assert.That(pool.Despawn(c), Is.True);

            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.DestroyedOnTrim, Is.EqualTo(2));
        }

        [Test]
        public void SpawnRollback_ResetsItemAndIncrementsDiagnostics()
        {
            var pool = new ObjectPool<int, TestPoolable>(new TestPoolableFactory(), new PoolCapacitySettings(0, 4));
            TestPoolable.ThrowOnSpawnParam = 99;

            try
            {
                Assert.Throws<InvalidOperationException>(() => pool.Spawn(99));
                Assert.That(pool.CountActive, Is.Zero);
                Assert.That(pool.CountInactive, Is.EqualTo(1));
                Assert.That(pool.Diagnostics.FailedSpawnRollbacks, Is.EqualTo(1));
                Assert.That(pool.Diagnostics.TotalDespawned, Is.Zero);
            }
            finally
            {
                TestPoolable.ThrowOnSpawnParam = null;
            }
        }

        private sealed class TestPoolableFactory : IFactory<TestPoolable>
        {
            public TestPoolable Create()
            {
                return new TestPoolable();
            }
        }

        private sealed class TestPoolable : IPoolable<int, TestPoolable>, IDisposable
        {
            public static int? ThrowOnSpawnParam { get; set; }

            public int LastSpawnParam { get; private set; }
            public IDespawnableMemoryPool<TestPoolable> Pool { get; private set; }
            public int DespawnCalls { get; private set; }

            public void OnSpawned(int data, IDespawnableMemoryPool<TestPoolable> pool)
            {
                if (ThrowOnSpawnParam.HasValue && ThrowOnSpawnParam.Value == data)
                {
                    throw new InvalidOperationException("Spawn failure for rollback test.");
                }

                LastSpawnParam = data;
                Pool = pool;
            }

            public void OnDespawned()
            {
                DespawnCalls++;
                LastSpawnParam = 0;
                Pool = null;
            }

            public void Dispose()
            {
            }
        }
    }
}
