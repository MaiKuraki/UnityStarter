using System;
using CycloneGames.Factory.Runtime;
using CycloneGames.Scheduling.Core;
using CycloneGames.Scheduling.Integrations.Factory;
using NUnit.Framework;

namespace CycloneGames.Scheduling.Tests.Editor
{
    public sealed class FactoryWorkItemTests
    {
        [Test]
        public void Warmup_AdvancesInBoundedBatchesUntilRequestedCountIsCreated()
        {
            using var pool = new TestPool(10);
            var work = new PoolWarmupWorkItem(pool, 5, 2);
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(work, out _);

            scheduler.Tick(1, double.PositiveInfinity);
            Assert.That(work.CreatedCount, Is.EqualTo(2));
            scheduler.Tick(1, double.PositiveInfinity);
            Assert.That(work.CreatedCount, Is.EqualTo(4));
            scheduler.Tick(1, double.PositiveInfinity);

            Assert.That(work.CreatedCount, Is.EqualTo(5));
            Assert.That(work.TargetReached, Is.True);
            Assert.That(work.TerminationReason, Is.EqualTo(WorkTerminationReason.Completed));
            Assert.That(pool.CountInactive, Is.EqualTo(5));
        }

        [Test]
        public void Warmup_ZeroProgressCompletesWithoutClaimingTargetReached()
        {
            using var pool = new TestPool(1);
            var work = new PoolWarmupWorkItem(pool, 3);
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(work, out _);

            scheduler.Tick(1, double.PositiveInfinity);
            scheduler.Tick(1, double.PositiveInfinity);

            Assert.That(work.CreatedCount, Is.EqualTo(1));
            Assert.That(work.TargetReached, Is.False);
            Assert.That(work.TerminationReason, Is.EqualTo(WorkTerminationReason.Completed));
        }

        [Test]
        public void TrimInactive_UsesAbsoluteTargetAndDoesNotTouchActiveItems()
        {
            using var pool = new TestPool(10);
            pool.Prewarm(4);
            TestItem active = pool.Spawn();
            var work = new PoolTrimInactiveWorkItem(pool, 1, 2);
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(work, out _);

            scheduler.Tick(1, double.PositiveInfinity);
            scheduler.Tick(1, double.PositiveInfinity);

            Assert.That(work.TargetReached, Is.True);
            Assert.That(work.ProcessedCount, Is.EqualTo(2));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.CountActive, Is.EqualTo(1));
            Assert.That(pool.Contains(active), Is.True);
        }

        [Test]
        public void Despawn_ReturnsAllActiveItemsWithoutDestroyingThePool()
        {
            using var pool = new TestPool(10);
            pool.Prewarm(3);
            pool.Spawn();
            pool.Spawn();
            pool.Spawn();
            var work = new PoolDespawnWorkItem(pool, 2);
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(work, out _);

            scheduler.Tick(1, double.PositiveInfinity);
            Assert.That(pool.CountActive, Is.EqualTo(1));
            scheduler.Tick(1, double.PositiveInfinity);

            Assert.That(work.TargetReached, Is.True);
            Assert.That(work.ProcessedCount, Is.EqualTo(3));
            Assert.That(pool.CountActive, Is.Zero);
            Assert.That(pool.CountInactive, Is.EqualTo(3));
            Assert.That(pool.LifecycleState, Is.EqualTo(PoolLifecycleState.Ready));
        }

        [Test]
        public void Cancel_StopsFuturePoolOperationsWithoutDisposingThePool()
        {
            using var pool = new TestPool(10);
            var work = new PoolWarmupWorkItem(pool, 5);
            var scheduler = new WorkScheduler(1);
            scheduler.TrySchedule(work, out WorkItemHandle handle);
            scheduler.Tick(1, double.PositiveInfinity);

            Assert.That(scheduler.Cancel(handle), Is.True);
            Assert.That(work.TerminationReason, Is.EqualTo(WorkTerminationReason.Cancelled));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.LifecycleState, Is.EqualTo(PoolLifecycleState.Ready));
            Assert.Throws<ObjectDisposedException>(() => work.Step());
        }

        private sealed class TestPool : FastObjectPool<TestItem>
        {
            public TestPool(int hardCapacity)
                : base(
                    new PoolCapacitySettings(softCapacity: hardCapacity, hardCapacity: hardCapacity),
                    deferInitialPrewarm: true)
            {
            }

            protected override TestItem CreateNew()
            {
                return new TestItem();
            }

            protected override void OnSpawn(TestItem item)
            {
                item.IsActive = true;
            }

            protected override void OnDespawn(TestItem item)
            {
                item.IsActive = false;
            }
        }

        private sealed class TestItem : IDisposable
        {
            public bool IsActive { get; set; }
            public bool IsDisposed { get; private set; }

            public void Dispose()
            {
                IsDisposed = true;
            }
        }
    }
}
