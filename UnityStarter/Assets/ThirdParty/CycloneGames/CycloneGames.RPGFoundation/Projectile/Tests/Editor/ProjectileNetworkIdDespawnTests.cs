using CycloneGames.RPGFoundation.Projectile.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor
{
    /// <summary>
    /// Covers <see cref="ProjectileWorld.TryDespawnByNetworkEntityId"/>, the id-addressed
    /// counterpart of <see cref="ProjectileWorld.Despawn"/> added so a despawn message can be
    /// landed without the receiver already holding a handle.
    ///
    /// No timing assertions anywhere: these must be green on a busy machine and red only when
    /// the behaviour changes.
    /// </summary>
    public sealed class ProjectileNetworkIdDespawnTests
    {
        private static readonly ProjectileSpaceProfile Space = ProjectileSpaceProfile.Full3D();

        [Test]
        public void DespawnById_RemovesTheLiveProjectileAndReturnsItsHandle()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition(9001);

            Assert.That(world.TrySpawn(Request(definition, 1UL), out ProjectileHandle handle1), Is.True);
            Assert.That(world.TrySpawn(Request(definition, 2UL), out ProjectileHandle handle2), Is.True);
            Assert.That(world.TrySpawn(Request(definition, 3UL), out ProjectileHandle handle3), Is.True);
            Assert.That(world.ActiveCount, Is.EqualTo(3));

            Assert.That(world.TryDespawnByNetworkEntityId(2UL, out ProjectileHandle removed), Is.True);
            Assert.That(removed, Is.EqualTo(handle2), "the removed handle must be the one that id owned");
            Assert.That(removed, Is.Not.EqualTo(handle1));
            Assert.That(removed, Is.Not.EqualTo(handle3));
            Assert.That(world.ActiveCount, Is.EqualTo(2));

            // The remaining projectiles must still resolve, and the removed one must not.
            Assert.That(world.TryGetStateByNetworkEntityId(1UL, out _), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(3UL, out _), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(2UL, out _), Is.False);

            // The returned handle is the one the projectile had: it is no longer valid, which is
            // what proves the caller can use it to release view state it keyed by handle.
            Assert.That(world.TryGetState(removed, out _), Is.False);
        }

        [Test]
        public void DespawnById_ReportsFalseWhenNothingOwnsTheId()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition(9001);

            Assert.That(world.TrySpawn(Request(definition, 5UL), out _), Is.True);
            long despawnsBefore = world.Stats.TotalDespawnCount;

            Assert.That(world.TryDespawnByNetworkEntityId(404UL, out ProjectileHandle missing), Is.False);
            Assert.That(missing.IsValid, Is.False);
            Assert.That(world.TryDespawnByNetworkEntityId(0UL, out _), Is.False, "id 0 is not a projectile");
            Assert.That(world.ActiveCount, Is.EqualTo(1));
            Assert.That(world.Stats.TotalDespawnCount, Is.EqualTo(despawnsBefore),
                "a failed despawn must not be counted as a despawn");
        }

        [Test]
        public void DespawnById_IsIdempotentForDuplicateDelivery()
        {
            ProjectileWorld world = CreateWorld(8);
            ProjectileDefinition definition = Definition(9001);

            Assert.That(world.TrySpawn(Request(definition, 7UL), out _), Is.True);

            Assert.That(world.TryDespawnByNetworkEntityId(7UL, out _), Is.True);
            Assert.That(world.TryDespawnByNetworkEntityId(7UL, out _), Is.False, "second delivery is a no-op");
            Assert.That(world.ActiveCount, Is.EqualTo(0));
            Assert.That(world.Stats.TotalDespawnCount, Is.EqualTo(1L));

            // The id must be reusable afterwards: removal has to leave the index clean.
            Assert.That(world.TrySpawn(Request(definition, 7UL), out _), Is.True);
            Assert.That(world.TryGetStateByNetworkEntityId(7UL, out ProjectileState respawned), Is.True);
            Assert.That(respawned.NetworkEntityId, Is.EqualTo(7UL));
        }

        private static ProjectileWorld CreateWorld(int capacity)
        {
            var config = new ProjectileWorldConfig(capacity, 64, 8, in Space, 2, 0f, 1);
            return new ProjectileWorld(in config);
        }

        private static ProjectileDefinition Definition(int definitionId)
        {
            return new ProjectileDefinition(
                new ProjectileDefinitionId(definitionId),
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.DespawnOnHit,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                gravityScale: 0f,
                radius: 0.2f,
                maxLifetime: 100f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                pierceCount: 0,
                bounceCount: 0,
                collisionLayerMask: 0,
                effectPayloadId: 0);
        }

        private static ProjectileSpawnRequest Request(in ProjectileDefinition definition, ulong networkEntityId)
        {
            return new ProjectileSpawnRequest(
                definition,
                ownerEntityId: 1UL,
                networkEntityId,
                targetEntityId: 0UL,
                spawnTick: 1,
                predictionKey: 0,
                seed: 0u,
                ProjectileVector3.Zero,
                new ProjectileVector3(0f, 0f, 1f),
                ProjectileVector3.Zero);
        }
    }
}
