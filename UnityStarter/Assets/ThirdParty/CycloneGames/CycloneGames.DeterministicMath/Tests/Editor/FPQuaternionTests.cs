using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPQuaternionTests
    {
        private static readonly FPInt64 Epsilon = FPInt64.FromRaw(1000);

        [Test]
        public void Identity_Multiply_Returns_Same()
        {
            var q = FPQuaternion.Euler(FPInt64.Pi / 4, 0, 0);
            Assert.That((q * FPQuaternion.Identity) == q, Is.True);
            Assert.That((FPQuaternion.Identity * q) == q, Is.True);
        }

        [Test]
        public void Multiply_Is_Associative()
        {
            var a = FPQuaternion.Euler(FPInt64.Pi / 6, 0, 0);
            var b = FPQuaternion.Euler(0, FPInt64.Pi / 4, 0);
            var c = FPQuaternion.Euler(0, 0, FPInt64.Pi / 3);
            var ab_c = (a * b) * c;
            var a_bc = a * (b * c);
            AssertQuaternionClose(ab_c, a_bc, FPInt64.FromRaw(2));
        }

        [Test]
        public void Normalized_Magnitude_Is_One()
        {
            var q = new FPQuaternion(1, 2, 3, 4);
            var n = q.Normalized;
            var diff = FPInt64.Abs(n.Magnitude - FPInt64.OneValue);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue));
        }

        [Test]
        public void AngleAxis_Roundtrip()
        {
            var axis = FPVector3.Up;
            var angle = FPInt64.Pi / 3;
            var q = FPQuaternion.AngleAxis(angle, axis);
            // Rotating the axis vector should produce identity (axis is invariant under own rotation)
            var rotated = q * axis;
            var diff = FPVector3.DistanceSqr(rotated, axis);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue * 10));
        }

        [Test]
        public void Vector_Rotation_Preserves_Magnitude()
        {
            var v = new FPVector3(3, 4, 5);
            var q = FPQuaternion.Euler(FPInt64.Pi / 4, FPInt64.Pi / 6, 0);
            var rotated = q * v;
            var origMag = v.SqrMagnitude;
            var newMag = rotated.SqrMagnitude;
            var diff = FPInt64.Abs(origMag - newMag);
            Assert.That(diff.RawValue, Is.LessThan(Epsilon.RawValue * 100));
        }

        [Test]
        public void Slerp_T_Zero_Returns_A()
        {
            var a = FPQuaternion.Euler(0, 0, 0);
            var b = FPQuaternion.Euler(FPInt64.Pi / 2, 0, 0);
            var result = FPQuaternion.Slerp(a, b, FPInt64.Zero);
            Assert.That(result == a, Is.True);
        }

        [Test]
        public void Slerp_T_One_Returns_B()
        {
            var a = FPQuaternion.Euler(0, 0, 0);
            var b = FPQuaternion.Euler(FPInt64.Pi / 2, 0, 0);
            var result = FPQuaternion.Slerp(a, b, FPInt64.OneValue);
            Assert.That(result == b, Is.True);
        }

        [Test]
        public void Slerp_Opposite_Quaternions()
        {
            var a = FPQuaternion.Identity;
            var b = -a; // exactly opposite
            // Should not throw or produce NaN - falls back to Nlerp
            Assert.DoesNotThrow(() => FPQuaternion.Slerp(a, b, FPInt64.FromFloat(0.5f)));
        }

        private static void AssertQuaternionClose(FPQuaternion actual, FPQuaternion expected, FPInt64 tolerance)
        {
            Assert.That(FPInt64.Abs(actual.X - expected.X).RawValue, Is.LessThanOrEqualTo(tolerance.RawValue));
            Assert.That(FPInt64.Abs(actual.Y - expected.Y).RawValue, Is.LessThanOrEqualTo(tolerance.RawValue));
            Assert.That(FPInt64.Abs(actual.Z - expected.Z).RawValue, Is.LessThanOrEqualTo(tolerance.RawValue));
            Assert.That(FPInt64.Abs(actual.W - expected.W).RawValue, Is.LessThanOrEqualTo(tolerance.RawValue));
        }
    }
}
