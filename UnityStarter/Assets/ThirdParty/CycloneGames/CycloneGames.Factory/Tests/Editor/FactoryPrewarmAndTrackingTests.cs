using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;
using UnityEngine;
using Object = UnityEngine.Object;

namespace CycloneGames.Factory.Tests.Editor
{
    /// <summary>
    /// Locks the split between the creation budget (<c>InitialPrewarm</c>), the retention target
    /// (<c>SoftCapacity</c>) and the reserved tracking room (<c>EnsureCapacity</c>).
    /// </summary>
    public sealed class FactoryPrewarmAndTrackingTests
    {
        [Test]
        public void CapacitySettings_PrewarmCount_InheritsSoftCapacityByDefault()
        {
            var legacy = new PoolCapacitySettings(softCapacity: 12, hardCapacity: 40);
            Assert.That(legacy.InitialPrewarm, Is.EqualTo(-1));
            Assert.That(legacy.PrewarmCount, Is.EqualTo(12));

            var explicitZero = new PoolCapacitySettings(softCapacity: 12, hardCapacity: 40, initialPrewarm: 0);
            Assert.That(explicitZero.PrewarmCount, Is.Zero);

            var explicitCount = new PoolCapacitySettings(softCapacity: 0, hardCapacity: 40, initialPrewarm: 7);
            Assert.That(explicitCount.PrewarmCount, Is.EqualTo(7));
        }

        [Test]
        public void CapacitySettings_EqualitySeparatesPrewarmFromRetention()
        {
            var inherit = new PoolCapacitySettings(softCapacity: 8, hardCapacity: 16);
            var zero = new PoolCapacitySettings(softCapacity: 8, hardCapacity: 16, initialPrewarm: 0);

            Assert.That(inherit.Equals(zero), Is.False);
            Assert.That(inherit.GetHashCode(), Is.Not.EqualTo(zero.GetHashCode()));
            Assert.That(
                inherit.Equals(new PoolCapacitySettings(softCapacity: 8, hardCapacity: 16, initialPrewarm: -1)),
                Is.True);
        }

        [Test]
        public void CapacitySettings_RejectsOutOfRangePrewarm()
        {
            Assert.Throws<ArgumentOutOfRangeException>(
                () => new PoolCapacitySettings(softCapacity: 0, hardCapacity: 8, initialPrewarm: -2));
            Assert.Throws<ArgumentException>(
                () => new PoolCapacitySettings(softCapacity: 0, hardCapacity: 8, initialPrewarm: 9));
        }

        [Test]
        public void ObjectPool_PrewarmZeroWithRetention_DefersCreationAndStillRetains()
        {
            var factory = new CountingFactory();
            using var pool = new ObjectPool<int, PoolItem>(
                factory,
                new PoolCapacitySettings(softCapacity: 8, hardCapacity: 16, initialPrewarm: 0));

            Assert.That(pool.CountInactive, Is.Zero, "initialPrewarm: 0 must not create anything.");
            Assert.That(factory.CreateCalls, Is.Zero);
            Assert.That(pool.CapacitySettings.PrewarmCount, Is.Zero);
            Assert.That(pool.CapacitySettings.SoftCapacity, Is.EqualTo(8), "Retention target stays at SoftCapacity.");

            PoolItem a = pool.Spawn(1);
            PoolItem b = pool.Spawn(2);
            PoolItem c = pool.Spawn(3);
            Assert.That(factory.CreateCalls, Is.EqualTo(3), "Items are created on demand only.");

            pool.Despawn(a);
            pool.Despawn(b);
            pool.Despawn(c);

            Assert.That(pool.CountInactive, Is.EqualTo(3));
            Assert.That(pool.Diagnostics.TotalDestroyed, Is.Zero, "Returned items stay below the retention target.");
        }

