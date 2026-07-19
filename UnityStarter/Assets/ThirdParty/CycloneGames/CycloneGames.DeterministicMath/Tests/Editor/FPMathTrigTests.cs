using System;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPMathTrigTests
    {
        private const long TRIG_TOLERANCE_RAW = 2048L;
        private const long ANGLE_TOLERANCE_RAW = 4096L;

        [TestCase(-2.75)]
        [TestCase(-2.25)]
        [TestCase(-0.75)]
        [TestCase(-0.125)]
        [TestCase(0.0)]
        [TestCase(0.125)]
        [TestCase(0.75)]
        [TestCase(2.25)]
        [TestCase(2.75)]
        public void SinCos_AgreesWithDoubleOracle_InEveryQuadrant(double radians)
        {
            FPInt64 angle = FPInt64.FromDouble(radians);

            FPMath.SinCos(angle, out FPInt64 sin, out FPInt64 cos);

            AssertClose(sin, Math.Sin(angle.ToDouble()), TRIG_TOLERANCE_RAW);
            AssertClose(cos, Math.Cos(angle.ToDouble()), TRIG_TOLERANCE_RAW);
        }

        [Test]
        public void SinCos_CardinalAngles_AreExact()
        {
            Assert.That(FPMath.Sin(FPInt64.Zero), Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.Cos(FPInt64.Zero), Is.EqualTo(FPInt64.One));
            Assert.That(FPMath.Sin(FPInt64.HalfPi), Is.EqualTo(FPInt64.One));
            Assert.That(FPMath.Cos(FPInt64.HalfPi), Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.Sin(FPInt64.Pi), Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.Cos(FPInt64.Pi), Is.EqualTo(FPInt64.MinusOne));
            Assert.That(FPMath.Sin(-FPInt64.HalfPi), Is.EqualTo(FPInt64.MinusOne));
        }

        [Test]
        public void SinCos_Identity_RemainsWithinFixedPointError()
        {
            FPInt64[] angles =
            {
                FPInt64.FromDouble(-2.7),
                FPInt64.FromDouble(-1.1),
                FPInt64.FromDouble(0.3),
                FPInt64.FromDouble(1.9),
            };

            foreach (FPInt64 angle in angles)
            {
                FPMath.SinCos(angle, out FPInt64 sin, out FPInt64 cos);
                FPInt64 identity = sin * sin + cos * cos;
                Assert.That(
                    FPInt64.Abs(identity - FPInt64.One).RawValue,
                    Is.LessThanOrEqualTo(TRIG_TOLERANCE_RAW),
                    $"Angle raw value {angle.RawValue} violated sin^2 + cos^2 = 1.");
            }
        }

        [Test]
        public void SinCos_IsExactlyPeriodicByFixedPointTwoPi()
        {
            FPInt64 angle = FPInt64.FromDouble(0.731);
            FPInt64 repeated = angle + FPInt64.TwoPi * FPInt64.FromInt(100_000_000);

            FPMath.SinCos(angle, out FPInt64 sinA, out FPInt64 cosA);
            FPMath.SinCos(repeated, out FPInt64 sinB, out FPInt64 cosB);

            Assert.That(sinB.RawValue, Is.EqualTo(sinA.RawValue));
            Assert.That(cosB.RawValue, Is.EqualTo(cosA.RawValue));
        }

        [TestCase(1.0, 1.0)]
        [TestCase(1.0, -1.0)]
        [TestCase(-1.0, -1.0)]
        [TestCase(-1.0, 1.0)]
        [TestCase(0.25, 4.0)]
        [TestCase(-3.0, 0.125)]
        public void Atan2_AgreesWithDoubleOracle_InEveryQuadrant(double y, double x)
        {
            FPInt64 fixedY = FPInt64.FromDouble(y);
            FPInt64 fixedX = FPInt64.FromDouble(x);

            FPInt64 result = FPMath.Atan2(fixedY, fixedX);

            AssertClose(result, Math.Atan2(y, x), ANGLE_TOLERANCE_RAW);
        }

        [Test]
        public void Atan2_LargeComponents_PreservesRatioAndQuadrant()
        {
            FPInt64 large = FPInt64.FromInt(1_500_000_000);
            FPInt64 result = FPMath.Atan2(large, large);

            AssertClose(result, Math.PI / 4.0, ANGLE_TOLERANCE_RAW);
        }

        [Test]
        public void Atan2_AxesAndOrigin_HaveDefinedResults()
        {
            Assert.That(FPMath.Atan2(FPInt64.Zero, FPInt64.Zero), Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.Atan2(FPInt64.Zero, FPInt64.One), Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.Atan2(FPInt64.Zero, FPInt64.MinusOne), Is.EqualTo(FPInt64.Pi));
            Assert.That(FPMath.Atan2(FPInt64.One, FPInt64.Zero), Is.EqualTo(FPInt64.HalfPi));
            Assert.That(FPMath.Atan2(FPInt64.MinusOne, FPInt64.Zero), Is.EqualTo(-FPInt64.HalfPi));
        }

        [Test]
        public void Tan_AtExactAsymptotes_ReportsUndefinedOperation()
        {
            Assert.That(FPMath.TryTan(FPInt64.HalfPi, out FPInt64 positiveResult), Is.False);
            Assert.That(positiveResult, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.TryTan(-FPInt64.HalfPi, out FPInt64 negativeResult), Is.False);
            Assert.That(negativeResult, Is.EqualTo(FPInt64.Zero));
            Assert.That(
                () => FPMath.Tan(FPInt64.HalfPi),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => FPMath.Tan(-FPInt64.HalfPi),
                Throws.TypeOf<InvalidOperationException>());
        }

        [TestCase(-1.0)]
        [TestCase(-0.5)]
        [TestCase(0.0)]
        [TestCase(0.5)]
        [TestCase(1.0)]
        public void Tan_AgreesWithDoubleOracle_AwayFromAsymptotes(double radians)
        {
            FPInt64 angle = FPInt64.FromDouble(radians);

            bool success = FPMath.TryTan(angle, out FPInt64 result);

            Assert.That(success, Is.True);
            AssertClose(result, Math.Tan(radians), TRIG_TOLERANCE_RAW * 4L);
            Assert.That(FPMath.Tan(angle), Is.EqualTo(result));
        }

        [TestCase(-1.0)]
        [TestCase(-0.75)]
        [TestCase(-0.25)]
        [TestCase(0.0)]
        [TestCase(0.25)]
        [TestCase(0.75)]
        [TestCase(1.0)]
        public void AsinAndAcos_AgreeWithDoubleOracle(double value)
        {
            FPInt64 input = FPInt64.FromDouble(value);

            Assert.That(FPMath.TryAsin(input, out FPInt64 asin), Is.True);
            Assert.That(FPMath.TryAcos(input, out FPInt64 acos), Is.True);
            AssertClose(asin, Math.Asin(value), ANGLE_TOLERANCE_RAW);
            AssertClose(acos, Math.Acos(value), ANGLE_TOLERANCE_RAW);
            Assert.That(FPMath.Asin(input), Is.EqualTo(asin));
            Assert.That(FPMath.Acos(input), Is.EqualTo(acos));
        }

        [Test]
        public void AsinAndAcos_RejectInputsOutsideUnitInterval()
        {
            FPInt64 above = FPInt64.FromInt(2);
            FPInt64 below = FPInt64.FromInt(-2);

            Assert.That(FPMath.TryAsin(above, out FPInt64 asinAbove), Is.False);
            Assert.That(asinAbove, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.TryAsin(below, out FPInt64 asinBelow), Is.False);
            Assert.That(asinBelow, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.TryAcos(above, out FPInt64 acosAbove), Is.False);
            Assert.That(acosAbove, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPMath.TryAcos(below, out FPInt64 acosBelow), Is.False);
            Assert.That(acosBelow, Is.EqualTo(FPInt64.Zero));
            Assert.That(() => FPMath.Asin(above), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => FPMath.Asin(below), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => FPMath.Acos(above), Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => FPMath.Acos(below), Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void NormalizeAngle_ProducesDocumentedRanges()
        {
            FPInt64 positive = FPMath.NormalizeAnglePositive(-FPInt64.Pi / 2);
            FPInt64 signed = FPMath.NormalizeAngle(FPInt64.Pi + FPInt64.HalfPi);

            Assert.That(positive.RawValue, Is.GreaterThanOrEqualTo(0L));
            Assert.That(positive.RawValue, Is.LessThan(FPInt64.TwoPi.RawValue));
            Assert.That(signed.RawValue, Is.GreaterThanOrEqualTo(-FPInt64.Pi.RawValue));
            Assert.That(signed.RawValue, Is.LessThanOrEqualTo(FPInt64.Pi.RawValue));
            Assert.That(FPInt64.Abs(positive - (FPInt64.TwoPi - FPInt64.HalfPi)).RawValue, Is.LessThanOrEqualTo(1L));
            Assert.That(FPInt64.Abs(signed + FPInt64.HalfPi).RawValue, Is.LessThanOrEqualTo(1L));
        }

        private static void AssertClose(FPInt64 actual, double expected, long toleranceRaw)
        {
            FPInt64 expectedFixed = FPInt64.FromDouble(expected);
            long difference = FPInt64.Abs(actual - expectedFixed).RawValue;
            Assert.That(difference, Is.LessThanOrEqualTo(toleranceRaw));
        }
    }
}
