using System.Reflection;
using CycloneGames.AnimationRuntime.Core;
using NUnit.Framework;

namespace CycloneGames.AnimationRuntime.Tests
{
    /// <summary>
    /// Clamps applied when an authored profile is projected into a graph descriptor.
    /// </summary>
    public sealed class AnimationLayerDescriptorTests : LoggedTestBase
    {
        [Test]
        public void Capacity_IsClampedToBudget()
        {
            var low = new AnimationLayerDescriptor(null, false, 1f, 0);
            var high = new AnimationLayerDescriptor(null, false, 1f, 999);

            Assert.AreEqual(AnimationRuntimeLimits.MinLayerCapacity, (int)low.Capacity);
            Assert.AreEqual(AnimationRuntimeLimits.MaxLayerCapacity, (int)high.Capacity);
        }

        [Test]
        public void Weight_IsClampedToUnitRange()
        {
            var negative = new AnimationLayerDescriptor(null, false, -5f, 4);
            var above = new AnimationLayerDescriptor(null, false, 1f, 4);

            Assert.AreEqual(0f, negative.Weight, 1e-6f);
            Assert.AreEqual(1f, above.Weight, 1e-6f);
        }

        [Test]
        public void Default_IsOneFullBodyLayerWithFourSlots()
        {
            AnimationLayerDescriptor descriptor = AnimationLayerDescriptor.Default;

            Assert.IsNull(descriptor.Mask);
            Assert.IsFalse(descriptor.Additive);
            Assert.AreEqual(1f, descriptor.Weight, 1e-6f);
            Assert.AreEqual(AnimationRuntimeLimits.DefaultLayerCapacity, (int)descriptor.Capacity);
            Assert.IsFalse(descriptor.ApplyFootIK);
            Assert.IsFalse(descriptor.ApplyPlayableIK);
        }

        [Test]
        public void Descriptor_DoesNotCarryPerLayerRootMotion()
        {
            // Root motion is produced once per output, so a per-layer axis filter could never be honoured.
            // The fields were removed; this guard fails if they come back without a consumer.
            FieldInfo[] fields = typeof(AnimationLayerDescriptor).GetFields(BindingFlags.Public | BindingFlags.Instance);
            for (int i = 0; i < fields.Length; i++)
            {
                Assert.AreNotEqual("RootMotion", fields[i].Name);
                Assert.AreNotEqual("RootMotionScale", fields[i].Name);
            }
        }

        [Test]
        public void RootMotionAxis_HorizontalCoversXAndZOnly()
        {
            Assert.AreEqual(RootMotionAxis.Horizontal, RootMotionAxis.X | RootMotionAxis.Z);
            Assert.AreEqual(0, (int)(RootMotionAxis.Horizontal & RootMotionAxis.Y));
            Assert.AreEqual(0, (int)(RootMotionAxis.Horizontal & RootMotionAxis.Rotation));
            Assert.AreEqual(
                RootMotionAxis.X | RootMotionAxis.Y | RootMotionAxis.Z | RootMotionAxis.Rotation,
                RootMotionAxis.All);
        }
    }
}
