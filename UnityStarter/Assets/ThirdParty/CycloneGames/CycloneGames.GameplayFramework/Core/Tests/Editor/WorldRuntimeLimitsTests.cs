using System;
using NUnit.Framework;

namespace CycloneGames.GameplayFramework.Core.Tests
{
    public sealed class WorldRuntimeLimitsTests
    {
        [Test]
        public void InitialCapacities_AreClampedToActorLimit()
        {
            var limits = new WorldRuntimeLimits(
                maximumActorCount: 4,
                initialActorCapacity: 10,
                initialUpdateTickCapacity: 9,
                initialFixedUpdateTickCapacity: 8,
                initialLateUpdateTickCapacity: 7);

            Assert.AreEqual(4, limits.InitialActorCapacity);
            Assert.AreEqual(4, limits.InitialUpdateTickCapacity);
            Assert.AreEqual(4, limits.InitialFixedUpdateTickCapacity);
            Assert.AreEqual(4, limits.InitialLateUpdateTickCapacity);
        }

        [Test]
        public void Constructor_RejectsInvalidLimits()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WorldRuntimeLimits(maximumActorCount: 0));
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new WorldRuntimeLimits(initialActorCapacity: -1));
        }

        [Test]
        public void AdmissionSnapshot_RejectsInconsistentCounts()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new ActorAdmissionSnapshot(
                    actorCount: 3,
                    maximumActorCount: 2,
                    allocatedActorCapacity: 3,
                    peakActorCount: 3,
                    rejectedAdmissionCount: 0));
        }

        [Test]
        public void AdmissionSnapshot_AllowsListCapacityAboveProductAdmissionLimit()
        {
            var snapshot = new ActorAdmissionSnapshot(
                actorCount: 129,
                maximumActorCount: 200,
                allocatedActorCapacity: 256,
                peakActorCount: 129,
                rejectedAdmissionCount: 0);

            Assert.AreEqual(256, snapshot.AllocatedActorCapacity);
            Assert.AreEqual(200, snapshot.MaximumActorCount);
        }
    }
}
