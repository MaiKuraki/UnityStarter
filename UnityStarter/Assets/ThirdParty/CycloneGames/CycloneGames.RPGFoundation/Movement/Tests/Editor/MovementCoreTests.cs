using NUnit.Framework;
using Unity.Mathematics;

using CycloneGames.RPGFoundation.Movement.Core;

namespace CycloneGames.RPGFoundation.Movement.Tests.Editor
{
    public sealed class MovementCoreTests
    {
        [Test]
        public void MovementAttributeModifier_AppliesOptionalBaseOverrideAndMultiplier()
        {
            var multiplierOnly = new MovementAttributeModifier(null, 1.5f);
            var overrideAndMultiplier = new MovementAttributeModifier(8f, 2f);

            Assert.That(multiplierOnly.Apply(10f), Is.EqualTo(15f));
            Assert.That(overrideAndMultiplier.Apply(10f), Is.EqualTo(16f));
        }

        [Test]
        public void MovementAttributeHelper_ReturnsConfigValueWithoutAuthority()
        {
            float finalValue = MovementAttributeHelper.GetFinalValue(MovementAttribute.RunSpeed, 6f);
            MovementAttributeModifier modifier = MovementAttributeHelper.GetModifier(MovementAttribute.RunSpeed);

            Assert.That(finalValue, Is.EqualTo(6f));
            Assert.That(modifier.BaseValueOverride.HasValue, Is.False);
            Assert.That(modifier.Multiplier, Is.EqualTo(1f));
        }

        [Test]
        public void MovementAttributeHelper_DelegatesToAuthorityWhenAvailable()
        {
            var authority = new TestMovementAuthority(
                finalValue: 9f,
                modifier: new MovementAttributeModifier(4f, 2f));

            float finalValue = MovementAttributeHelper.GetFinalValue(MovementAttribute.SprintSpeed, 6f, authority);
            MovementAttributeModifier modifier = MovementAttributeHelper.GetModifier(MovementAttribute.SprintSpeed, authority);

            Assert.That(finalValue, Is.EqualTo(9f));
            Assert.That(modifier.BaseValueOverride.HasValue, Is.True);
            Assert.That(modifier.BaseValueOverride.Value, Is.EqualTo(4f));
            Assert.That(modifier.Multiplier, Is.EqualTo(2f));
        }

        [Test]
        public void StatePool_ReturnsOneInstancePerConcreteStateType()
        {
            TestStateA first = StatePool<TestStateBase>.GetState<TestStateA>();
            TestStateA second = StatePool<TestStateBase>.GetState<TestStateA>();
            TestStateB other = StatePool<TestStateBase>.GetState<TestStateB>();

            Assert.That(second, Is.SameAs(first));
            Assert.That(other, Is.Not.SameAs(first));
        }

        [Test]
        public void MovementSnapshotProvider_AppliesAndResetsNetworkSafeState()
        {
            var provider = new TestSnapshotProvider();
            var first = new MovementSnapshot
            {
                Position = new float3(1f, 2f, 3f),
                Velocity = new float3(4f, 5f, 6f),
                WorldUp = new float3(0f, 1f, 0f),
                StateType = MovementStateType.Jump,
                VerticalVelocity = 7f,
                IsGrounded = false,
                JumpCount = 1,
                Tick = 100,
                Timestamp = 1.5f
            };
            var second = new MovementSnapshot
            {
                Position = new float3(8f, 9f, 10f),
                Velocity = new float3(0f, 0f, 0f),
                WorldUp = new float3(0f, 1f, 0f),
                StateType = MovementStateType.Idle,
                VerticalVelocity = 0f,
                IsGrounded = true,
                JumpCount = 0,
                Tick = 200,
                Timestamp = 2.5f
            };

            provider.ApplySnapshot(first);

            AssertSnapshot(first, provider.GetSnapshot());

            provider.ResetFromSnapshot(second);

            AssertSnapshot(second, provider.GetSnapshot());
        }