        [Test]
        public void ObjectPool_PrewarmWithoutRetention_DestroysEveryReturnedItem()
        {
            var factory = new CountingFactory();
            using var pool = new ObjectPool<int, PoolItem>(
                factory,
                new PoolCapacitySettings(
                    softCapacity: 0,
                    hardCapacity: 16,
                    trimPolicy: PoolTrimPolicy.TrimOnDespawn,
                    initialPrewarm: 4));

            Assert.That(pool.CountInactive, Is.EqualTo(4), "The creation budget is independent of retention.");
            Assert.That(factory.CreateCalls, Is.EqualTo(4));

            PoolItem a = pool.Spawn(1);
            PoolItem b = pool.Spawn(2);
            PoolItem c = pool.Spawn(3);
            PoolItem d = pool.Spawn(4);
            pool.Despawn(a);
            pool.Despawn(b);
            pool.Despawn(c);
            pool.Despawn(d);

            Assert.That(pool.CountInactive, Is.Zero, "A zero retention target must not keep returned items.");
            Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(4));
        }

        [Test]
        public void MonoFastPool_ConstructionCreatesExactlyInitialPrewarm()
        {
            var prefab = new GameObject("PrewarmProbePrefab");
            var spawner = new CountingSpawner();
            MonoFastPool<Transform> deferred = null;
            MonoFastPool<Transform> legacy = null;
            try
            {
                deferred = new MonoFastPool<Transform>(
                    spawner,
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 64, hardCapacity: -1, initialPrewarm: 0));

                Assert.That(spawner.CreateCalls, Is.Zero, "Construction must not instantiate when prewarm is 0.");
                Assert.That(deferred.CountInactive, Is.Zero);

                int created = deferred.WarmupStep(10);
                Assert.That(created, Is.EqualTo(10));
                Assert.That(spawner.CreateCalls, Is.EqualTo(10));
                Assert.That(deferred.CountInactive, Is.EqualTo(10));

                legacy = new MonoFastPool<Transform>(
                    spawner,
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 5, hardCapacity: -1));

