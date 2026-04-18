using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Editor
{
    public sealed class FastObjectPoolTests
    {
        [Test]
        public void FastPool_TracksOwnershipAndTrimPolicy()
        {
            var pool = new TestFastPool(new PoolCapacitySettings(softCapacity: 1, hardCapacity: 4, trimPolicy: PoolTrimPolicy.TrimOnDespawn));

            var a = pool.Spawn();
            var b = pool.Spawn();

            Assert.That(pool.Contains(a), Is.True);
            Assert.That(pool.Despawn(a), Is.True);
            Assert.That(pool.Despawn(b), Is.True);
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.Diagnostics.DestroyedOnTrim, Is.EqualTo(1));
            Assert.That(pool.Despawn(b), Is.False);
            Assert.That(pool.Diagnostics.InvalidDespawns, Is.EqualTo(1));
        }

        [Test]
        public void FastPool_TrySpawnReturnsFalseWhenAtHardLimit()
        {
            var pool = new TestFastPool(new PoolCapacitySettings(softCapacity: 0, hardCapacity: 1, overflowPolicy: PoolOverflowPolicy.ReturnNull));

            Assert.That(pool.TrySpawn(out var first), Is.True);
            Assert.That(first, Is.Not.Null);
            Assert.That(pool.TrySpawn(out var second), Is.False);
            Assert.That(second, Is.Null);
            Assert.That(pool.Diagnostics.RejectedSpawns, Is.EqualTo(1));
        }

        private sealed class TestFastPool : FastObjectPool<TestFastItem>
        {
            public TestFastPool(PoolCapacitySettings capacitySettings)
                : base(capacitySettings)
            {
            }

            protected override TestFastItem CreateNew()
            {
                return new TestFastItem();
            }

            protected override void OnSpawn(TestFastItem item)
            {
                item.IsActive = true;
            }

            protected override void OnDespawn(TestFastItem item)
            {
                item.IsActive = false;
            }
        }

        private sealed class TestFastItem : IDisposable
        {
            public bool IsActive { get; set; }

            public void Dispose()
            {
            }
        }
    }
}
