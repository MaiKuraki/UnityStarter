using System;
using CycloneGames.Factory.Runtime;
using NUnit.Framework;

namespace CycloneGames.Factory.Tests.Performance
{
    /// <summary>
    /// Deterministic invariant guard for pool ownership tracking.
    /// <para>
    /// The ownership set introduced to make duplicate detection O(1) adds exactly one new failure mode:
    /// drift between the set and the pool's own accounting, which would silently retain references and leak
    /// memory. The set has a single insertion point (<c>CreateNewItem</c>) and a single removal point
    /// (<c>TryDestroyOwnedItem</c>, which every teardown path funnels through), so the drift risk is
    /// bounded - and this suite checks that it stays bounded after every single operation.
    /// </para>
    /// Coverage is a seed x operation-mix matrix driven by <see cref="PoolOwnershipStress"/>, the same
    /// driver the standalone .NET verification harness runs, so both runtimes execute identical sequences.
    /// Fixed seeds, no wall-clock dependency, no assertions on time: fully deterministic.
    /// </summary>
    public sealed class PoolOwnershipInvariantTests
    {
        private const int SoftCapacity = 64;

        [TestCaseSource(typeof(PoolOwnershipStress), nameof(PoolOwnershipStress.Profiles))]
        public void OwnershipTracking_StaysConsistent(PoolStressProfile profile)
        {
            PoolStressResult result = PoolOwnershipStress.Run(profile);

            TestContext.WriteLine(result.Detail);

            Assert.That(result.Consistent, Is.True, result.Detail);
        }

        [Test]
        public void Clear_EmptiesOwnershipTrackingAndStillAllowsReuse()
        {
            var factory = new PerfPoolableFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: SoftCapacity, hardCapacity: -1));

            for (int i = 0; i < 32; i++)
            {
                pool.Spawn(i);
            }

            pool.Clear();

            Assert.That(pool.CountAll, Is.EqualTo(0));
            Assert.That(pool.TrackedItemCount, Is.EqualTo(0), "Ownership set retained references after Clear.");

            // A stale entry would surface as a false "already owned" rejection on the next creation.
            PerfPoolable respawned = pool.Spawn(1);
            Assert.That(respawned, Is.Not.Null);
            Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
        }

        [Test]
        public void FactoryThatRepeatsAnOwnedInstance_IsRejected()
        {
            var factory = new CachingFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: -1),
                validateUniqueOwnership: true);

            pool.Prewarm(1);
            Assert.That(pool.CountInactive, Is.EqualTo(1));

            // The factory now hands back the very instance the pool already owns: the second creation must
            // be refused instead of double-owning the object.
            Assert.Throws<InvalidOperationException>(() => pool.Prewarm(1));
            Assert.That(pool.CountInactive, Is.EqualTo(1));
            Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
        }

        [Test]
        public void DisabledOwnershipValidation_DocumentsTheTradeOff()
        {
            var factory = new CachingFactory();
            using var pool = new ObjectPool<int, PerfPoolable>(
                factory,
                new PoolCapacitySettings(softCapacity: 0, hardCapacity: -1),
                validateUniqueOwnership: false);

            // With validation off the pool cannot see the duplicate and accepts it. This test pins the
            // documented consequence of the option instead of pretending it is free.
            pool.Prewarm(2);
            Assert.That(pool.CountInactive, Is.EqualTo(2));
            Assert.That(pool.TrackedItemCount, Is.EqualTo(pool.CountAll));
        }

        /// <summary>Factory that returns the same instance on every Create call.</summary>
        private sealed class CachingFactory : IFactory<PerfPoolable>
        {
            private PerfPoolable _instance;

            public PerfPoolable Create()
            {
                return _instance ?? (_instance = new PerfPoolable());
            }
        }
    }
}
