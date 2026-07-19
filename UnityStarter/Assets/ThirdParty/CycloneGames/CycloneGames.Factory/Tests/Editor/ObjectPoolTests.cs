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
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(softCapacity: 4, hardCapacity: 8));

            Assert.That(pool.CountAll, Is.EqualTo(4));
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(4));
            Assert.That(pool.Diagnostics.TotalCreated, Is.EqualTo(4));
            Assert.That(pool.Profile.LifecycleState, Is.EqualTo(PoolLifecycleState.Ready));
        }

        [Test]
        public void ReferenceIdentity_AllowsDistinctValueEqualInstances()
        {
            using var pool = new ObjectPool<int, ValueEqualPoolable>(
                new ValueEqualPoolableFactory(),
                new PoolCapacitySettings(softCapacity: 2, hardCapacity: 2));

            ValueEqualPoolable first = pool.Spawn(1);
            ValueEqualPoolable second = pool.Spawn(2);

            Assert.That(first, Is.Not.SameAs(second));
            Assert.That(first.Equals(second), Is.True);
            Assert.That(pool.Contains(first), Is.True);
            Assert.That(pool.Contains(second), Is.True);
            Assert.That(pool.Despawn(first), Is.True);
            Assert.That(pool.Despawn(second), Is.True);
        }

        [Test]
        public void Despawn_RejectsForeignAndDuplicateItems()
        {
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(0, 4));
            TestPoolable item = pool.Spawn(7);

            Assert.That(pool.Despawn(new TestPoolable()), Is.False);
            Assert.That(pool.Despawn(item), Is.True);
            Assert.That(pool.Despawn(item), Is.False);
            Assert.That(pool.Diagnostics.InvalidDespawns, Is.EqualTo(2));
        }

        [Test]
        public void TrySpawn_ReturnsFalseAtHardCapacityRegardlessOfSpawnPolicy()
        {
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(0, 1, PoolOverflowPolicy.Throw));

            Assert.That(pool.TrySpawn(1, out TestPoolable first), Is.True);
            Assert.That(first, Is.Not.Null);
            Assert.That(pool.TrySpawn(2, out TestPoolable second), Is.False);
            Assert.That(second, Is.Null);
            Assert.That(pool.Diagnostics.RejectedSpawns, Is.EqualTo(1));
        }

        [Test]
        public void Spawn_ReturnsNullAtHardCapacityWhenConfigured()
        {
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(0, 1, PoolOverflowPolicy.ReturnNull));

            Assert.That(pool.Spawn(1), Is.Not.Null);
            Assert.That(pool.Spawn(2), Is.Null);
        }

        [Test]
        public void TrimOnDespawn_RespectsSoftCapacity()
        {
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(1, 8, trimPolicy: PoolTrimPolicy.TrimOnDespawn));

            TestPoolable first = pool.Spawn(1);
            TestPoolable second = pool.Spawn(2);
            TestPoolable third = pool.Spawn(3);

            pool.Despawn(first);
            pool.Despawn(second);
            pool.Despawn(third);

            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(2));
        }

        [Test]
        public void SpawnRollback_ReturnsItemOnlyWhenResetSucceeds()
        {
            var item = new TestPoolable { ThrowOnSpawn = true };
            using var pool = new ObjectPool<int, TestPoolable>(
                new SingleItemFactory<TestPoolable>(item),
                new PoolCapacitySettings(0, 1));

            Assert.Throws<InvalidOperationException>(() => pool.Spawn(99));
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.FailedSpawnRollbacks, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.QuarantinedItems, Is.Zero);
        }

        [Test]
        public void SpawnRollback_QuarantinesItemWhenResetFails()
        {
            var item = new TestPoolable
            {
                ThrowOnSpawn = true,
                ThrowOnDespawn = true,
            };
            using var pool = new ObjectPool<int, TestPoolable>(
                new SingleItemFactory<TestPoolable>(item),
                new PoolCapacitySettings(0, 1));

            Assert.Throws<AggregateException>(() => pool.Spawn(99));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(item.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.QuarantinedItems, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.CallbackFailures, Is.EqualTo(1));
        }

        [Test]
        public void DespawnFailure_QuarantinesInsteadOfRetainingItem()
        {
            var item = new TestPoolable();
            using var pool = new ObjectPool<int, TestPoolable>(
                new SingleItemFactory<TestPoolable>(item),
                new PoolCapacitySettings(0, 1));
            pool.Spawn(1);
            item.ThrowOnDespawn = true;

            Assert.Throws<InvalidOperationException>(() => pool.Despawn(item));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(item.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.QuarantinedItems, Is.EqualTo(1));
        }

        [Test]
        public void LifecycleCallback_CannotReenterPoolMutation()
        {
            var item = new TestPoolable { DespawnDuringSpawn = true };
            using var pool = new ObjectPool<int, TestPoolable>(
                new SingleItemFactory<TestPoolable>(item),
                new PoolCapacitySettings(0, 1));

            Assert.Throws<InvalidOperationException>(() => pool.Spawn(1));
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
        }

        [Test]
        public void ActiveIteration_AllowsCurrentItemToReturnItself()
        {
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(4, 4));
            pool.Spawn(1);
            pool.Spawn(2);
            pool.Spawn(3);
            pool.Spawn(4);

            pool.ForEachActive(static item => item.ReturnToPool());

            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(4));
        }

        [Test]
        public void PrewarmedSpawnDespawnLoop_DoesNotAllocateManagedMemory()
        {
            using var pool = new ObjectPool<int, TestPoolable>(
                new TestPoolableFactory(),
                new PoolCapacitySettings(1, 1));

            TestPoolable warmup = pool.Spawn(0);
            pool.Despawn(warmup);
            long before = GC.GetAllocatedBytesForCurrentThread();

            for (int i = 0; i < 4096; i++)
            {
                TestPoolable item = pool.Spawn(i);
                pool.Despawn(item);
            }

            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
            Assert.That(allocated, Is.Zero);
        }

        private sealed class TestPoolableFactory : IFactory<TestPoolable>
        {
            public TestPoolable Create()
            {
                return new TestPoolable();
            }
        }

        private sealed class ValueEqualPoolableFactory : IFactory<ValueEqualPoolable>
        {
            public ValueEqualPoolable Create()
            {
                return new ValueEqualPoolable();
            }
        }

        private sealed class SingleItemFactory<T> : IFactory<T> where T : class
        {
            private readonly T _item;

            public SingleItemFactory(T item)
            {
                _item = item;
            }

            public T Create()
            {
                return _item;
            }
        }

        private sealed class TestPoolable : IPoolable<int, TestPoolable>, IDisposable
        {
            private IDespawnableMemoryPool<TestPoolable> _pool;

            public bool ThrowOnSpawn { get; set; }
            public bool ThrowOnDespawn { get; set; }
            public bool DespawnDuringSpawn { get; set; }
            public int DisposeCalls { get; private set; }

            public void OnSpawned(int data, IDespawnableMemoryPool<TestPoolable> pool)
            {
                _pool = pool;
                if (DespawnDuringSpawn)
                {
                    pool.Despawn(this);
                }

                if (ThrowOnSpawn)
                {
                    throw new InvalidOperationException("Spawn failure.");
                }
            }

            public void OnDespawned()
            {
                if (ThrowOnDespawn)
                {
                    throw new InvalidOperationException("Despawn failure.");
                }

                _pool = null;
            }

            public void ReturnToPool()
            {
                _pool.Despawn(this);
            }

            public void Dispose()
            {
                DisposeCalls++;
                _pool = null;
            }
        }

        private sealed class ValueEqualPoolable : IPoolable<int, ValueEqualPoolable>
        {
            public void OnSpawned(int data, IDespawnableMemoryPool<ValueEqualPoolable> pool)
            {
            }

            public void OnDespawned()
            {
            }

            public override bool Equals(object obj)
            {
                return obj is ValueEqualPoolable;
            }

            public override int GetHashCode()
            {
                return 1;
            }
        }
    }
}
