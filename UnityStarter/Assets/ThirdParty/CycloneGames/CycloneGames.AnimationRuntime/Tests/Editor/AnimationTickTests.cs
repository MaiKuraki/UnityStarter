using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class AnimationTickTests : LoggedTestBase
    {
        [Test]
        public void None_IsNotValid()
        {
            Assert.IsFalse(AnimationTick.None.IsValid);
        }

        [Test]
        public void FirstStep_IsValid()
        {
            Assert.IsTrue(new AnimationTick(0d, 0L, AnimationTimeSource.Update).IsValid);
        }

        [Test]
        public void NonFiniteClocks_AreClampedToZero()
        {
            var tick = new AnimationTick(double.NaN, double.PositiveInfinity, 1L, AnimationTimeSource.External);
            Assert.AreEqual(0d, tick.ClipTime);
            Assert.AreEqual(0d, tick.TransitionTime);
        }

        [Test]
        public void ConvenienceConstructor_UsesClipTimeAsTransitionTime()
        {
            var tick = new AnimationTick(12.5d, 42L, AnimationTimeSource.LateUpdate);
            Assert.AreEqual(12.5d, tick.ClipTime);
            Assert.AreEqual(12.5d, tick.TransitionTime);
            Assert.AreEqual(42L, tick.StepIndex);
            Assert.AreEqual(AnimationTimeSource.LateUpdate, tick.Source);
            Assert.IsFalse(tick.IsBackward);
            Assert.IsFalse(tick.SuppressEvents);
        }

        [Test]
        public void SeparateClocks_ArePreserved()
        {
            var tick = new AnimationTick(3d, 7d, 9L, AnimationTimeSource.Replay, true, true);
            Assert.AreEqual(3d, tick.ClipTime);
            Assert.AreEqual(7d, tick.TransitionTime);
            Assert.IsTrue(tick.IsBackward);
            Assert.IsTrue(tick.SuppressEvents);
        }
    }

    public sealed class AnimationRuntimeLimitsTests : LoggedTestBase
    {
        [Test]
        public void PlaybackStruct_FitsInOneCacheLine()
        {
            Assert.LessOrEqual(
                AnimationRuntimeGraph.PlaybackStructSize,
                64,
                "The playback struct must stay within one 64-byte cache line. Adding a field is a layout regression.");
        }

        [Test]
        public void LayerBudgets_AreStable()
        {
            Assert.AreEqual(8, AnimationRuntimeLimits.MaxLayers);
            Assert.AreEqual(8, AnimationRuntimeLimits.MaxLayerCapacity);
            Assert.AreEqual(4, AnimationRuntimeLimits.DefaultLayerCapacity);
        }

        [Test]
        public void RuntimeBudgets_AreStable()
        {
            Assert.AreEqual(64, AnimationRuntimeLimits.MaxSweepLegs);
            Assert.AreEqual(4096, AnimationRuntimeLimits.MaxPendingClipEventsPerCommit);
            Assert.AreEqual(32, AnimationRuntimeLimits.InitialClipEventQueueCapacity);
            Assert.AreEqual(512, AnimationRuntimeLimits.SteadyClipEventQueueCapacity);
            Assert.GreaterOrEqual(AnimationRuntimeLimits.MaxPendingClipEventsPerCommit,
                AnimationRuntimeLimits.SteadyClipEventQueueCapacity);
        }

        [Test]
        public void ClockMath_RejectsOverflowingProduct()
        {
            Assert.IsFalse(AnimationClockMath.TryUnwrapped(
                0d, 0d, 1e300d, float.MaxValue, out double unwrapped));
            Assert.AreEqual(0d, unwrapped);
        }

        [Test]
        public void ClockMath_AcceptsFiniteStep()
        {
            Assert.IsTrue(AnimationClockMath.TryUnwrapped(0.25d, 10d, 10.5d, 2d, out double unwrapped));
            Assert.AreEqual(1.25d, unwrapped, 1e-12d);
        }

        [Test]
        public void ClockMath_ClampFinite_NonFiniteDegradesToZero()
        {
            Assert.AreEqual(0d, AnimationClockMath.ClampFinite(double.NaN, 1f));
            Assert.AreEqual(0d, AnimationClockMath.ClampFinite(double.PositiveInfinity, 1f));
            Assert.AreEqual(0d, AnimationClockMath.ClampFinite(-1d, 1f));
            Assert.AreEqual(1d, AnimationClockMath.ClampFinite(4d, 1f));
        }

        [Test]
        public void PlaybackKey_EqualityUsesInstanceChannelClipAndLayer()
        {
            var a = new AnimationPlaybackKey(3, 1, 2);
            var b = new AnimationPlaybackKey(3, 1, 2);
            var differentInstance = new AnimationPlaybackKey(4, 1, 2);
            var differentChannel = new AnimationPlaybackKey(3, 2, 2);
            var differentClip = new AnimationPlaybackKey(3, 1, 2, 1);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
            Assert.AreNotEqual(a, differentInstance);
            Assert.AreNotEqual(a, differentChannel);
            Assert.AreNotEqual(a, differentClip);
        }
    }
}
