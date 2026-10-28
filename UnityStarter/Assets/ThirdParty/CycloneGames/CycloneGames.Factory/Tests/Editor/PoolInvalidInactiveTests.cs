using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;
using UnityEngine;

namespace CycloneGames.Factory.Tests.Editor
{
    public sealed class PoolInvalidInactiveTests
    {
        private const int Iterations = 50;

        [Test]
        public void Spawn_InvalidInactive_ReleasesAbandonedOwnership()
        {
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                using var pool = new TestPool(4);
                InvalidItem abandoned = CreateInvalidInactive(pool);
                InvalidItem replacement = pool.Spawn();

                Assert.That(replacement, Is.Not.Null);
                Assert.That(abandoned.DisposeCalls, Is.EqualTo(1));
                Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
                Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(1));
            }
        }

        [Test]
        public void TrimInactive_InvalidInactive_ReleasesAbandonedOwnership()
        {
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                using var pool = new TestPool(4);
                InvalidItem abandoned = CreateInvalidInactive(pool);
                pool.TrimInactiveStep(0, 1);

                Assert.That(abandoned.DisposeCalls, Is.EqualTo(1));
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.TrackedItemCount, Is.Zero);
                Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(1));
            }
        }

        [Test]
        public void Clear_InvalidInactive_ReleasesAbandonedOwnership()
        {
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                using var pool = new TestPool(4);
                InvalidItem abandoned = CreateInvalidInactive(pool);
                pool.Clear();

                Assert.That(abandoned.DisposeCalls, Is.EqualTo(1));
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.TrackedItemCount, Is.Zero);
                Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(1));
            }
        }

        [Test]
        public void Dispose_InvalidInactive_ReleasesAbandonedOwnership()
        {
            for (int iteration = 0; iteration < Iterations; iteration++)
            {
                var pool = new TestPool(4);
                InvalidItem abandoned = CreateInvalidInactive(pool);
                pool.Dispose();

                Assert.That(abandoned.DisposeCalls, Is.EqualTo(1));
                Assert.That(pool.CountAll, Is.Zero);
                Assert.That(pool.TrackedItemCount, Is.Zero);
                Assert.That(pool.Diagnostics.TotalDestroyed, Is.EqualTo(1));
            }
        }

        [Test]
        public void Clear_ValidationException_AttemptsCleanupAndPreservesFailure()
        {
            using var pool = new TestPool(4);
            InvalidItem abandoned = CreateInvalidInactive(pool);
            abandoned.ThrowOnValidation = true;

            Assert.Throws<AggregateException>(() => pool.Clear());
            Assert.That(abandoned.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.TrackedItemCount, Is.Zero);
            Assert.That(pool.Diagnostics.InvalidatedInactiveItems, Is.EqualTo(1));
        }

        [Test]
        public void TrimInactive_DestroyFailure_KeepsOwnershipForSafety()
        {
            using var pool = new TestPool(4);
            InvalidItem abandoned = CreateInvalidInactive(pool);
            abandoned.ThrowOnDispose = true;

            Assert.Throws<AggregateException>(() => pool.TrimInactiveStep(0, 1));
            Assert.That(abandoned.DisposeCalls, Is.EqualTo(1));
            Assert.That(pool.CountAll, Is.Zero);
            Assert.That(pool.TrackedItemCount, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.CallbackFailures, Is.EqualTo(1));
        }

        [Test]
        public void MonoFastPool_ExternallyDestroyedInactive_ReleasesOwnershipOnSpawn()
        {
            var prefab = new GameObject("InvalidInactivePrefab");
            MonoFastPool<Transform> pool = null;
            Transform abandoned = null;
            Transform replacement = null;
            try
            {
                pool = new MonoFastPool<Transform>(
                    prefab.transform,
                    new PoolCapacitySettings(softCapacity: 0, hardCapacity: 2),
                    autoSetActive: false);
                pool.Prewarm(1);
                abandoned = pool.Spawn();
                pool.Despawn(abandoned);
                UnityEngine.Object.DestroyImmediate(abandoned.gameObject);

                Assert.That(abandoned == null, Is.True, "Unity should expose the destroyed instance as fake-null.");
                replacement = pool.Spawn();
                Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
                Assert.That(pool.Diagnostics.InvalidatedInactiveItems, Is.EqualTo(1));
            }
            finally
            {
                if (replacement != null)
                {
                    UnityEngine.Object.DestroyImmediate(replacement.gameObject);
                }

                if (abandoned != null)
                {
                    UnityEngine.Object.DestroyImmediate(abandoned.gameObject);
                }

                if (prefab != null)
                {
                    UnityEngine.Object.DestroyImmediate(prefab);
                }
            }
        }

        private static InvalidItem CreateInvalidInactive(TestPool pool)
        {
            pool.Prewarm(1);
            InvalidItem item = pool.Spawn();
            pool.Despawn(item);
            item.Valid = false;
            return item;
        }

        private sealed class TestPool : FastObjectPool<InvalidItem>
        {
            public TestPool(int hardCapacity)
                : base(
                    new PoolCapacitySettings(softCapacity: hardCapacity, hardCapacity: hardCapacity),
                    deferInitialPrewarm: true)
            {
            }

            protected override InvalidItem CreateNew()
            {
                return new InvalidItem();
            }

            protected override void OnSpawn(InvalidItem item)
            {
            }

            protected override void OnDespawn(InvalidItem item)
            {
            }

            protected override bool IsValid(InvalidItem item)
            {
                if (item != null && item.ThrowOnValidation)
                {
                    throw new InvalidOperationException("Validation failed.");
                }

                return item != null && item.Valid;
            }
        }

        private sealed class InvalidItem : IDisposable
        {
            public bool Valid { get; set; } = true;
            public bool ThrowOnValidation { get; set; }
            public bool ThrowOnDispose { get; set; }
            public int DisposeCalls { get; private set; }

            public void Dispose()
            {
                DisposeCalls++;
                if (ThrowOnDispose)
                {
                    throw new InvalidOperationException("Destroy failed.");
                }
            }
        }
    }
}
