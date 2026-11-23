using NUnit.Framework;
using UnityEditor;
using Unity.Mathematics;
using UnityEngine;

using CycloneGames.RPGFoundation.Movement.Core;
using CycloneGames.RPGFoundation.Movement.Runtime;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D;
using CycloneGames.RPGFoundation.Movement.Runtime.Movement2D.States;
using Movement3DStateBase = CycloneGames.RPGFoundation.Movement.Runtime.States.MovementStateBase;
using Movement3DRollState = CycloneGames.RPGFoundation.Movement.Runtime.States.RollState;

namespace CycloneGames.RPGFoundation.Movement.Tests.Editor
{
    public sealed class MovementRuntimeStateTests
    {
        [Test]
        public void RunState2D_TopDownOutputsFrameDisplacementAndCurrentVelocity()
        {
            MovementConfig2D config = ScriptableObject.CreateInstance<MovementConfig2D>();
            var serializedConfig = new SerializedObject(config);
            serializedConfig.FindProperty("movementType").intValue = (int)MovementType2D.TopDown;
            serializedConfig.ApplyModifiedPropertiesWithoutUndo();

            try
            {
                var context = new MovementContext2D
                {
                    StatePool = new MovementStatePool<MovementStateBase2D>(),
                    Config = config,
                    DeltaTime = 0.02f,
                    InputDirection = new float2(0.5f, -0.25f)
                };

                var state = new RunState2D();
                state.OnUpdate(ref context, out float2 displacement);

                var expectedVelocity = new float2(config.RunSpeed * 0.5f, config.RunSpeed * -0.25f);

                Assert.That(math.distance(context.CurrentVelocity, expectedVelocity), Is.LessThan(0.0001f));
                Assert.That(math.distance(displacement, expectedVelocity * context.DeltaTime), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RollState2D_KeepsTimerAndDirectionPerMovementContext()
        {
            MovementConfig2D config = ScriptableObject.CreateInstance<MovementConfig2D>();

            try
            {
                var state = new RollState2D();

                var first = new MovementContext2D
                {
                    StatePool = new MovementStatePool<MovementStateBase2D>(),
                    Config = config,
                    DeltaTime = 0.1f,
                    InputDirection = new float2(1f, 0f)
                };
                var second = new MovementContext2D
                {
                    StatePool = new MovementStatePool<MovementStateBase2D>(),
                    Config = config,
                    DeltaTime = 0.25f,
                    InputDirection = new float2(0f, 1f)
                };

                state.OnEnter(ref first);
                state.OnUpdate(ref first, out _);
                state.OnEnter(ref second);
                state.OnUpdate(ref second, out _);

                Assert.That(first.RollTimer, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(second.RollTimer, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(math.distance(first.RollDirection, new float2(1f, 0f)), Is.LessThan(0.0001f));
                Assert.That(math.distance(second.RollDirection, new float2(0f, 1f)), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(config);
            }
        }

        [Test]
        public void RollState3D_KeepsTimerAndDirectionPerMovementContext()
        {
            MovementConfig config = ScriptableObject.CreateInstance<MovementConfig>();
            var firstObject = new GameObject("First Roll Context");
            var secondObject = new GameObject("Second Roll Context");

            try
            {
                var state = new Movement3DRollState();

                var first = new MovementContext
                {
                    StatePool = new MovementStatePool<Movement3DStateBase>(),
                    Config = config,
                    DeltaTime = 0.1f,
                    Transform = firstObject.transform,
                    WorldUp = new float3(0f, 1f, 0f),
                    InputDirection = new float3(1f, 0f, 0f)
                };
                var second = new MovementContext
                {
                    StatePool = new MovementStatePool<Movement3DStateBase>(),
                    Config = config,
                    DeltaTime = 0.25f,
                    Transform = secondObject.transform,
                    WorldUp = new float3(0f, 1f, 0f),
                    InputDirection = new float3(0f, 0f, 1f)
                };

                state.OnEnter(ref first);
                state.OnUpdate(ref first, out _);
                state.OnEnter(ref second);
                state.OnUpdate(ref second, out _);

                Assert.That(first.RollTimer, Is.EqualTo(0.1f).Within(0.0001f));
                Assert.That(second.RollTimer, Is.EqualTo(0.25f).Within(0.0001f));
                Assert.That(math.distance(first.RollDirection, new float3(1f, 0f, 0f)), Is.LessThan(0.0001f));
                Assert.That(math.distance(second.RollDirection, new float3(0f, 0f, 1f)), Is.LessThan(0.0001f));
            }
            finally
            {
                Object.DestroyImmediate(firstObject);
                Object.DestroyImmediate(secondObject);
                Object.DestroyImmediate(config);
            }
        }

    }

        public sealed class MovementAnimationContractTests
        {
            private static readonly MovementVector3 Up = new MovementVector3(0f, 1f, 0f);

            [Test]
            public void Frame_SanitizesNonFiniteInputs()
            {
                var frame = new MovementAnimationState(
                    7u, float.NaN, MovementStateType.Run, MovementAnimationFlags.Grounded,
                    new MovementVector3(float.PositiveInfinity, 1f, 0f),
                    new MovementVector3(0f, float.NaN, 0f),
                    MovementVector3.Zero, MovementVector3.Zero,
                    new MovementVector3(float.NaN, 0f, 0f));

                Assert.That(frame.IsValid, Is.True);
                Assert.That(frame.DeltaTime, Is.EqualTo(0f));
                Assert.That(frame.Velocity.Equals(MovementVector3.Zero), Is.True);
                Assert.That(frame.Input.IsFinite, Is.True);
                Assert.That(frame.WorldUp.Equals(Up), Is.True);
                Assert.That(frame.GroundNormal.IsFinite, Is.True);
                Assert.That(float.IsNaN(frame.VerticalSpeed), Is.False);
                Assert.That(frame.Sequence, Is.EqualTo(7u));
            }

            [Test]
            public void Frame_ProjectsSpeedsOntoWorldUp()
            {
                var frame = new MovementAnimationState(
                    1u, 0.02f, MovementStateType.Sprint, MovementAnimationFlags.Sprinting,
                    new MovementVector3(3f, -4f, 0f),
                    MovementVector3.Zero, Up, Up, MovementVector3.Zero);

                Assert.That(frame.PlanarSpeed, Is.EqualTo(3f).Within(0.0001f));
                Assert.That(frame.VerticalSpeed, Is.EqualTo(-4f).Within(0.0001f));
                Assert.That(frame.IsSprinting, Is.True);
                Assert.That(frame.IsGrounded, Is.False);
                Assert.That(frame.State, Is.EqualTo(MovementStateType.Sprint));
            }

            [Test]
            public void Frame_IsEquatableAndSequenceSensitive()
            {
                var a = new MovementAnimationState(
                    5u, 0.01f, MovementStateType.Run, MovementAnimationFlags.Grounded,
                    new MovementVector3(1f, 0f, 0f), MovementVector3.Zero, Up, Up, MovementVector3.Zero);
                var b = new MovementAnimationState(
                    5u, 0.01f, MovementStateType.Run, MovementAnimationFlags.Grounded,
                    new MovementVector3(1f, 0f, 0f), MovementVector3.Zero, Up, Up, MovementVector3.Zero);
                var c = new MovementAnimationState(
                    6u, 0.01f, MovementStateType.Run, MovementAnimationFlags.Grounded,
                    new MovementVector3(1f, 0f, 0f), MovementVector3.Zero, Up, Up, MovementVector3.Zero);

                Assert.That(a.Equals(b), Is.True);
                Assert.That(a.GetHashCode(), Is.EqualTo(b.GetHashCode()));
                Assert.That(a.Equals(c), Is.False);
            }

            [Test]
            public void ActionWriter_IsDeterministicOrderedAndBounded()
            {
                MovementStepEvent events = MovementStepEvent.RollStarted
                    | MovementStepEvent.JumpImpulse | MovementStepEvent.Landed;
                var buffer = new MovementAnimationAction[MovementAnimationActionWriter.MaximumActionsPerStep];

                int count = MovementAnimationActionWriter.Write(events, 42u, buffer);
                Assert.That(count, Is.EqualTo(3));
                Assert.That(count, Is.EqualTo(MovementAnimationActionWriter.Count(events)));
                Assert.That(buffer[0].Kind, Is.EqualTo(MovementAnimationActionKind.Jump));
                Assert.That(buffer[1].Kind, Is.EqualTo(MovementAnimationActionKind.Roll));
                Assert.That(buffer[2].Kind, Is.EqualTo(MovementAnimationActionKind.Land));
                for (int i = 0; i < count; i++)
                {
                    Assert.That(buffer[i].Sequence, Is.EqualTo(42u));
                    Assert.That(buffer[i].Ordinal, Is.EqualTo((byte)i));
                    Assert.That(buffer[i].IsValid, Is.True);
                }

                var shortBuffer = new MovementAnimationAction[2];
                Assert.That(MovementAnimationActionWriter.Write(events, 1u, shortBuffer), Is.EqualTo(2));
                Assert.That(MovementAnimationActionWriter.Write(MovementStepEvent.None, 1u, buffer), Is.EqualTo(0));
                Assert.That(MovementAnimationActionWriter.Count((MovementStepEvent)0xFFFFFFFFu), Is.EqualTo(12));
            }

            [Test]
            public void FrameBuilder_EdgesFireOnlyOnChange()
            {
                var builder = new MovementAnimationFrameBuilder();
                Assert.That(builder.DeriveEdges(false, false, false, false, false), Is.EqualTo(MovementStepEvent.None));

                MovementStepEvent landed = builder.DeriveEdges(true, false, false, false, false);
                Assert.That(landed & MovementStepEvent.Landed, Is.EqualTo(MovementStepEvent.Landed));
                Assert.That(builder.DeriveEdges(true, false, false, false, false), Is.EqualTo(MovementStepEvent.None));

                MovementStepEvent climb = builder.DeriveEdges(true, true, false, false, false);
                Assert.That(climb & MovementStepEvent.ClimbEntered, Is.EqualTo(MovementStepEvent.ClimbEntered));
                MovementStepEvent exit = builder.DeriveEdges(true, false, false, false, false);
                Assert.That(exit & MovementStepEvent.ClimbExited, Is.EqualTo(MovementStepEvent.ClimbExited));

                builder.ResetBaseline();
                Assert.That(builder.HasBaseline, Is.False);
                Assert.That(builder.DeriveEdges(true, true, true, true, true), Is.EqualTo(MovementStepEvent.None));
            }

            [Test]
            public void FrameBuilder_SequenceIsMonotonic()
            {
                var builder = new MovementAnimationFrameBuilder();
                for (uint i = 1u; i <= 1000u; i++)
                    Assert.That(builder.AdvanceSequence(), Is.EqualTo(i));
                Assert.That(builder.CurrentSequence, Is.EqualTo(1000u));
            }

            [Test]
            public void RootMotionDelta_ConvertsToVelocityAndRejectsBadTime()
            {
                var delta = new MovementRootMotionDelta(
                    1u, new MovementVector3(0.5f, 0f, 0f), 0.1f, 0.05f, Up);
                Assert.That(delta.IsValid, Is.True);
                Assert.That(delta.LinearVelocity.X, Is.EqualTo(10f).Within(0.0001f));
                Assert.That(delta.YawVelocity, Is.EqualTo(2f).Within(0.0001f));
                Assert.That(delta.Equals(new MovementRootMotionDelta(
                    1u, new MovementVector3(0.5f, 0f, 0f), 0.1f, 0.05f, Up)), Is.True);

                var zero = new MovementRootMotionDelta(2u, MovementVector3.Zero, 0f, 0f, Up);
                Assert.That(zero.IsValid, Is.False);
                Assert.That(zero.LinearVelocity.Equals(MovementVector3.Zero), Is.True);

                var nonFinite = new MovementRootMotionDelta(
                    3u, new MovementVector3(float.NaN, 0f, 0f), float.NaN, float.PositiveInfinity, Up);
                Assert.That(nonFinite.IsValid, Is.False);
                Assert.That(nonFinite.Position.IsFinite, Is.True);
                Assert.That(nonFinite.DeltaTime, Is.EqualTo(0f));
            }

            [Test]
            public void Action_SanitizesMagnitudeAndTracksOrdinal()
            {
                var action = new MovementAnimationAction(9u, MovementAnimationActionKind.Jump, float.NaN, 3);
                Assert.That(action.IsValid, Is.True);
                Assert.That(action.Magnitude, Is.EqualTo(0f));
                Assert.That(action.Ordinal, Is.EqualTo((byte)3));
                Assert.That(action.Sequence, Is.EqualTo(9u));
                Assert.That(new MovementAnimationAction(1u, MovementAnimationActionKind.None, 0f, 0).IsValid, Is.False);
            }
        }

        public sealed class MovementAnimationPresentationTests
        {
            private static readonly MovementVector3 Up = new MovementVector3(0f, 1f, 0f);

            private static MovementRemoteSample Sample(long tick, double time, float x)
            {
                return new MovementRemoteSample(
                    tick, time, new MovementVector3(x, 0f, 0f),
                    new MovementVector3(1f, 0f, 0f), new MovementVector3(1f, 0f, 0f),
                    MovementStateType.Run, MovementAnimationFlags.Grounded);
            }

            [Test]
            public void RemoteInterpolator_RejectsDuplicateAndLateTicks()
            {
                var interpolator = new MovementRemoteInterpolator(8, 0.1, 0.25, 0.08f);
                Assert.That(interpolator.Enqueue(Sample(1, 0.00, 0f)), Is.True);
                Assert.That(interpolator.Enqueue(Sample(2, 0.05, 1f)), Is.True);
                Assert.That(interpolator.Enqueue(Sample(2, 0.10, 2f)), Is.False);
                Assert.That(interpolator.Enqueue(Sample(1, 0.15, 3f)), Is.False);
                Assert.That(interpolator.AcceptedCount, Is.EqualTo(2));
                Assert.That(interpolator.RejectedCount, Is.EqualTo(2));
                Assert.That(interpolator.BufferedCount, Is.EqualTo(2));
            }

            [Test]
            public void RemoteInterpolator_InterpolatesAndClampsExtrapolation()
            {
                var interpolator = new MovementRemoteInterpolator(16, 0.1, 0.25, 0.08f);
                for (int k = 0; k < 8; k++)
                    Assert.That(interpolator.Enqueue(Sample(k + 1, k * 0.05, k * 2f)), Is.True);

                MovementRemoteSample sample;
                bool extrapolated;
                Assert.That(interpolator.TrySample(0.25, out sample, out extrapolated), Is.True);
                Assert.That(extrapolated, Is.False);
                Assert.That(sample.Position.X, Is.EqualTo(6f).Within(0.001f));

                Assert.That(interpolator.TrySample(10.0, out sample, out extrapolated), Is.True);
                Assert.That(extrapolated, Is.True);
                Assert.That(sample.Position.X, Is.EqualTo(14f + 0.25f).Within(0.001f));
                Assert.That(interpolator.ExtrapolatedCount, Is.GreaterThan(0));
            }

            [Test]
            public void RemoteInterpolator_CorrectionDecaysInsteadOfSnapping()
            {
                var interpolator = new MovementRemoteInterpolator(8, 0.1, 0.25, 0.08f);
                interpolator.BeginCorrection(new MovementVector3(4f, 0f, 0f));
                Assert.That(interpolator.CorrectionOffset.X, Is.EqualTo(4f).Within(0.001f));
                Assert.That(interpolator.ApplyCorrection(new MovementVector3(1f, 0f, 0f)).X,
                    Is.EqualTo(5f).Within(0.001f));
                for (int k = 0; k < 100; k++) interpolator.AdvanceCorrection(0.016f);
                Assert.That(System.Math.Abs(interpolator.CorrectionOffset.X), Is.LessThan(0.001f));
                Assert.That(interpolator.ApplyCorrection(new MovementVector3(1f, 0f, 0f)).X,
                    Is.EqualTo(1f).Within(0.001f));

                interpolator.BeginCorrection(new MovementVector3(100f, 0f, 0f));
                Assert.That(interpolator.CorrectionOffset.X, Is.EqualTo(8f).Within(0.001f));
            }

            [Test]
            public void RemotePresentationSink_PublishesAValidFrame()
            {
                var counter = new CountingSink();
                var interpolator = new MovementRemoteInterpolator(8, 0.1, 0.25, 0.08f);
                var sink = new MovementRemotePresentationSink(counter, interpolator);
                for (int k = 0; k < 8; k++) sink.Enqueue(Sample(k + 1, k * 0.05, k * 2f));
                Assert.That(sink.Present(0.25, 0.016f), Is.True);
                Assert.That(counter.Applies, Is.EqualTo(1));
                Assert.That(counter.LastFrame.IsValid, Is.True);
                Assert.That(counter.LastFrame.IsGrounded, Is.True);
                sink.Reset();
                Assert.That(sink.PublishedCount, Is.EqualTo(0u));
            }

            [Test]
            public void RootMotionAccumulator_ConservesTranslationAndYaw()
            {
                var accumulator = new MovementRootMotionAccumulator();
                var delta = new MovementRootMotionDelta(
                    1u, new MovementVector3(1.2f, 0f, 0f), 0.6f, 0.05f, Up);
                accumulator.Push(in delta);
                float sumX = 0f;
                float sumYaw = 0f;
                for (int k = 0; k < 5; k++)
                {
                    MovementRootMotionDelta step;
                    if (!accumulator.TryConsumeStep(0.01f, out step)) continue;
                    sumX += step.Position.X;
                    sumYaw += step.YawRadians;
                    Assert.That(step.DeltaTime, Is.EqualTo(0.01f).Within(0.0001f));
                }
                Assert.That(sumX, Is.EqualTo(1.2f).Within(0.0001f));
                Assert.That(sumYaw, Is.EqualTo(0.6f).Within(0.0001f));
                Assert.That(accumulator.HasPending, Is.False);
            }

            [Test]
            public void RootMotionAccumulator_RejectsNonFiniteAndDiscardsWindows()
            {
                var accumulator = new MovementRootMotionAccumulator();
                var bad = new MovementRootMotionDelta(
                    1u, new MovementVector3(float.NaN, 0f, 0f), float.NaN, float.NaN, Up);
                accumulator.Push(in bad);
                Assert.That(accumulator.HasPending, Is.False);

                var good = new MovementRootMotionDelta(
                    2u, new MovementVector3(1f, 0f, 0f), 0f, 0.02f, Up);
                accumulator.Push(in good);
                Assert.That(accumulator.HasPending, Is.True);
                accumulator.Discard();
                Assert.That(accumulator.HasPending, Is.False);
                Assert.That(accumulator.DroppedWindowCount, Is.GreaterThan(0u));
                MovementRootMotionDelta ignored;
                Assert.That(accumulator.TryConsumeStep(0.01f, out ignored), Is.False);
            }

            [Test]
            public void CompositeSink_MulticastsOnceAndRejectsDuplicates()
            {
                var composite = new CompositeMovementAnimationSink(4, 4);
                var first = new CountingSink();
                var second = new CountingSink();
                Assert.That(composite.AddLocomotionSink(first), Is.True);
                Assert.That(composite.AddLocomotionSink(first), Is.False);
                Assert.That(composite.AddLocomotionSink(second), Is.True);
                Assert.That(composite.AddActionSink(first), Is.True);
                Assert.That(composite.AddActionSink(second), Is.True);
                Assert.That(composite.LocomotionSinkCount, Is.EqualTo(2));
                Assert.That(composite.ActionSinkCount, Is.EqualTo(2));

                var frame = new MovementAnimationState(
                    1u, 0.02f, MovementStateType.Run, MovementAnimationFlags.Grounded,
                    new MovementVector3(1f, 0f, 0f), MovementVector3.Zero, Up, Up, MovementVector3.Zero);
                composite.Apply(in frame);
                Assert.That(first.Applies, Is.EqualTo(1));
                Assert.That(second.Applies, Is.EqualTo(1));

                var action = new MovementAnimationAction(1u, MovementAnimationActionKind.Jump, 0f, 0);
                composite.Play(in action);
                Assert.That(first.Actions, Is.EqualTo(1));
                Assert.That(second.Actions, Is.EqualTo(1));
            }

            [Test]
            public void LayerStack_CombinesParallelLayersWithABoundedCapacity()
            {
                var stack = new MovementLayerStack();
                Assert.That(stack.TrySet(1u, MovementAnimationFlags.Sprinting, 1f, 1u), Is.True);
                Assert.That(stack.TrySet(2u, MovementAnimationFlags.Crouching, 0.5f, 2u), Is.True);
                Assert.That(stack.CombinedFlags,
                    Is.EqualTo(MovementAnimationFlags.Sprinting | MovementAnimationFlags.Crouching));
                Assert.That(stack.TryClear(1u), Is.True);
                Assert.That(stack.CombinedFlags, Is.EqualTo(MovementAnimationFlags.Crouching));
                for (int i = 0; i < 20; i++)
                    stack.TrySet((uint)(i + 10), MovementAnimationFlags.Rolling, 1f, 1u);
                Assert.That(stack.Count, Is.LessThanOrEqualTo(8));
                MovementAnimationLayer layer;
                Assert.That(stack.TryGet(2u, out layer), Is.True);
                Assert.That(layer.Weight, Is.EqualTo(0.5f).Within(0.0001f));
            }

            [Test]
            public void FrameExtensions_AreSanitizedAndParticipateInEquality()
            {
                var frame = new MovementAnimationState(
                    1u, 0.02f, MovementStateType.Sprint, MovementAnimationFlags.Sprinting,
                    new MovementVector3(3f, 0f, 0f), MovementVector3.Zero, Up, Up, MovementVector3.Zero);
                var extended = frame.WithExtensions(0xFFu, 1.5f, float.NaN, 3f, 4f);
                Assert.That(extended.ExtensionFlags, Is.EqualTo(0xFFu));
                Assert.That(extended.Extension0, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(extended.Extension1, Is.EqualTo(0f));
                Assert.That(extended.Extension3, Is.EqualTo(4f).Within(0.0001f));
                Assert.That(extended.IsValid, Is.True);
                Assert.That(extended.PlanarSpeed, Is.EqualTo(frame.PlanarSpeed));
                Assert.That(extended.Equals(frame), Is.False);
                Assert.That(extended.WithExtensions(0xFFu, 1.5f, 0f, 3f, 4f).Equals(extended), Is.True);
                Assert.That(extended.HasExtension(1u), Is.True);
                Assert.That(extended.HasExtension(1u << 20), Is.False);
            }

            private sealed class CountingSink : IMovementLocomotionSink, IMovementActionSink
            {
                public int Applies;
                public int Actions;
                public MovementAnimationState LastFrame;

                public void Apply(in MovementAnimationState state)
                {
                    Applies++;
                    LastFrame = state;
                }

                public void Play(in MovementAnimationAction action)
                {
                    if (action.IsValid) Actions++;
                }
            }
        }

        public sealed class MovementAnimationRoundThreeTests
        {
            private static readonly MovementVector3 Up = new MovementVector3(0f, 1f, 0f);

            private static MovementRemoteSample Sample(long tick, double time, float x)
            {
                return new MovementRemoteSample(tick, time, new MovementVector3(x, 0f, 0f),
                    new MovementVector3(0.5f, 0f, 0f), new MovementVector3(1f, 0f, 0f),
                    MovementStateType.Run, MovementAnimationFlags.Grounded);
            }

            [Test]
            public void FrameBuilder_WrapsInsteadOfStalling()
            {
                var builder = new MovementAnimationFrameBuilder();
                builder.ResetSequence(uint.MaxValue - 1u);
                Assert.That(builder.AdvanceSequence(), Is.EqualTo(uint.MaxValue));
                Assert.That(builder.AdvanceSequence(), Is.EqualTo(1u));
                Assert.That(builder.AdvanceSequence(), Is.EqualTo(2u));
                Assert.That(builder.WrapCount, Is.EqualTo(1u));
                Assert.That(builder.HasBaseline, Is.False);
            }

            [Test]
            public void RemoteInterpolator_RejectsTimestampRegression()
            {
                var interpolator = new MovementRemoteInterpolator(16, 0.1, 0.25, 0.08f);
                Assert.That(interpolator.Enqueue(Sample(1, 0.10, 1f)), Is.True);
                Assert.That(interpolator.Enqueue(Sample(2, 0.05, 2f)), Is.False);
                Assert.That(interpolator.RejectedCount, Is.EqualTo(1));
                Assert.That(interpolator.Enqueue(Sample(2, 0.10, 2f)), Is.True);
                Assert.That(interpolator.Enqueue(Sample(3, 0.12, 3f)), Is.True);
                Assert.That(interpolator.AcceptedCount, Is.EqualTo(3));
            }

            [Test]
            public void RemoteInterpolator_AdaptiveBracketMatchesLinearReference()
            {
                const int count = 40;
                var samples = new MovementRemoteSample[count];
                var deep = new MovementRemoteInterpolator(64, 0.1, 0.25, 0.08f);
                for (int k = 0; k < count; k++)
                {
                    samples[k] = Sample(k + 1, k * 0.02, k * 0.5f);
                    Assert.That(deep.Enqueue(in samples[k]), Is.True);
                }

                for (int q = 0; q < 200; q++)
                {
                    double target = 0.02 + (q / 200.0) * 0.70;
                    MovementRemoteSample result;
                    bool extrapolated;
                    Assert.That(deep.TrySample(target + 0.1, out result, out extrapolated), Is.True);
                    float expected = ReferenceX(samples, 0, count, target);
                    Assert.That(System.Math.Abs(result.Position.X - expected), Is.LessThan(0.001f),
                        "deep buffer at target=" + target);
                }

                const int shallowCount = 6;
                var shallowSamples = new MovementRemoteSample[shallowCount];
                var shallow = new MovementRemoteInterpolator(16, 0.1, 0.25, 0.08f);
                for (int k = 0; k < shallowCount; k++)
                {
                    shallowSamples[k] = Sample(k + 1, k * 0.02, k * 0.5f);
                    Assert.That(shallow.Enqueue(in shallowSamples[k]), Is.True);
                }
                for (int q = 0; q < 100; q++)
                {
                    double target = 0.005 + (q / 100.0) * 0.09;
                    MovementRemoteSample result;
                    bool extrapolated;
                    Assert.That(shallow.TrySample(target + 0.1, out result, out extrapolated), Is.True);
                    float expected = ReferenceX(shallowSamples, 0, shallowCount, target);
                    Assert.That(System.Math.Abs(result.Position.X - expected), Is.LessThan(0.001f),
                        "shallow buffer at target=" + target);
                }
            }

            private static float ReferenceX(MovementRemoteSample[] samples, int start, int end, double target)
            {
                if (target <= samples[start].TimestampSeconds) return samples[start].Position.X;
                if (target >= samples[end - 1].TimestampSeconds) return samples[end - 1].Position.X;
                for (int i = start; i < end - 1; i++)
                {
                    if (target < samples[i].TimestampSeconds || target > samples[i + 1].TimestampSeconds) continue;
                    double span = samples[i + 1].TimestampSeconds - samples[i].TimestampSeconds;
                    float alpha = span > 1E-9 ? (float)((target - samples[i].TimestampSeconds) / span) : 0f;
                    return samples[i].Position.X + (samples[i + 1].Position.X - samples[i].Position.X) * alpha;
                }
                return samples[end - 1].Position.X;
            }
        }

        public sealed class MovementAnimationRoundFourTests
        {
            [Test]
            public void BackendOwnership_IsDeterministic()
            {
                Assert.That(MovementAnimationBackendOwnership.IsOwner(0, 0), Is.True);
                Assert.That(MovementAnimationBackendOwnership.IsOwner(1, 0), Is.True);
                Assert.That(MovementAnimationBackendOwnership.IsOwner(2, 0), Is.True);
                Assert.That(MovementAnimationBackendOwnership.IsOwner(2, 1), Is.False);
                Assert.That(MovementAnimationBackendOwnership.IsOwner(3, 2), Is.False);
                Assert.That(MovementAnimationBackendOwnership.IsOwner(2, -1), Is.False);
                for (int count = 2; count <= 16; count++)
                {
                    int owners = 0;
                    for (int order = 0; order < count; order++)
                        if (MovementAnimationBackendOwnership.IsOwner(count, order)) owners++;
                    Assert.That(owners, Is.EqualTo(1), "exactly one owner for count=" + count);
                }
            }

            [Test]
            public void BlendWeights_NormalizesAndSanitizes()
            {
                var weights = new float[] { 2f, 2f, 0f, 0f };
                float sum = MovementAnimationBlendWeights.Normalize(weights);
                Assert.That(sum, Is.EqualTo(4f).Within(0.0001f));
                Assert.That(weights[0], Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(weights[1], Is.EqualTo(0.5f).Within(0.0001f));
                Assert.That(weights[0] + weights[1] + weights[2] + weights[3], Is.EqualTo(1f).Within(0.0001f));

                var hostile = new float[] { float.NaN, float.PositiveInfinity, -3f, 1f };
                float hostileSum = MovementAnimationBlendWeights.Normalize(hostile);
                Assert.That(hostileSum, Is.EqualTo(1f).Within(0.0001f));
                Assert.That(hostile[0], Is.EqualTo(0f));
                Assert.That(hostile[1], Is.EqualTo(0f));
                Assert.That(hostile[2], Is.EqualTo(0f));
                Assert.That(hostile[3], Is.EqualTo(1f).Within(0.0001f));

                var zeros = new float[] { 0f, 0f, 0f };
                Assert.That(MovementAnimationBlendWeights.Normalize(zeros), Is.EqualTo(0f));
                for (int i = 0; i < zeros.Length; i++) Assert.That(zeros[i], Is.EqualTo(0f));
            }

            [Test]
            public void BlendWeights_LayerAndPlaceholderAreBounded()
            {
                Assert.That(MovementAnimationBlendWeights.LayerWeight(0f), Is.EqualTo(0f));
                Assert.That(MovementAnimationBlendWeights.LayerWeight(0.4f), Is.EqualTo(0.4f).Within(0.0001f));
                Assert.That(MovementAnimationBlendWeights.LayerWeight(7f), Is.EqualTo(1f));
                Assert.That(MovementAnimationBlendWeights.LayerWeight(float.NaN), Is.EqualTo(0f));
                Assert.That(MovementAnimationBlendWeights.LayerWeight(float.NegativeInfinity), Is.EqualTo(0f));
                Assert.That(MovementAnimationBlendWeights.PlaceholderWeight(0.25f), Is.EqualTo(0.75f).Within(0.0001f));
                Assert.That(MovementAnimationBlendWeights.PlaceholderWeight(1f), Is.EqualTo(0f));
                Assert.That(MovementAnimationBlendWeights.PlaceholderWeight(-1f), Is.EqualTo(1f));
                Assert.That(MovementAnimationBlendWeights.PlaceholderWeight(float.NaN), Is.EqualTo(0f));
            }

            [Test]
            public void BackendGuard_EvaluatesOwnershipWithoutSideEffects()
            {
                var go = new GameObject("BackendGuardProbe");
                try
                {
                    var first = go.AddComponent<UnityAnimatorMovementPresenter>();
                    var second = go.AddComponent<PlayableMovementPresenter>();

                    MovementBackendClaim claim1 = MovementAnimationBackendGuard.Evaluate(first);
                    MovementBackendClaim claim2 = MovementAnimationBackendGuard.Evaluate(second);

                    Assert.That(claim1.BackendCount, Is.EqualTo(2));
                    Assert.That(claim2.BackendCount, Is.EqualTo(2));
                    Assert.That(claim1.SelfOrder, Is.EqualTo(0));
                    Assert.That(claim2.SelfOrder, Is.EqualTo(1));
                    Assert.That(claim1.IsOwner, Is.True);
                    Assert.That(claim2.IsOwner, Is.False);
                    Assert.That(MovementAnimationBackendGuard.Count(first), Is.EqualTo(2));
                    Assert.That(first.enabled, Is.True);
                    Assert.That(second.enabled, Is.True);

                    for (int count = 2; count <= 8; count++)
                    {
                        int owners = 0;
                        for (int order = 0; order < count; order++)
                            if (new MovementBackendClaim(count, order).IsOwner) owners++;
                        Assert.That(owners, Is.EqualTo(1));
                    }
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }
        }

        public sealed class MovementExtensionTests
        {
            private static readonly MovementVector3 Up = new MovementVector3(0f, 1f, 0f);

            [Test]
            public void SemanticId_IsStableAndUsesReservedRange()
            {
                MovementSemanticId a = MovementSemanticId.FromName("Combat.AimDownSights");
                MovementSemanticId b = MovementSemanticId.FromName("Combat.AimDownSights");
                MovementSemanticId c = MovementSemanticId.FromName("Combat.Reload");
                Assert.That(a.Equals(b), Is.True);
                Assert.That(a.Equals(c), Is.False);
                Assert.That(a.IsValid, Is.True);
                Assert.That(MovementSemanticId.FromName("").IsValid, Is.False);
                Assert.That(new MovementSemanticId(5u).IsBuiltIn, Is.True);
                Assert.That(a.IsBuiltIn, Is.False);
            }

            [Test]
            public void ExtensionRegistry_RegistersResolvesAndIsBounded()
            {
                var registry = new MovementExtensionRegistry();
                MovementSemanticId aim;
                Assert.That(registry.TryRegister("Combat.Aim", out aim), Is.True);
                Assert.That(registry.TryRegister("Combat.Aim", out aim), Is.True);
                Assert.That(registry.Count, Is.EqualTo(1));
                string name;
                Assert.That(registry.TryResolve(aim, out name), Is.True);
                Assert.That(name, Is.EqualTo("Combat.Aim"));
                for (int i = 0; i < MovementExtensionRegistry.Capacity + 8; i++)
                    registry.TryRegister("Ext." + i, out aim);
                Assert.That(registry.Count, Is.LessThanOrEqualTo(MovementExtensionRegistry.Capacity));
                var buffer = new MovementSemanticId[MovementExtensionRegistry.Capacity];
                Assert.That(registry.Write(buffer), Is.EqualTo(registry.Count));
                registry.Clear();
                Assert.That(registry.Count, Is.EqualTo(0));
            }

            [Test]
            public void UnityLifetime_DetectsDestroyedUnityObject()
            {
                var go = new GameObject("LifetimeProbe");
                try
                {
                    var component = go.AddComponent<Animator>();
                    Assert.That(MovementUnityLifetime.IsAlive(component), Is.True);
                    Object.DestroyImmediate(component);
                    Assert.That(MovementUnityLifetime.IsAlive(component), Is.False);
                    Assert.That(MovementUnityLifetime.Alive(component), Is.Null);
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            [Test]
            public void PresetLibrary_AcceptsExternalPresetWithoutSourceChange()
            {
                var library = new MovementConfigPresetLibrary();
                int builtIn = library.Count;
                var entries = new[]
                {
                    new MovementConfigPresetEntry(MovementConfigField.WalkSpeed, 2f),
                    new MovementConfigPresetEntry(MovementConfigField.RunSpeed, 4f)
                };
                int index = library.Register(
                    new MovementConfigPreset("External", "authored in a consuming project", true, entries));
                Assert.That(index, Is.GreaterThanOrEqualTo(0));
                Assert.That(library.Count, Is.EqualTo(builtIn + 1));
                Assert.That(library.Get(index).Name, Is.EqualTo("External"));
                Assert.That(library.CountFor(true), Is.EqualTo(4));

                var rules = new MovementConfigRules();
                var issues = new MovementConfigIssue[MovementConfigRules.FieldCount + 4];
                Assert.That(library.ValidatePreset(rules, index, issues), Is.EqualTo(0));

                library.ClearExternalPresets();
                Assert.That(library.Count, Is.EqualTo(builtIn));
                Assert.That(library.Register(null), Is.EqualTo(-1));
            }

            [Test]
            public void Frame_WithSemanticStatePreservesMotionData()
            {
                var frame = new MovementAnimationState(
                    1u, 0.02f, MovementStateType.Run, MovementAnimationFlags.Grounded,
                    new MovementVector3(3f, 0f, 0f), MovementVector3.Zero, Up, Up, Up);
                var tagged = frame.WithSemanticState(0x1234u);
                Assert.That(tagged.SemanticStateId, Is.EqualTo(0x1234u));
                Assert.That(tagged.PlanarSpeed, Is.EqualTo(frame.PlanarSpeed));
                Assert.That(tagged.Velocity.Equals(frame.Velocity), Is.True);
                Assert.That(tagged.Equals(frame), Is.False);
                Assert.That(tagged.WithSemanticState(0x1234u).Equals(tagged), Is.True);
                Assert.That(tagged.GetHashCode(), Is.EqualTo(
                    tagged.WithSemanticState(0x1234u).GetHashCode()));
            }
        }

        public sealed class MovementAdvancedTests
        {
            private sealed class CountingSink : IMovementLocomotionSink, IMovementActionSink
            {
                public int Applies;
                public int Actions;
                public MovementAnimationState LastFrame;
                public void Apply(in MovementAnimationState state) { Applies++; LastFrame = state; }
                public void Play(in MovementAnimationAction action) { if (action.IsValid) Actions++; }
            }

            private sealed class AdditiveLayer : IMovementStateLayer, IMovementLayerReplay
            {
                private readonly MovementSemanticId _id;
                private readonly float _priority;
                private bool _enabled = true;
                private float _savedTimer;
                public int Ticks;
                public int Saves;
                public int Restores;
                public float Timer;
                public AdditiveLayer(string name, float priority)
                {
                    _id = MovementSemanticId.FromName(name);
                    _priority = priority;
                }
                public MovementSemanticId Id { get { return _id; } }
                public bool Enabled { get { return _enabled; } set { _enabled = value; } }
                public float Priority { get { return _priority; } }
                public void Tick(in MovementLayerFrame frame, ref MovementLayerOutput output)
                {
                    Ticks++;
                    Timer += frame.DeltaTime;
                    output.VelocityDelta = new MovementVector3(1f, 0f, 0f);
                    output.PositionDelta = new MovementVector3(0f, 0f, 0f);
                }

                public void SaveReplayState() { Saves++; _savedTimer = Timer; }
                public void RestoreReplayState() { Restores++; Timer = _savedTimer; }
            }

            [Test]
            public void LayerArbiter_IsBoundedDeterministicAndPriorityScaled()
            {
                var arbiter = new MovementLayerArbiter();
                var first = new AdditiveLayer("Layer.A", 1f);
                var second = new AdditiveLayer("Layer.B", 0.5f);
                Assert.That(arbiter.TryAdd(first), Is.True);
                Assert.That(arbiter.TryAdd(first), Is.False);
                Assert.That(arbiter.TryAdd(second), Is.True);
                var frame = new MovementLayerFrame(1u, 0.02f,
                    new MovementVector3(3f, 0f, 0f), MovementVector3.Up, true);
                MovementLayerOutput output = arbiter.Resolve(in frame);
                Assert.That(output.VelocityDelta.X, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(output.Weight, Is.EqualTo(1.5f).Within(0.0001f));
                Assert.That(first.Ticks, Is.EqualTo(1));
                Assert.That(second.Ticks, Is.EqualTo(1));
                Assert.That(arbiter.TryRemove(first.Id), Is.True);
                Assert.That(arbiter.Count, Is.EqualTo(1));
                arbiter.Clear();
                Assert.That(arbiter.Count, Is.EqualTo(0));
            }

            [Test]
            public void LayerReplay_CoordinatesCustomStateWithoutTouchingWire()
            {
                var arbiter = new MovementLayerArbiter();
                var layer = new AdditiveLayer("Layer.Replay", 1f);
                Assert.That(arbiter.TryAdd(layer), Is.True);
                var frame = new MovementLayerFrame(1u, 0.02f,
                    MovementVector3.Zero, MovementVector3.Up, true);
                arbiter.Resolve(in frame);
                Assert.That(layer.Timer, Is.EqualTo(0.02f).Within(0.0001f));

                arbiter.SaveReplayState();
                arbiter.Resolve(in frame);
                arbiter.Resolve(in frame);
                Assert.That(layer.Timer, Is.EqualTo(0.06f).Within(0.0001f));
                arbiter.RestoreReplayState();
                Assert.That(layer.Timer, Is.EqualTo(0.02f).Within(0.0001f));
                Assert.That(layer.Saves, Is.EqualTo(1));
                Assert.That(layer.Restores, Is.EqualTo(1));
                Assert.That(layer.Ticks, Is.EqualTo(3));
            }

            [Test]
            public void BackendClaim_ResolvesCaptureOwnershipDeterministically()
            {
                Assert.That(new MovementBackendClaim(0, -1).IsOwner, Is.True);
                Assert.That(new MovementBackendClaim(1, 0).IsOwner, Is.True);
                Assert.That(new MovementBackendClaim(2, 0).IsOwner, Is.True);
                Assert.That(new MovementBackendClaim(2, 1).IsOwner, Is.False);
                Assert.That(new MovementBackendClaim(3, -1).IsOwner, Is.False);
                Assert.That(new MovementBackendClaim(2, 0).Equals(new MovementBackendClaim(2, 0)), Is.True);
                Assert.That(new MovementBackendClaim(2, 0).Equals(new MovementBackendClaim(2, 1)), Is.False);
            }

            [Test]
            public void ComponentReplay_NotifiesLayersOnceAcrossNestedWindows()
            {
                var go = new GameObject("ReplayProbe");
                try
                {
                    var component = go.AddComponent<MovementComponent>();
                    var arbiter = new MovementLayerArbiter();
                    var layer = new AdditiveLayer("Layer.ComponentReplay", 1f);
                    Assert.That(arbiter.TryAdd(layer), Is.True);
                    component.BindMovementLayers(arbiter);

                    component.BeginReplay();
                    component.BeginReplay();
                    Assert.That(layer.Saves, Is.EqualTo(1));
                    component.EndReplay();
                    Assert.That(layer.Restores, Is.EqualTo(0));
                    component.EndReplay();
                    Assert.That(layer.Restores, Is.EqualTo(1));
                    component.EndReplay();
                    Assert.That(layer.Restores, Is.EqualTo(1));
                }
                finally
                {
                    Object.DestroyImmediate(go);
                }
            }

            [Test]
            public void RemoteSession_LifecycleInterestAndReplay()
            {
                var counter = new CountingSink();
                var session = new MovementRemotePresentationSession(counter, 8, 0.02, 0.25);
                Assert.That(session.IsConnected, Is.True);
                for (int i = 0; i < 8; i++)
                {
                    var sample = new MovementRemoteSample(i + 1, i * 0.02,
                        new MovementVector3(i, 0f, 0f), new MovementVector3(1f, 0f, 0f),
                        MovementVector3.Up, MovementStateType.Run, MovementAnimationFlags.Grounded);
                    Assert.That(session.TryAccept(in sample), Is.True);
                }
                Assert.That(session.Present(0.2, 0.016f), Is.True);
                Assert.That(counter.Applies, Is.EqualTo(1));
                session.SetInterest(false);
                Assert.That(session.Present(0.22, 0.016f), Is.False);
                session.SetInterest(true);
                session.Disconnect();
                Assert.That(session.IsConnected, Is.False);
                Assert.That(session.Present(0.24, 0.016f), Is.False);
                Assert.That(session.Reconnect(), Is.True);
                Assert.That(session.IsConnected, Is.True);
            }
        }
}
