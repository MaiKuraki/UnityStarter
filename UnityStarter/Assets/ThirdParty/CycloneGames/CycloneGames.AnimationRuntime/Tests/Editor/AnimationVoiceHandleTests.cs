using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;

namespace CycloneGames.AnimationRuntime.Tests
{
    public sealed class AnimationVoiceHandleTests : LoggedTestBase
    {
        [Test]
        public void Default_IsNotUsable()
        {
            Assert.IsFalse(AnimationVoiceHandle.Invalid.IsValid);
        }

        [Test]
        public void Constructed_ExposesItsParts()
        {
            var handle = new AnimationVoiceHandle(7, 2, 3, 11);

            Assert.IsTrue(handle.IsValid);
            Assert.AreEqual(7, handle.GraphId);
            Assert.AreEqual(2, handle.Layer);
            Assert.AreEqual(3, handle.Slot);
            Assert.AreEqual(11, handle.Generation);
        }

        [Test]
        public void Generation_IsPartOfIdentity()
        {
            var first = new AnimationVoiceHandle(1, 0, 2, 4);
            var recycled = new AnimationVoiceHandle(1, 0, 2, 5);

            Assert.AreNotEqual(first, recycled);
            Assert.IsFalse(first == recycled);
        }

        [Test]
        public void GraphRebuild_IsPartOfIdentity()
        {
            var before = new AnimationVoiceHandle(1, 0, 2, 4);
            var after = new AnimationVoiceHandle(2, 0, 2, 4);

            Assert.AreNotEqual(before, after);
        }

        [Test]
        public void SameParts_AreEqual_AndShareHash()
        {
            var a = new AnimationVoiceHandle(3, 1, 2, 9);
            var b = new AnimationVoiceHandle(3, 1, 2, 9);

            Assert.AreEqual(a, b);
            Assert.AreEqual(a.GetHashCode(), b.GetHashCode());
        }

        [Test]
        public void OutOfRangeParts_AreClamped()
        {
            var handle = new AnimationVoiceHandle(1, -5, 999, -2);

            Assert.AreEqual(0, handle.Layer);
            Assert.AreEqual((int)byte.MaxValue, handle.Slot);
            Assert.AreEqual(0, handle.Generation);
        }
    }
}
