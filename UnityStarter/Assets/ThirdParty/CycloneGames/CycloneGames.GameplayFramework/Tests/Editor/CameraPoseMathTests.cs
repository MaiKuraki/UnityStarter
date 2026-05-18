using CycloneGames.GameplayFramework.Runtime;
using NUnit.Framework;
using Unity.Mathematics;

namespace CycloneGames.GameplayFramework.Tests.Editor
{
    public sealed class CameraPoseMathTests
    {
        [Test]
        public void ExponentialDecayT_ClampsNegativeInputs()
        {
            Assert.AreEqual(0f, CameraPoseMath.ExponentialDecayT(-1f, 0.25f), 0.0001f);
            Assert.AreEqual(0f, CameraPoseMath.ExponentialDecayT(5f, -0.25f), 0.0001f);
        }

        [Test]
        public void ExponentialDecayT_ReturnsExpectedDecayFactor()
        {
            float result = CameraPoseMath.ExponentialDecayT(2f, 0.25f);
            float expected = 1f - math.exp(-0.5f);

            Assert.AreEqual(expected, result, 0.0001f);
        }

        [Test]
        public void LookRotationSafe_ReturnsFallback_ForNearZeroDirection()
        {
            quaternion fallback = quaternion.EulerXYZ(new float3(0.1f, 0.2f, 0.3f));

            quaternion result = CameraPoseMath.LookRotationSafe(float3.zero, fallback);

            Assert.AreEqual(fallback.value.x, result.value.x, 0.0001f);
            Assert.AreEqual(fallback.value.y, result.value.y, 0.0001f);
            Assert.AreEqual(fallback.value.z, result.value.z, 0.0001f);
            Assert.AreEqual(fallback.value.w, result.value.w, 0.0001f);
        }

        [Test]
        public void IsInsideAngularDeadZone_HandlesForwardBoundaryBehindAndZeroDirection()
        {
            quaternion referenceRotation = quaternion.identity;

            Assert.IsTrue(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(0f, 0f, 1f), 10f, 10f));
            Assert.IsTrue(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(1f, 0f, 10f), 10f, 10f));
            Assert.IsFalse(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(2f, 0f, 1f), 10f, 10f));
            Assert.IsFalse(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, new float3(0f, 0f, -1f), 10f, 10f));
            Assert.IsTrue(CameraPoseMath.IsInsideAngularDeadZone(referenceRotation, float3.zero, 10f, 10f));
        }
    }
}
