using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class AnimationPlaybackHandleTests : LoggedTestBase
    {
        [Test]
        public void Default_IsNotUsable()
        {
            Assert.IsFalse(AnimationPlaybackHandle.Invalid.IsValid);
        }

        [Test]
        public void Constructed_ExposesItsParts()
        {
            var handle = new AnimationPlaybackHandle(7, 2, 3, 11u);

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(7, handle.GraphId);
            Assert.AreEqual(2, handle.Layer);
            Assert.AreEqual(3, handle.Slot);
            Assert.AreEqual(11u, handle.Generation);
        }

        [Test]
        public void Generation_IsPartOfIdentity()
        {
            var first = new AnimationPlaybackHandle(1, 0, 2, 4u);
            var recycled = new AnimationPlaybackHandle(1, 0, 2, 5u);

            Assert.AreNotEqual(first, recycled);
            Assert.IsFalse(first == recycled);
        }

        [Test]
        public void GraphRebuild_IsPartOfIdentity()
        {
            var before = new AnimationPlaybackHandle(1, 0, 2, 4u);
            var after = new AnimationPlaybackHandle(2, 0, 2, 4u);

            Assert.AreNotEqual(before, after);
        }

        [Test]
        public void SameParts_AreEqual_AndShareHash()
        {
            var a = new AnimationPlaybackHandle(3, 1, 2, 9u);
            var b = new AnimationPlaybackHandle(3, 1, 2, 9u);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void OutOfRangeParts_AreClamped()
        {
            var handle = new AnimationPlaybackHandle(1, -5, 999, 0u);

            Assert.AreEqual(0, handle.Layer);
            Assert.AreEqual((int)byte.MaxValue, handle.Slot);
            Assert.AreEqual(0u, handle.Generation);
            Assert.IsFalse(handle.IsValid);
        }

        [Test]
        public void Generation_SkipsZeroAndReportsWrap()
        {
            uint next = AnimationPlaybackGeneration.Next(0u, out bool wrapped);
            Assert.AreEqual(1u, next);
            Assert.IsFalse(wrapped);

            next = AnimationPlaybackGeneration.Next(41u, out wrapped);
            Assert.AreEqual(42u, next);
            Assert.IsFalse(wrapped);

            next = AnimationPlaybackGeneration.Next(uint.MaxValue, out wrapped);
            Assert.AreEqual(1u, next);
            Assert.IsTrue(wrapped);

            Assert.IsTrue(AnimationPlaybackLiveness.IsAlive(
                new AnimationPlaybackHandle(3, 0, 1, 42u), 3, 42u, true));
            Assert.IsFalse(AnimationPlaybackLiveness.IsAlive(
                new AnimationPlaybackHandle(3, 0, 1, 42u), 3, 43u, true));
            Assert.IsFalse(AnimationPlaybackLiveness.IsAlive(
                new AnimationPlaybackHandle(3, 0, 1, 42u), 3, 42u, false));
            Assert.IsFalse(AnimationPlaybackLiveness.IsAlive(
                new AnimationPlaybackHandle(4, 0, 1, 42u), 3, 42u, true));
        }
    }
}
