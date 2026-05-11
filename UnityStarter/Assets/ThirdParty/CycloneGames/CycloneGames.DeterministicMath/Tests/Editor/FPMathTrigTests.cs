using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPMathTrigTests
    {
        private static readonly FPInt64 Epsilon = FPInt64.FromRaw(1000); // ~2.3e-7

        [Test]
        public void Sin_Zero_Is_Zero()
        {
            Assert.That(FPMath.Sin(FPInt64.Zero).RawValue, Is.EqualTo(0));
        }

        [Test]
        public void Cos_Zero_Is_One()
        {
            Assert.That(FPMath.Cos(FPInt64.Zero).RawValue, Is.EqualTo(FPInt64.OneValue.RawValue));
        }

        [Test]
        public void SinCos_Identity_Holds()
        {
            var angle = FPInt64.Pi / 6; // 30 degrees
            FPMath.SinCos(angle, out var sin, out var cos);
            var sumSq = sin * sin + cos * cos;
            var diff = FPInt64.Abs(sumSq - FPInt64.OneValue);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void Sin_HalfPi_Is_One()
        {
            var sin = FPMath.Sin(FPInt64.HalfPi);
            var diff = FPInt64.Abs(sin - FPInt64.OneValue);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void Cos_Pi_Is_MinusOne()
        {
            var cos = FPMath.Cos(FPInt64.Pi);
            var diff = FPInt64.Abs(cos - FPInt64.MinusOne);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void Sin_Negative_Theta_Is_Negative_Sin()
        {
            var a = FPInt64.Pi / 4;
            Assert.That(FPMath.Sin(-a).RawValue, Is.EqualTo((-FPMath.Sin(a)).RawValue));
        }

        [Test]
        public void Cos_Is_Even()
        {
            var a = FPInt64.Pi / 3;
            Assert.That(FPMath.Cos(-a).RawValue, Is.EqualTo(FPMath.Cos(a).RawValue));
        }

        [Test]
        public void Atan2_Zero_Zero_Is_Zero()
        {
            Assert.That(FPMath.Atan2(FPInt64.Zero, FPInt64.Zero).RawValue, Is.EqualTo(0));
        }

        [Test]
        public void Atan2_ZeroY_PositiveX_Is_Zero()
        {
            Assert.That(FPMath.Atan2(FPInt64.Zero, FPInt64.OneValue).RawValue, Is.EqualTo(0));
        }

        [Test]
        public void Atan2_ZeroY_NegativeX_Is_Pi()
        {
            Assert.That(FPMath.Atan2(FPInt64.Zero, -FPInt64.OneValue).RawValue, Is.EqualTo(FPInt64.Pi.RawValue));
        }

        [Test]
        public void Atan2_PositiveY_ZeroX_Is_HalfPi()
        {
            var diff = FPInt64.Abs(FPMath.Atan2(FPInt64.OneValue, FPInt64.Zero) - FPInt64.HalfPi);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void Atan2_NegativeY_ZeroX_Is_MinusHalfPi()
        {
            var diff = FPInt64.Abs(FPMath.Atan2(-FPInt64.OneValue, FPInt64.Zero) - (-FPInt64.HalfPi));
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void Atan2_Quadrant_Correctness()
        {
            // Quadrant I: (1, 1) -> ~pi/4
            var q1 = FPMath.Atan2(FPInt64.OneValue, FPInt64.OneValue);
            Assert.That(q1.RawValue, Is.GreaterThan(0));
            Assert.That(q1.RawValue, Is.LessThan(FPInt64.HalfPi.RawValue));

            // Quadrant II: (1, -1) -> ~3pi/4
            var q2 = FPMath.Atan2(FPInt64.OneValue, -FPInt64.OneValue);
            Assert.That(q2.RawValue, Is.GreaterThan(FPInt64.HalfPi.RawValue));
            Assert.That(q2.RawValue, Is.LessThan(FPInt64.Pi.RawValue));

            // Quadrant III: (-1, -1) -> ~-3pi/4
            var q3 = FPMath.Atan2(-FPInt64.OneValue, -FPInt64.OneValue);
            Assert.That(q3.RawValue, Is.LessThan(-FPInt64.HalfPi.RawValue));
            Assert.That(q3.RawValue, Is.GreaterThan(-FPInt64.Pi.RawValue));

            // Quadrant IV: (-1, 1) -> ~-pi/4
            var q4 = FPMath.Atan2(-FPInt64.OneValue, FPInt64.OneValue);
            Assert.That(q4.RawValue, Is.GreaterThan(-FPInt64.HalfPi.RawValue));
            Assert.That(q4.RawValue, Is.LessThan(0));
        }

        [Test]
        public void Asin_Acos_Domain_Clamping()
        {
            // Should not throw - clamped internally
            var tooBig = FPInt64.FromInt(2);
            Assert.DoesNotThrow(() => FPMath.Asin(tooBig));
            Assert.DoesNotThrow(() => FPMath.Acos(tooBig));
            Assert.DoesNotThrow(() => FPMath.Asin(-tooBig));
            Assert.DoesNotThrow(() => FPMath.Acos(-tooBig));
        }

        [Test]
        public void Deterministic_Invariance()
        {
            var angle = FPInt64.FromDouble(1.234);
            var sin1 = FPMath.Sin(angle);
            var cos1 = FPMath.Cos(angle);
            var sin2 = FPMath.Sin(angle);
            var cos2 = FPMath.Cos(angle);
            Assert.That(sin1.RawValue, Is.EqualTo(sin2.RawValue));
            Assert.That(cos1.RawValue, Is.EqualTo(cos2.RawValue));
        }
    }
}
