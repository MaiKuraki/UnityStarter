using System;
using System.Numerics;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPQuaternionTests
    {
        private const long VECTOR_TOLERANCE_SQR_RAW = 200_000L;

        [TestCase(EulerOrder.XYZ)]
        [TestCase(EulerOrder.XZY)]
        [TestCase(EulerOrder.YXZ)]
        [TestCase(EulerOrder.YZX)]
        [TestCase(EulerOrder.ZXY)]
        [TestCase(EulerOrder.ZYX)]
        public void Euler_ToEuler_RoundTripsRotation_ForEveryOrder(EulerOrder order)
        {
            FPInt64 x = FPInt64.FromDouble(0.31);
            FPInt64 y = FPInt64.FromDouble(-0.47);
            FPInt64 z = FPInt64.FromDouble(0.22);
            FPQuaternion expected = FPQuaternion.Euler(x, y, z, order);

            FPVector3 extracted = expected.ToEuler(order);
            FPQuaternion actual = FPQuaternion.Euler(extracted.X, extracted.Y, extracted.Z, order);

            AssertSameRotation(actual, expected);
        }

        [TestCase(EulerOrder.XYZ, -1)]
        [TestCase(EulerOrder.XYZ, 1)]
        [TestCase(EulerOrder.XZY, -1)]
        [TestCase(EulerOrder.XZY, 1)]
        [TestCase(EulerOrder.YXZ, -1)]
        [TestCase(EulerOrder.YXZ, 1)]
        [TestCase(EulerOrder.YZX, -1)]
        [TestCase(EulerOrder.YZX, 1)]
        [TestCase(EulerOrder.ZXY, -1)]
        [TestCase(EulerOrder.ZXY, 1)]
        [TestCase(EulerOrder.ZYX, -1)]
        [TestCase(EulerOrder.ZYX, 1)]
        public void Euler_ToEuler_RoundTripsRotation_AtGimbalLock(EulerOrder order, int sign)
        {
            FPInt64 x = FPInt64.FromDouble(0.31);
            FPInt64 y = FPInt64.FromDouble(-0.47);
            FPInt64 z = FPInt64.FromDouble(0.22);
            FPInt64 middleAngle = sign < 0 ? -FPInt64.HalfPi : FPInt64.HalfPi;

            switch (order)
            {
                case EulerOrder.XYZ:
                case EulerOrder.ZYX:
                    y = middleAngle;
                    break;

                case EulerOrder.XZY:
                case EulerOrder.YZX:
                    z = middleAngle;
                    break;

                case EulerOrder.YXZ:
                case EulerOrder.ZXY:
                    x = middleAngle;
                    break;

                default:
                    Assert.Fail("Unexpected Euler order.");
                    break;
            }

            FPQuaternion expected = FPQuaternion.Euler(x, y, z, order);
            FPVector3 extracted = expected.ToEuler(order);
            FPQuaternion actual = FPQuaternion.Euler(extracted.X, extracted.Y, extracted.Z, order);

            AssertSameRotation(actual, expected);
        }

        [TestCase(EulerOrder.XYZ)]
        [TestCase(EulerOrder.XZY)]
        [TestCase(EulerOrder.YXZ)]
        [TestCase(EulerOrder.YZX)]
        [TestCase(EulerOrder.ZXY)]
        [TestCase(EulerOrder.ZYX)]
        public void Euler_ParametersAlwaysRepresentFixedWorldAxes(EulerOrder order)
        {
            FPInt64 angle = FPInt64.FromDouble(0.63);

            FPQuaternion xRotation = FPQuaternion.Euler(angle, FPInt64.Zero, FPInt64.Zero, order);
            FPQuaternion yRotation = FPQuaternion.Euler(FPInt64.Zero, angle, FPInt64.Zero, order);
            FPQuaternion zRotation = FPQuaternion.Euler(FPInt64.Zero, FPInt64.Zero, angle, order);

            AssertSameRotation(xRotation, FPQuaternion.AngleAxis(angle, FPVector3.Right));
            AssertSameRotation(yRotation, FPQuaternion.AngleAxis(angle, FPVector3.Up));
            AssertSameRotation(zRotation, FPQuaternion.AngleAxis(angle, FPVector3.Forward));
        }

        [Test]
        public void DefaultEulerOverload_UsesZxyWithFixedAxisParameters()
        {
            FPInt64 x = FPInt64.FromDouble(0.2);
            FPInt64 y = FPInt64.FromDouble(0.3);
            FPInt64 z = FPInt64.FromDouble(0.4);

            FPQuaternion shorthand = FPQuaternion.Euler(x, y, z);
            FPQuaternion explicitOrder = FPQuaternion.Euler(x, y, z, EulerOrder.ZXY);

            Assert.That(shorthand, Is.EqualTo(explicitOrder));
        }

        [Test]
        public void AngleAxis_RotatesAroundRequestedAxis()
        {
            bool success = FPQuaternion.TryAngleAxis(
                FPInt64.HalfPi,
                FPVector3.Up,
                out FPQuaternion rotation);
            FPVector3 rotated = rotation * FPVector3.Forward;

            Assert.That(success, Is.True);
            AssertSameRotation(rotation, FPQuaternion.AngleAxis(FPInt64.HalfPi, FPVector3.Up));
            AssertVectorClose(rotated, FPVector3.Right);
        }

        [Test]
        public void AngleAxis_ZeroAxis_ReportsInvalidDomain()
        {
            bool success = FPQuaternion.TryAngleAxis(
                FPInt64.HalfPi,
                FPVector3.Zero,
                out FPQuaternion rotation);

            Assert.That(success, Is.False);
            Assert.That(rotation, Is.EqualTo(default(FPQuaternion)));
            Assert.That(
                () => FPQuaternion.AngleAxis(FPInt64.HalfPi, FPVector3.Zero),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void TryLookRotation_MapsForwardAxisToRequestedDirection()
        {
            FPVector3 direction = new FPVector3(2, 1, 3);

            bool success = FPQuaternion.TryLookRotation(direction, FPVector3.Up, out FPQuaternion rotation);

            Assert.That(success, Is.True);
            AssertVectorClose(rotation * FPVector3.Forward, direction.Normalized);
        }

        [Test]
        public void TryLookRotation_CollinearUp_UsesDeterministicFallback()
        {
            bool success = FPQuaternion.TryLookRotation(FPVector3.Up, FPVector3.Up, out FPQuaternion rotation);

            Assert.That(success, Is.True);
            AssertVectorClose(rotation * FPVector3.Forward, FPVector3.Up);
        }

        [Test]
        public void TryLookRotation_ZeroForward_FailsWithDefaultResult()
        {
            bool success = FPQuaternion.TryLookRotation(FPVector3.Zero, FPVector3.Up, out FPQuaternion rotation);

            Assert.That(success, Is.False);
            Assert.That(rotation, Is.EqualTo(default(FPQuaternion)));
            Assert.That(
                () => FPQuaternion.LookRotation(FPVector3.Zero),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void FromToRotation_ValidAndZeroDirectionsExposeCheckedContract()
        {
            bool success = FPQuaternion.TryFromToRotation(
                FPVector3.Right,
                FPVector3.Up,
                out FPQuaternion rotation);

            Assert.That(success, Is.True);
            AssertVectorClose(rotation * FPVector3.Right, FPVector3.Up);
            Assert.That(
                FPQuaternion.TryFromToRotation(FPVector3.Zero, FPVector3.Up, out FPQuaternion invalid),
                Is.False);
            Assert.That(invalid, Is.EqualTo(default(FPQuaternion)));
            Assert.That(
                () => FPQuaternion.FromToRotation(FPVector3.Zero, FPVector3.Up),
                Throws.TypeOf<System.ArgumentException>());
        }

        [Test]
        public void EulerApis_RejectUnknownOrder()
        {
            EulerOrder invalid = (EulerOrder)999;

            Assert.That(
                () => FPQuaternion.Euler(FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, invalid),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
            Assert.That(
                () => FPQuaternion.Identity.ToEuler(invalid),
                Throws.TypeOf<System.ArgumentOutOfRangeException>());
        }

        [Test]
        public void Nlerp_UsesShortestArcForEquivalentOppositeSigns()
        {
            FPQuaternion rotation = FPQuaternion.Euler(
                FPInt64.FromDouble(0.3),
                FPInt64.FromDouble(-0.6),
                FPInt64.FromDouble(0.1));

            FPQuaternion interpolated = FPQuaternion.Nlerp(rotation, -rotation, FPInt64.FromDouble(0.5));

            AssertSameRotation(interpolated, rotation);
        }

        [Test]
        public void Interpolation_ClampsEndpointsExactly()
        {
            FPQuaternion a = FPQuaternion.Euler(FPInt64.FromDouble(0.1), FPInt64.Zero, FPInt64.Zero);
            FPQuaternion b = FPQuaternion.Euler(FPInt64.Zero, FPInt64.FromDouble(0.8), FPInt64.Zero);

            Assert.That(FPQuaternion.Slerp(a, b, FPInt64.MinusOne), Is.EqualTo(a));
            Assert.That(FPQuaternion.Slerp(a, b, FPInt64.FromInt(2)), Is.EqualTo(b));
            Assert.That(FPQuaternion.Nlerp(a, b, FPInt64.MinusOne), Is.EqualTo(a));
            Assert.That(FPQuaternion.Nlerp(a, b, FPInt64.FromInt(2)), Is.EqualTo(b));
        }

        [Test]
        public void UnclampedInterpolation_ExtrapolatesBeyondTheEndRotation()
        {
            FPQuaternion a = FPQuaternion.Identity;
            FPQuaternion b = FPQuaternion.AngleAxis(FPInt64.HalfPi, FPVector3.Up);
            FPInt64 t = FPInt64.FromInt(2);

            FPVector3 slerped = FPQuaternion.SlerpUnclamped(a, b, t) * FPVector3.Forward;
            FPVector3 nlerped = FPQuaternion.NlerpUnclamped(a, b, t) * FPVector3.Forward;

            Assert.That(FPVector3.DistanceSqr(slerped, b * FPVector3.Forward).RawValue, Is.GreaterThan(1_000_000L));
            Assert.That(FPVector3.DistanceSqr(nlerped, b * FPVector3.Forward).RawValue, Is.GreaterThan(1_000_000L));
        }

        [Test]
        public void Inverse_ComposesToIdentityRotation()
        {
            FPQuaternion rotation = FPQuaternion.Euler(
                FPInt64.FromDouble(-0.4),
                FPInt64.FromDouble(0.7),
                FPInt64.FromDouble(0.2));

            Assert.That(rotation.TryNormalize(out FPQuaternion normalized), Is.True);
            Assert.That(rotation.TryInverse(out FPQuaternion inverse), Is.True);
            AssertSameRotation(normalized, rotation);
            AssertSameRotation(rotation * inverse, FPQuaternion.Identity);
            AssertSameRotation(rotation * rotation.Inverse, FPQuaternion.Identity);
        }

        [Test]
        public void MicroQuaternion_InverseUsesFullWidthSquaredMagnitude()
        {
            FPQuaternion singleAxis = new FPQuaternion(
                FPInt64.FromRaw(2L),
                FPInt64.Zero,
                FPInt64.Zero,
                FPInt64.Zero);
            FPQuaternion twoAxes = new FPQuaternion(
                FPInt64.FromRaw(2L),
                FPInt64.FromRaw(2L),
                FPInt64.Zero,
                FPInt64.Zero);

            Assert.That(singleAxis.TryInverse(out FPQuaternion singleAxisInverse), Is.True);
            Assert.That(singleAxisInverse.X, Is.EqualTo(FPInt64.MinValue));
            Assert.That(singleAxis * singleAxisInverse, Is.EqualTo(FPQuaternion.Identity));

            Assert.That(twoAxes.TryInverse(out FPQuaternion twoAxesInverse), Is.True);
            Assert.That(twoAxesInverse.X.RawValue, Is.EqualTo(-(1L << 62)));
            Assert.That(twoAxesInverse.Y.RawValue, Is.EqualTo(-(1L << 62)));
            Assert.That(twoAxes * twoAxesInverse, Is.EqualTo(FPQuaternion.Identity));
        }

        [Test]
        public void MicroQuaternion_UnrepresentableInverseReportsFailure()
        {
            FPQuaternion micro = new FPQuaternion(
                FPInt64.FromRaw(1L),
                FPInt64.Zero,
                FPInt64.Zero,
                FPInt64.Zero);

            Assert.That(micro.TryInverse(out FPQuaternion inverse), Is.False);
            Assert.That(inverse, Is.EqualTo(default(FPQuaternion)));
            Assert.That(() => _ = micro.Inverse, Throws.TypeOf<System.InvalidOperationException>());
        }

        [Test]
        public void TryInverse_RandomRawValues_MatchBigIntegerOracle()
        {
            var random = new Random(0x696E7672);
            var buffer = new byte[sizeof(long)];

            for (int iteration = 0; iteration < 2_000; iteration++)
            {
                long x = NextInt64(random, buffer);
                long y = NextInt64(random, buffer);
                long z = NextInt64(random, buffer);
                long w = NextInt64(random, buffer);
                if ((x | y | z | w) == 0)
                {
                    w = 1;
                }

                BigInteger squaredMagnitude =
                    (BigInteger)x * x +
                    (BigInteger)y * y +
                    (BigInteger)z * z +
                    (BigInteger)w * w;
                long expectedX = 0;
                long expectedY = 0;
                long expectedZ = 0;
                long expectedW = 0;
                bool expectedSuccess =
                    TryGetExpectedInverseRaw(-((BigInteger)x << 64), squaredMagnitude, out expectedX) &&
                    TryGetExpectedInverseRaw(-((BigInteger)y << 64), squaredMagnitude, out expectedY) &&
                    TryGetExpectedInverseRaw(-((BigInteger)z << 64), squaredMagnitude, out expectedZ) &&
                    TryGetExpectedInverseRaw((BigInteger)w << 64, squaredMagnitude, out expectedW) &&
                    (expectedX | expectedY | expectedZ | expectedW) != 0;
                var quaternion = new FPQuaternion(
                    FPInt64.FromRaw(x),
                    FPInt64.FromRaw(y),
                    FPInt64.FromRaw(z),
                    FPInt64.FromRaw(w));

                bool actualSuccess = quaternion.TryInverse(out FPQuaternion actual);

                Assert.That(actualSuccess, Is.EqualTo(expectedSuccess), $"Iteration {iteration}");
                if (expectedSuccess)
                {
                    Assert.That(actual.X.RawValue, Is.EqualTo(expectedX), $"X at iteration {iteration}");
                    Assert.That(actual.Y.RawValue, Is.EqualTo(expectedY), $"Y at iteration {iteration}");
                    Assert.That(actual.Z.RawValue, Is.EqualTo(expectedZ), $"Z at iteration {iteration}");
                    Assert.That(actual.W.RawValue, Is.EqualTo(expectedW), $"W at iteration {iteration}");
                }
                else
                {
                    Assert.That(actual, Is.EqualTo(default(FPQuaternion)), $"Iteration {iteration}");
                }
            }
        }

        [Test]
        public void ZeroQuaternion_NormalizeAndInverseReportInvalidDomain()
        {
            FPQuaternion zero = default;

            Assert.That(zero.TryNormalize(out FPQuaternion normalized), Is.False);
            Assert.That(normalized, Is.EqualTo(default(FPQuaternion)));
            Assert.That(zero.TryInverse(out FPQuaternion inverse), Is.False);
            Assert.That(inverse, Is.EqualTo(default(FPQuaternion)));
            Assert.That(() => _ = zero.Normalized, Throws.TypeOf<System.InvalidOperationException>());
            Assert.That(() => _ = zero.Inverse, Throws.TypeOf<System.InvalidOperationException>());
        }

        [Test]
        public void ZeroQuaternion_VectorRotationReportsInvalidDomain()
        {
            FPQuaternion zero = default;

            bool success = FPQuaternion.TryRotate(zero, FPVector3.Right, out FPVector3 result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.EqualTo(FPVector3.Zero));
            Assert.That(
                () => _ = zero * FPVector3.Right,
                Throws.TypeOf<System.InvalidOperationException>());
        }

        [Test]
        public void VectorRotation_PreservesLargeVectorMagnitude()
        {
            FPVector3 source = new FPVector3(50_000, -40_000, 30_000);
            FPQuaternion rotation = FPQuaternion.Euler(
                FPInt64.FromDouble(0.25),
                FPInt64.FromDouble(-0.5),
                FPInt64.FromDouble(0.75));

            FPVector3 rotated = rotation * source;

            long difference = FPInt64.Abs(rotated.Magnitude - source.Magnitude).RawValue;
            Assert.That(difference, Is.LessThanOrEqualTo(1_000_000L));
        }

        private static void AssertSameRotation(FPQuaternion actual, FPQuaternion expected)
        {
            AssertVectorClose(actual * FPVector3.Right, expected * FPVector3.Right);
            AssertVectorClose(actual * FPVector3.Up, expected * FPVector3.Up);
            AssertVectorClose(actual * FPVector3.Forward, expected * FPVector3.Forward);
        }

        private static void AssertVectorClose(FPVector3 actual, FPVector3 expected)
        {
            Assert.That(
                FPVector3.DistanceSqr(actual, expected).RawValue,
                Is.LessThanOrEqualTo(VECTOR_TOLERANCE_SQR_RAW));
        }

        private static long NextInt64(Random random, byte[] buffer)
        {
            random.NextBytes(buffer);
            return BitConverter.ToInt64(buffer, 0);
        }

        private static bool TryGetExpectedInverseRaw(
            BigInteger numerator,
            BigInteger denominator,
            out long result)
        {
            BigInteger quotient = numerator / denominator;
            if (quotient < long.MinValue || quotient > long.MaxValue)
            {
                result = 0;
                return false;
            }

            result = (long)quotient;
            return true;
        }
    }
}