        [Test]
        public void MovementStateRequestContext_Carries_NetworkTimeline_Without_NetworkingDependency()
        {
            MovementStateRequestContext context = MovementStateRequestContext.FromNetwork(
                payload: "roll",
                tick: 120,
                sequence: 9,
                predictionKey: 77,
                flags: 3U);

            Assert.That(context.Source, Is.EqualTo(MovementStateRequestSource.Network));
            Assert.That(context.Payload, Is.EqualTo("roll"));
            Assert.That(context.Tick, Is.EqualTo(120));
            Assert.That(context.Sequence, Is.EqualTo(9));
            Assert.That(context.PredictionKey, Is.EqualTo(77));
            Assert.That(context.Flags, Is.EqualTo(3U));
            Assert.That(context.IsNetworkDriven, Is.True);
            Assert.That(context.HasTimeline, Is.True);
            Assert.That(context.HasPredictionKey, Is.True);
        }

        [Test]
        public void MovementStateRequestContext_AbilitySource_DoesNot_Imply_NetworkTimeline()
        {
            MovementStateRequestContext context = MovementStateRequestContext.FromAbility(payload: this);

            Assert.That(context.IsAbilityDriven, Is.True);
            Assert.That(context.IsNetworkDriven, Is.False);
            Assert.That(context.HasTimeline, Is.False);
            Assert.That(context.Payload, Is.SameAs(this));
        }

        private static void AssertSnapshot(in MovementSnapshot expected, in MovementSnapshot actual)
        {
            Assert.That(math.distance(expected.Position, actual.Position), Is.LessThan(0.0001f));
            Assert.That(math.distance(expected.Velocity, actual.Velocity), Is.LessThan(0.0001f));
            Assert.That(math.distance(expected.WorldUp, actual.WorldUp), Is.LessThan(0.0001f));
            Assert.That(actual.StateType, Is.EqualTo(expected.StateType));
            Assert.That(actual.VerticalVelocity, Is.EqualTo(expected.VerticalVelocity));
            Assert.That(actual.IsGrounded, Is.EqualTo(expected.IsGrounded));
            Assert.That(actual.JumpCount, Is.EqualTo(expected.JumpCount));
            Assert.That(actual.Tick, Is.EqualTo(expected.Tick));
            Assert.That(actual.Timestamp, Is.EqualTo(expected.Timestamp));
        }

        private abstract class TestStateBase
        {
        }

        private sealed class TestStateA : TestStateBase
        {
        }

        private sealed class TestStateB : TestStateBase
        {
        }

        private sealed class TestSnapshotProvider : IMovementSnapshotProvider
        {
            private MovementSnapshot _snapshot;

            public MovementSnapshot GetSnapshot()
            {
                return _snapshot;
            }

            public void ApplySnapshot(in MovementSnapshot snapshot)
            {
                _snapshot = snapshot;
            }

            public void ResetFromSnapshot(in MovementSnapshot snapshot)
            {
                _snapshot = snapshot;
            }
        }

        private sealed class TestMovementAuthority : IMovementAuthority
        {
            private readonly float _finalValue;
            private readonly MovementAttributeModifier _modifier;

            public TestMovementAuthority(float finalValue, MovementAttributeModifier modifier)
            {
                _finalValue = finalValue;
                _modifier = modifier;
            }

            public bool CanEnterState(MovementStateType stateType, object context = null)
            {
                return true;
            }

            public void OnStateEntered(MovementStateType stateType)
            {
            }

            public void OnStateExited(MovementStateType stateType)
            {
            }

            public MovementAttributeModifier GetAttributeModifier(MovementAttribute attribute)
            {
                return _modifier;
            }

            public float? GetBaseValue(MovementAttribute attribute)
            {
                return _modifier.BaseValueOverride;
            }

            public float GetMultiplier(MovementAttribute attribute)
            {
                return _modifier.Multiplier;
            }

            public float GetFinalValue(MovementAttribute attribute, float configValue)
            {
                return _finalValue;
            }
        }
    }
}

