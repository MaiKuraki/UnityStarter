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
            var tick = new AnimationTick(0d, 0L, AnimationTimeSource.Update);

            Assert.IsTrue(tick.IsValid);
        }

        [Test]
        public void NonFiniteTime_IsClampedToZero()
        {
            var tick = new AnimationTick(double.NaN, 1L, AnimationTimeSource.External);

            Assert.AreEqual(0d, tick.Time);
        }

        [Test]
        public void NegativeDelta_IsClampedToZero()
        {
            var tick = new AnimationTick(1d, 1L, AnimationTimeSource.FixedUpdate, false, -0.5f);

            Assert.AreEqual(0f, tick.Delta);
        }

        [Test]
        public void NonFiniteDelta_IsClampedToZero()
        {
            Assert.AreEqual(0f, new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, float.NaN).Delta);
            Assert.AreEqual(0f, new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, float.PositiveInfinity).Delta);
            Assert.AreEqual(0f, new AnimationTick(0d, 0L, AnimationTimeSource.Update, false, float.NegativeInfinity).Delta);
        }

        [Test]
        public void Create_PreservesStepAndSource()
        {
            var tick = AnimationTick.Create(12.5d, 42L, AnimationTimeSource.LateUpdate, 0.016f);

            Assert.AreEqual(12.5d, tick.Time);
            Assert.AreEqual(42L, tick.StepIndex);
            Assert.AreEqual(AnimationTimeSource.LateUpdate, tick.Source);
            Assert.AreEqual(0.016f, tick.Delta, 1e-6f);
            Assert.IsFalse(tick.IsBackward);
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