                Assert.That(spawner.CreateCalls, Is.EqualTo(15), "The legacy path must still prewarm SoftCapacity.");
                Assert.That(legacy.CountInactive, Is.EqualTo(5));
            }
            finally
            {
                deferred?.Dispose();
                legacy?.Dispose();
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void MonoFastPool_DisposeOutsidePlayMode_DestroysInstancesImmediately()
        {
            var prefab = new GameObject("DisposeProbePrefab");
            try
            {
                var pool = new MonoFastPool<Transform>(
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 2, hardCapacity: 4));
                Transform first = pool.Spawn();
                Transform second = pool.Spawn();

                pool.Dispose();

                Assert.That(first == null, Is.True, "Edit Mode dispose must destroy instead of deferring.");
                Assert.That(second == null, Is.True, "Edit Mode dispose must destroy instead of deferring.");
                Assert.That(pool.CountAll, Is.Zero);
            }
            finally
            {
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void MonoFastPool_DisposeWithExternallyDestroyedActive_ReclaimsWithoutThrowing()
        {
            var prefab = new GameObject("DisposeDestroyedPrefab");
            MonoFastPool<Transform> pool = null;
            try
            {
                pool = new MonoFastPool<Transform>(
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4),
                    autoSetActive: false);
                Transform alive = pool.Spawn();
                Transform dead = pool.Spawn();
                UnityEngine.Object.DestroyImmediate(dead.gameObject);

                Assert.DoesNotThrow(() => pool.Dispose());

                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.TrackedItemCount, Is.Zero);
                Assert.That(pool.Diagnostics.InvalidatedActiveItems, Is.EqualTo(1));
                Assert.That(pool.Diagnostics.CallbackFailures, Is.Zero);
                Assert.That(alive == null, Is.True, "Dispose must still release the healthy lease.");
            }
            finally
            {
                if (prefab != null)
                {
                    Object.DestroyImmediate(prefab);
                }
            }
        }

        [Test]
        public void MonoFastPool_SpawnerReturnsDestroyedInstance_RejectsBeforeFieldAccess()
        {
            var prefab = new GameObject("DeadSpawnerPrefab");
            Transform dead = null;
            MonoFastPool<Transform> pool = null;
            try
            {
                dead = Object.Instantiate(prefab.transform);
                Object.DestroyImmediate(dead.gameObject);

                pool = new MonoFastPool<Transform>(
                    new DestroyedInstanceSpawner(dead),
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4));

                // A destroyed instance compares equal to null through UnityEngine.Object's operator, so the
                // adapter must reject it instead of dereferencing gameObject.
                Assert.Throws<InvalidOperationException>(() => pool.Spawn());
                Assert.That(pool.CountAll, Is.Zero);
            }
            finally
            {
                pool?.Dispose();
                if (prefab != null)
                {
                    Object.DestroyImmediate(prefab);
                }
            }
        }

        [Test]
        public void MonoObjectPool_ExternallyDestroyedActive_ReclaimsWithoutRunningReset()
        {
            using var pool = new MonoObjectPool<int, PooledProbe>(
                new ProbeFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4));

            PooledProbe item = pool.Spawn(7);
            Assert.That(item.LastPayload, Is.EqualTo(7));
            UnityEngine.Object.DestroyImmediate(item);

            bool despawned = false;
            Assert.DoesNotThrow(() => despawned = pool.Despawn(item));

            Assert.That(despawned, Is.True);
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.Diagnostics.InvalidatedActiveItems, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.CallbackFailures, Is.Zero,
                "Reset must not run on a destroyed instance.");
            Assert.That(item.DespawnCalls, Is.Zero);
        }

        [Test]
        public void MonoObjectPool_ExternallyDestroyedInactive_IsNotHandedOut()
        {
            using var pool = new MonoObjectPool<int, PooledProbe>(
                new ProbeFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4));

            pool.Prewarm(1);
            PooledProbe stale = pool.Spawn(1);
            pool.Despawn(stale);
            UnityEngine.Object.DestroyImmediate(stale);

            PooledProbe replacement = pool.Spawn(2);

            Assert.That(replacement, Is.Not.Null);
            Assert.That(ReferenceEquals(replacement, stale), Is.False,
                "A destroyed inactive instance must never be handed out again.");
            Assert.That(replacement.LastPayload, Is.EqualTo(2));
            Assert.That(pool.Diagnostics.InvalidatedInactiveItems, Is.EqualTo(1));
            Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
        }

        [Test]
        public void DespawnAll_ContinuesAfterFailingReset_AndAggregates()
        {
            using var pool = new ObjectPool<int, PoolItem>(
                new CountingFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4));

            pool.Spawn(1);
            PoolItem failing = pool.Spawn(2);
            pool.Spawn(3);
            failing.ThrowOnDespawn = true;

            Assert.Throws<AggregateException>(() => pool.DespawnAll());

            Assert.That(pool.CountActive, Is.Zero, "A failing reset must not strand the remaining leases.");
            Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
            Assert.That(pool.Diagnostics.QuarantinedItems, Is.EqualTo(1));
        }

        [Test]
        public void DespawnStep_ProcessesWholeBatchDespiteFailingReset()
        {
            using var pool = new ObjectPool<int, PoolItem>(
                new CountingFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4));

            pool.Spawn(1);
            PoolItem failing = pool.Spawn(2);
            pool.Spawn(3);
            failing.ThrowOnDespawn = true;

            Assert.Throws<AggregateException>(() => pool.DespawnStep(3));

            Assert.That(pool.CountActive, Is.Zero, "A bounded batch must still process the remaining items.");
        }

        [Test]
        public void EnsureCapacity_RemovesTrackingGrowthAllocations()
        {
            var factory = new CountingFactory();
            using var pool = new ObjectPool<int, PoolItem>(
                factory,
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 600));

            pool.EnsureCapacity(512);
            int reserved = pool.TrackingCapacity;
            Assert.That(reserved, Is.GreaterThanOrEqualTo(512));

            var buffer = new PoolItem[512];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = pool.Spawn(i);
            }

            // TrackingCapacity can only stay at its reserved value when no container had to grow.
            Assert.That(pool.TrackingCapacity, Is.EqualTo(reserved));
            Assert.That(pool.CountActive, Is.EqualTo(512));

            for (int i = 0; i < buffer.Length; i++)
            {
                pool.Despawn(buffer[i]);
            }

            Assert.That(pool.TrackingCapacity, Is.EqualTo(reserved));
            Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
        }

        [Test]
        public void TrimInactive_ToEmpty_ReleasesTrackingStorage()
        {
            using var pool = new ObjectPool<int, PoolItem>(
                new CountingFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: -1));

            pool.EnsureCapacity(512);
            Assert.That(pool.TrackingCapacity, Is.GreaterThanOrEqualTo(512));

            var buffer = new PoolItem[512];
            for (int i = 0; i < buffer.Length; i++)
            {
                buffer[i] = pool.Spawn(i);
            }

            for (int i = 0; i < buffer.Length; i++)
            {
                pool.Despawn(buffer[i]);
            }

            pool.TrimInactive(0);

            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.TrackingCapacity, Is.Zero, "An idle pool must not keep peak tracking storage.");
        }

        [Test]
        public void Interface_ExposesCapacityReservationAndDispose()
        {
            using IMemoryPool pool = new ObjectPool<int, PoolItem>(
                new CountingFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 8));

            pool.EnsureCapacity(8);
            Assert.That(pool.TrackingCapacity, Is.GreaterThanOrEqualTo(8));
            Assert.That(pool.CountAll, Is.Zero);
        }

        [Test]
        public void EnsureCapacity_ClampsToHardCapacity()
        {
            using var pool = new ObjectPool<int, PoolItem>(
                new CountingFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 16));

            pool.EnsureCapacity(4096);
            Assert.That(pool.TrackingCapacity, Is.LessThan(4096));
            Assert.That(pool.CountAll, Is.Zero, "Reserving tracking room must not create items.");
        }

        [Test]
        public void SpawnRollback_KeepsSpawnDespawnActiveInvariant()
        {
            var item = new PoolItem { ThrowOnSpawn = true };
            using var pool = new ObjectPool<int, PoolItem>(
                new SingleItemFactory(item),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 2));

            Assert.Throws<InvalidOperationException>(() => pool.Spawn(1));

            PoolDiagnostics diagnostics = pool.Diagnostics;
            Assert.That(diagnostics.FailedSpawnRollbacks, Is.EqualTo(1));
            Assert.That(diagnostics.TotalSpawned, Is.Zero, "A rolled-back borrow is not a spawn.");
            Assert.That(diagnostics.TotalDespawned, Is.Zero);
            Assert.That(diagnostics.TotalSpawned - diagnostics.TotalDespawned, Is.EqualTo(pool.CountActive));
        }

        [Test]
        public void MonoFastPool_ExternallyDestroyedActive_ReclaimsLeaseWithoutCallbackFailure()
        {
            var prefab = new GameObject("DestroyedActivePrefab");
            MonoFastPool<Transform> pool = null;
            try
            {
                pool = new MonoFastPool<Transform>(
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 0, hardCapacity: 4),
                    autoSetActive: true);
                Transform instance = pool.Spawn();
                UnityEngine.Object.DestroyImmediate(instance.gameObject);

                Assert.That(instance == null, Is.True, "Unity must expose the destroyed instance as fake-null.");
                bool despawned = false;
                Assert.DoesNotThrow(() => despawned = pool.Despawn(instance));

                Assert.That(despawned, Is.True, "The lease must still end.");
                Assert.That(pool.CountActive, Is.Zero);
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.TrackedItemCount, Is.Zero);
                Assert.That(pool.Diagnostics.CallbackFailures, Is.Zero, "No reset callback should have run.");
                Assert.That(pool.Diagnostics.InvalidatedActiveItems, Is.EqualTo(1));
            }
            finally
            {
                pool?.Dispose();
                Object.DestroyImmediate(prefab);
            }
        }

        [Test]
        public void FastObjectPool_BaseConstructorNeverCreatesItems()
        {
            // StatefulPool.CreateNew reads a field assigned in its own constructor. A base constructor
            // that prewarmed would call it too early and fail.
            using var pool = new StatefulPool();
            Assert.That(pool.CountInactive, Is.Zero);

            pool.Prewarm(3);
            Assert.That(pool.CountInactive, Is.EqualTo(3));
            Assert.That(pool.Spawn(), Is.Not.Null);
        }

        [Test]
        public void OwnershipValidationDisabled_StillRejectsDuplicateActiveInstance()
        {
            var item = new PoolItem();
            using var pool = new ObjectPool<int, PoolItem>(
                new SingleItemFactory(item),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: -1),
                validateUniqueOwnership: false);

            Assert.That(pool.Spawn(1), Is.SameAs(item));
            Assert.Throws<InvalidOperationException>(() => pool.Spawn(2));
        }

        [Test]
        public void EnsureCapacity_AfterDispose_Throws()
        {
            var pool = new ObjectPool<int, PoolItem>(
                new CountingFactory(),
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: 8));
            pool.Dispose();

            Assert.Throws<ObjectDisposedException>(() => pool.EnsureCapacity(4));
        }

        private sealed class StatefulPool : FastObjectPool<PoolItem>
        {
            private readonly PoolItem[] _items = new PoolItem[4];
            private int _next;

            public StatefulPool()
                : base(new PoolCapacitySettings(softCapacity: 4, hardCapacity: 4))
            {
                for (int i = 0; i < _items.Length; i++)
                {
                    _items[i] = new PoolItem();
                }
            }

            protected override PoolItem CreateNew()
            {
                return _items[_next++];
            }

            protected override void OnSpawn(PoolItem item)
            {
            }

            protected override void OnDespawn(PoolItem item)
            {
            }
        }

        private sealed class CountingFactory : IFactory<PoolItem>
        {
            public int CreateCalls { get; private set; }

            public PoolItem Create()
            {
                CreateCalls++;
                return new PoolItem();
            }
        }

        private sealed class ProbeFactory : IFactory<PooledProbe>
        {
            public PooledProbe Create()
            {
                return ScriptableObject.CreateInstance<PooledProbe>();
            }
        }

        private sealed class SingleItemFactory : IFactory<PoolItem>
        {
            private readonly PoolItem _item;

            public SingleItemFactory(PoolItem item)
            {
                _item = item;
            }

            public PoolItem Create()
            {
                return _item;
            }
        }

        private sealed class CountingSpawner : IUnityObjectSpawner
        {
            public int CreateCalls { get; private set; }

            public T Create<T>(T origin) where T : Object
            {
                CreateCalls++;
                return Object.Instantiate(origin);
            }

            public T Create<T>(T origin, Transform parent) where T : Object
            {
                CreateCalls++;
                return Object.Instantiate(origin, parent);
            }
        }

        /// <summary>Returns an already destroyed instance, the shape a misbehaving custom spawner can produce.</summary>
        private sealed class DestroyedInstanceSpawner : IUnityObjectSpawner
        {
            private readonly Object _dead;

            public DestroyedInstanceSpawner(Object dead)
            {
                _dead = dead;
            }

            public T Create<T>(T origin) where T : Object
            {
                return (T)_dead;
            }

            public T Create<T>(T origin, Transform parent) where T : Object
            {
                return (T)_dead;
            }
        }

        private sealed class PoolItem : IPoolable<int, PoolItem>
        {
            public bool ThrowOnSpawn { get; set; }

            public bool ThrowOnDespawn { get; set; }

            public void OnSpawned(int data, IDespawnableMemoryPool<PoolItem> pool)
            {
                if (ThrowOnSpawn)
                {
                    throw new InvalidOperationException("Spawn failure.");
                }
            }

            public void OnDespawned()
            {
                if (ThrowOnDespawn)
                {
                    // One-shot so a retry by the pool cannot loop forever.
                    ThrowOnDespawn = false;
                    throw new InvalidOperationException("Despawn failure.");
                }
            }
        }
    }
}
