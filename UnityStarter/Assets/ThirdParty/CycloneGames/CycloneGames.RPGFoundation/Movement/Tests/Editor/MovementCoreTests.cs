using NUnit.Framework;

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
        public void MovementStatePool_IsInstanceOwnedAndDeterministic()
        {
            var firstPool = new MovementStatePool<TestStateBase>();
            var secondPool = new MovementStatePool<TestStateBase>();

            TestStateA first = firstPool.Get<TestStateA>();
            TestStateA second = firstPool.Get<TestStateA>();
            TestStateB other = firstPool.Get<TestStateB>();

            Assert.That(second, Is.SameAs(first));
            Assert.That(other, Is.Not.SameAs(first));
            Assert.That(firstPool.Count, Is.EqualTo(2));
            Assert.That(secondPool.Get<TestStateA>(), Is.Not.SameAs(first));

            firstPool.Clear();
            Assert.That(firstPool.Count, Is.EqualTo(0));
            Assert.That(firstPool.Get<TestStateA>(), Is.Not.SameAs(first));
        }

        [Test]
        public void MovementSnapshotProvider_AppliesAndResetsFullState()
        {
            var provider = new TestSnapshotProvider();
            MovementSnapshot first = CreateSnapshot((ushort)MovementStateType.Jump, false, 100L, 1.5);
            MovementSnapshot second = CreateSnapshot((ushort)MovementStateType.Idle, true, 200L, 2.5);

            provider.ApplySnapshot(first);
            AssertSnapshot(first, provider.GetSnapshot());

            MovementFullState full = CreateFullState((ushort)MovementStateType.Idle, true, 200L, 2.5);
            AssertSnapshot(second, full.ToDelta(0));
            provider.ResetFromFullState(full);
            AssertSnapshot(full.ToDelta(0), provider.GetSnapshot());
            Assert.That(provider.CurrentFullState.RollTimer, Is.EqualTo(1.25f));
            Assert.That(provider.CurrentFullState.Extension0, Is.EqualTo(2.5f));
        }

        [Test]
        public void MovementFullState_RoundTripsDeltaWithoutLosingExtensibleTimers()
        {
            MovementFullState full = CreateFullState((ushort)MovementStateType.Run, false, 300L, 3.5);
            MovementSnapshot delta = full.ToDelta(0);
            MovementFullState rebuilt = MovementFullState.FromDelta(delta);

            Assert.That(full.IsValid, Is.True);
            Assert.That(delta.IsValid, Is.True);
            Assert.That(rebuilt.NetworkTick, Is.EqualTo(full.NetworkTick));
            Assert.That(rebuilt.Position.Equals(full.Position), Is.True);
            Assert.That(rebuilt.StateId, Is.EqualTo(full.StateId));
        }

        [Test]
        public void MovementFullState_RejectsNonFiniteAndOutOfRangeFields()
        {
            MovementFullState full = CreateFullState((ushort)MovementStateType.Walk, true, 10L, 1.0);
            Assert.That(full.IsValid, Is.True);

            full.StateId = 65535;
            Assert.That(full.IsValid, Is.False);

            full = CreateFullState((ushort)MovementStateType.Walk, true, 10L, 1.0);
            full.RollTimer = float.NaN;
            Assert.That(full.IsValid, Is.False);
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

        private static MovementSnapshot CreateSnapshot(ushort stateId, bool grounded, long tick, double time)
        {
            MovementSnapshot snapshot = default;
            snapshot.Position = new MovementVector3(1f, 2f, 3f);
            snapshot.Velocity = new MovementVector3(4f, 5f, 6f);
            snapshot.WorldUp = new MovementVector3(0f, 1f, 0f);
            snapshot.StateId = stateId;
            snapshot.Flags = 0;
            snapshot.VerticalVelocity = 7f;
            snapshot.IsGrounded = grounded;
            snapshot.JumpCount = 1;
            snapshot.NetworkTick = tick;
            snapshot.NetworkTimeSeconds = time;
            return snapshot;
        }

        private static MovementFullState CreateFullState(ushort stateId, bool grounded, long tick, double time)
        {
            MovementFullState state = default;
            state.NetworkTick = tick;
            state.NetworkTimeSeconds = time;
            state.Position = new MovementVector3(1f, 2f, 3f);
            state.Velocity = new MovementVector3(4f, 5f, 6f);
            state.WorldUp = new MovementVector3(0f, 1f, 0f);
            state.GroundNormal = new MovementVector3(0f, 1f, 0f);
            state.VerticalVelocity = 7f;
            state.JumpCount = 1;
            state.StateId = stateId;
            state.IsGrounded = grounded;
            state.WasGrounded = grounded;
            state.RollTimer = 1.25f;
            state.Extension0 = 2.5f;
            state.PlatformLocalRotationW = 1f;
            return state;
        }

        private static float Distance(MovementVector3 left, MovementVector3 right)
        {
            float dx = left.X - right.X;
            float dy = left.Y - right.Y;
            float dz = left.Z - right.Z;
            return (float)System.Math.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        private static void AssertSnapshot(in MovementSnapshot expected, in MovementSnapshot actual)
        {
            Assert.That(Distance(expected.Position, actual.Position), Is.LessThan(0.0001f));
            Assert.That(Distance(expected.Velocity, actual.Velocity), Is.LessThan(0.0001f));
            Assert.That(Distance(expected.WorldUp, actual.WorldUp), Is.LessThan(0.0001f));
            Assert.That(actual.StateId, Is.EqualTo(expected.StateId));
            Assert.That(actual.VerticalVelocity, Is.EqualTo(expected.VerticalVelocity));
            Assert.That(actual.IsGrounded, Is.EqualTo(expected.IsGrounded));
            Assert.That(actual.JumpCount, Is.EqualTo(expected.JumpCount));
            Assert.That(actual.NetworkTick, Is.EqualTo(expected.NetworkTick));
            Assert.That(actual.NetworkTimeSeconds, Is.EqualTo(expected.NetworkTimeSeconds));
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
            private MovementFullState _fullState;

            public MovementFullState CurrentFullState
            {
                get { return _fullState; }
            }

            public MovementSnapshot GetSnapshot()
            {
                return _snapshot;
            }

            public MovementFullState CaptureFullState()
            {
                return _fullState;
            }

            public void ApplySnapshot(in MovementSnapshot snapshot)
            {
                _snapshot = snapshot;
                _fullState = MovementFullState.FromDelta(snapshot);
            }

            public void ApplyFullState(in MovementFullState state)
            {
                _fullState = state;
                _snapshot = state.ToDelta(0);
            }

            public void ResetFromFullState(in MovementFullState state)
            {
                ApplyFullState(state);
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
