using CycloneGames.DeterministicMath;
using CycloneGames.RPGFoundation.Projectile.Core;
using CycloneGames.RPGFoundation.Projectile.Integrations.DeterministicMath;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Projectile.Tests.Editor.Integrations.DeterministicMath
{
    public sealed class DeterministicProjectileSimulatorTests
    {
        [Test]
        public void Step_WithSameInput_ProducesIdenticalState()
        {
            DeterministicProjectileDefinition definition = DeterministicProjectileDefinition.FromFloats(
                definitionId: 100,
                ProjectileGuidanceMode.Homing,
                ProjectileLifecycleFlags.DespawnOnHit,
                initialSpeed: 10f,
                maxSpeed: 10f,
                acceleration: 0f,
                radius: 0.1f,
                maxLifetime: 5f,
                turnRateRadiansPerSecond: 1.5f,
                leadPredictionTime: 0f,
                gravityX: 0f,
                gravityY: 0f,
                gravityZ: 0f);

            DeterministicProjectileState a = DeterministicProjectileState.Create(
                1UL,
                10UL,
                99UL,
                spawnTick: 0,
                predictionKey: 0,
                seed: 123u,
                FPVector3.Zero,
                FPVector3.Right,
                in definition);

            DeterministicProjectileState b = a;
            var input = new DeterministicProjectileInput(
                FPInt64.FromFloat(0.0166667f),
                hasTarget: true,
                new FPVector3(FPInt64.Zero, FPInt64.Zero, FPInt64.FromInt(20)),
                FPVector3.Zero);

            for (int i = 0; i < 60; i++)
            {
                a = DeterministicProjectileSimulator.Step(in a, in definition, in input, i + 1);
                b = DeterministicProjectileSimulator.Step(in b, in definition, in input, i + 1);
            }

            Assert.That(a.Position, Is.EqualTo(b.Position));
            Assert.That(a.Velocity, Is.EqualTo(b.Velocity));
            Assert.That(a.Age, Is.EqualTo(b.Age));
        }

        [Test]
        public void ToSnapshot_PreservesIdentityAndTick()
        {
            DeterministicProjectileDefinition definition = DeterministicProjectileDefinition.FromFloats(
                definitionId: 200,
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.Authoritative,
                initialSpeed: 5f,
                maxSpeed: 5f,
                acceleration: 0f,
                radius: 0.25f,
                maxLifetime: 2f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                gravityX: 0f,
                gravityY: 0f,
                gravityZ: 0f);

            DeterministicProjectileState state = DeterministicProjectileState.Create(
                55UL,
                2UL,
                0UL,
                spawnTick: 7,
                predictionKey: 12,
                seed: 0u,
                FPVector3.Zero,
                FPVector3.Forward,
                in definition);

            state = DeterministicProjectileSimulator.Step(
                in state,
                in definition,
                new DeterministicProjectileInput(FPInt64.FromFloat(0.1f), false, FPVector3.Zero, FPVector3.Zero),
                tick: 8);

            ProjectileSnapshot snapshot = DeterministicProjectileConversions.ToSnapshot(in state);
            Assert.That(snapshot.NetworkEntityId, Is.EqualTo(55UL));
            Assert.That(snapshot.Tick, Is.EqualTo(8));
            Assert.That(snapshot.PredictionKey, Is.EqualTo(12));
        }

        [Test]
        public void Create_WithZeroDirection_ProducesZeroVelocity()
        {
            DeterministicProjectileDefinition definition = DeterministicProjectileDefinition.FromFloats(
                definitionId: 300,
                ProjectileGuidanceMode.Direction,
                ProjectileLifecycleFlags.None,
                initialSpeed: 5f,
                maxSpeed: 5f,
                acceleration: 0f,
                radius: 0.25f,
                maxLifetime: 2f,
                turnRateRadiansPerSecond: 0f,
                leadPredictionTime: 0f,
                gravityX: 0f,
                gravityY: 0f,
                gravityZ: 0f);

            DeterministicProjectileState state = DeterministicProjectileState.Create(
                1UL,
                2UL,
                0UL,
                spawnTick: 0,
                predictionKey: 0,
                seed: 0u,
                FPVector3.Zero,
                FPVector3.Zero,
                in definition);

            Assert.That(state.Velocity, Is.EqualTo(FPVector3.Zero));
        }

        [Test]
        public void Step_HomingTargetAtProjectilePosition_PreservesDirection()
        {
            DeterministicProjectileDefinition definition = DeterministicProjectileDefinition.FromFloats(
                definitionId: 301,
                ProjectileGuidanceMode.Homing,
                ProjectileLifecycleFlags.None,
                initialSpeed: 5f,
                maxSpeed: 5f,
                acceleration: 0f,
                radius: 0.25f,
                maxLifetime: 2f,
                turnRateRadiansPerSecond: 100f,
                leadPredictionTime: 0f,
                gravityX: 0f,
                gravityY: 0f,
                gravityZ: 0f);

            DeterministicProjectileState state = DeterministicProjectileState.Create(
                1UL,
                2UL,
                3UL,
                spawnTick: 0,
                predictionKey: 0,
                seed: 0u,
                FPVector3.Zero,
                FPVector3.Right,
                in definition);
            DeterministicProjectileInput input = new DeterministicProjectileInput(
                FPInt64.One,
                hasTarget: true,
                state.Position,
                FPVector3.Zero);

            state = DeterministicProjectileSimulator.Step(in state, in definition, in input, tick: 1);

            Assert.That(state.Velocity, Is.EqualTo(FPVector3.Right * definition.InitialSpeed));
        }
    }
}
