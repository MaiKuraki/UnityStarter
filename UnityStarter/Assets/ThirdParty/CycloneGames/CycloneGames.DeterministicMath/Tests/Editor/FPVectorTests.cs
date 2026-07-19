using System;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPVectorTests
    {
        [Test]
        public void Vector2_BasicMagnitude_IsExact()
        {
            FPVector2 vector = new FPVector2(3, 4);

            Assert.That(vector.SqrMagnitude, Is.EqualTo(FPInt64.FromInt(25)));
            Assert.That(vector.Magnitude, Is.EqualTo(FPInt64.FromInt(5)));
        }

        [Test]
        public void Vector3_BasicMagnitude_IsExact()
        {
            FPVector3 vector = new FPVector3(2, 3, 6);

            Assert.That(vector.SqrMagnitude, Is.EqualTo(FPInt64.FromInt(49)));
            Assert.That(vector.Magnitude, Is.EqualTo(FPInt64.FromInt(7)));
        }

        [Test]
        public void LargeVector2_Magnitude_DoesNotWrap()
        {
            FPVector2 vector = new FPVector2(50_000, 40_000);
            double expected = Math.Sqrt(50_000d * 50_000d + 40_000d * 40_000d);

            Assert.That(vector.SqrMagnitude, Is.EqualTo(FPInt64.MaxValue));
            Assert.That(
                FPInt64.Abs(vector.Magnitude - FPInt64.FromDouble(expected)).RawValue,
                Is.LessThanOrEqualTo(200_000L));
        }

        [Test]
        public void LargeVector3_Magnitude_DoesNotWrap()
        {
            FPVector3 vector = new FPVector3(50_000, -40_000, 30_000);
            double expected = Math.Sqrt(50_000d * 50_000d + 40_000d * 40_000d + 30_000d * 30_000d);

            Assert.That(vector.SqrMagnitude, Is.EqualTo(FPInt64.MaxValue));
            Assert.That(
                FPInt64.Abs(vector.Magnitude - FPInt64.FromDouble(expected)).RawValue,
                Is.LessThanOrEqualTo(300_000L));
        }

        [Test]
        public void LargeVector_NormalizedMagnitude_IsOne()
        {
            FPVector3 source = new FPVector3(1_500_000_000, -900_000_000, 300_000_000);

            bool success = source.TryNormalize(out FPVector3 normalized);

            Assert.That(success, Is.True);
            Assert.That(source.Normalized, Is.EqualTo(normalized));
            Assert.That(
                FPInt64.Abs(normalized.Magnitude - FPInt64.One).RawValue,
                Is.LessThanOrEqualTo(32L));
        }

        [Test]
        public void ZeroVector_RequiresAnExplicitFallbackPolicy()
        {
            Assert.That(FPVector2.Zero.TryNormalize(out FPVector2 normalized2), Is.False);
            Assert.That(normalized2, Is.EqualTo(FPVector2.Zero));
            Assert.That(FPVector3.Zero.TryNormalize(out FPVector3 normalized3), Is.False);
            Assert.That(normalized3, Is.EqualTo(FPVector3.Zero));
            Assert.That(FPVector2.Zero.NormalizedOrZero, Is.EqualTo(FPVector2.Zero));
            Assert.That(FPVector3.Zero.NormalizedOrZero, Is.EqualTo(FPVector3.Zero));
            Assert.That(() => _ = FPVector2.Zero.Normalized, Throws.TypeOf<InvalidOperationException>());
            Assert.That(() => _ = FPVector3.Zero.Normalized, Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void VectorEqualityAndInterpolation_HaveExplicitValueSemantics()
        {
            FPVector2 start2 = new FPVector2(2, 4);
            FPVector2 end2 = new FPVector2(6, 8);
            FPVector3 start3 = new FPVector3(2, 4, 6);
            FPVector3 end3 = new FPVector3(6, 8, 10);
            FPInt64 beyondEnd = FPInt64.FromInt(2);

            Assert.That(start2 == new FPVector2(2, 4), Is.True);
            Assert.That(start2 != end2, Is.True);
            Assert.That(start3 == new FPVector3(2, 4, 6), Is.True);
            Assert.That(start3 != end3, Is.True);
            Assert.That(FPVector2.Lerp(start2, end2, beyondEnd), Is.EqualTo(end2));
            Assert.That(FPVector2.LerpUnclamped(start2, end2, beyondEnd), Is.EqualTo(new FPVector2(10, 12)));
            Assert.That(FPVector3.Lerp(start3, end3, beyondEnd), Is.EqualTo(end3));
            Assert.That(FPVector3.LerpUnclamped(start3, end3, beyondEnd), Is.EqualTo(new FPVector3(10, 12, 14)));
        }

        [Test]
        public void SmallestNonZeroVector_PreservesMagnitudeAndNormalizes()
        {
            FPInt64 smallest = FPInt64.FromRaw(1L);
            FPVector2 vector2 = new FPVector2(smallest, FPInt64.Zero);
            FPVector3 vector3 = new FPVector3(smallest, FPInt64.Zero, FPInt64.Zero);

            Assert.That(vector2.Magnitude, Is.EqualTo(smallest));
            Assert.That(vector2.TryNormalize(out FPVector2 normalized2), Is.True);
            Assert.That(normalized2, Is.EqualTo(FPVector2.Right));
            Assert.That(vector3.Magnitude, Is.EqualTo(smallest));
            Assert.That(vector3.TryNormalize(out FPVector3 normalized3), Is.True);
            Assert.That(normalized3, Is.EqualTo(FPVector3.Right));
        }

        [Test]
        public void Vector2_DistanceSqr_SaturatesInsteadOfWrapping()
        {
            FPVector2 a = new FPVector2(FPInt64.MinValue, FPInt64.MinValue);
            FPVector2 b = new FPVector2(FPInt64.MaxValue, FPInt64.MaxValue);

            Assert.That(FPVector2.DistanceSqr(a, b), Is.EqualTo(FPInt64.MaxValue));
        }

        [Test]
        public void Vector3_DistanceSqr_SaturatesInsteadOfWrapping()
        {
            FPVector3 a = new FPVector3(FPInt64.MinValue, FPInt64.MinValue, FPInt64.MinValue);
            FPVector3 b = new FPVector3(FPInt64.MaxValue, FPInt64.MaxValue, FPInt64.MaxValue);

            Assert.That(FPVector3.DistanceSqr(a, b), Is.EqualTo(FPInt64.MaxValue));
        }

        [Test]
        public void DistanceSqr_RegularValues_RemainsExact()
        {
            FPVector3 a = new FPVector3(1, 2, 3);
            FPVector3 b = new FPVector3(4, 6, 3);

            Assert.That(FPVector3.DistanceSqr(a, b), Is.EqualTo(FPInt64.FromInt(25)));
        }

        [Test]
        public void CrossProduct_ProducesOrthogonalVector()
        {
            FPVector3 a = new FPVector3(2, -3, 4);
            FPVector3 b = new FPVector3(-1, 5, 2);
            FPVector3 cross = FPVector3.Cross(a, b);

            Assert.That(FPVector3.Dot(cross, a).RawValue, Is.EqualTo(0L));
            Assert.That(FPVector3.Dot(cross, b).RawValue, Is.EqualTo(0L));
        }

        [Test]
        public void ProjectionOntoZeroVector_ReportsUndefinedOperation()
        {
            FPVector2 source2 = new FPVector2(3, 4);
            FPVector3 source3 = new FPVector3(3, 4, 5);

            Assert.That(FPVector2.TryProject(source2, FPVector2.Zero, out FPVector2 projection2), Is.False);
            Assert.That(projection2, Is.EqualTo(FPVector2.Zero));
            Assert.That(FPVector3.TryProject(source3, FPVector3.Zero, out FPVector3 projection3), Is.False);
            Assert.That(projection3, Is.EqualTo(FPVector3.Zero));
            Assert.That(
                () => FPVector2.Project(source2, FPVector2.Zero),
                Throws.TypeOf<InvalidOperationException>());
            Assert.That(
                () => FPVector3.Project(source3, FPVector3.Zero),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CheckedVectorOperations_SucceedForRepresentableInputs()
        {
            FPVector2 source2 = new FPVector2(3, -4);
            FPVector2 normal2 = FPVector2.Up;
            FPVector3 source3 = new FPVector3(3, -4, 5);
            FPVector3 normal3 = FPVector3.Up;

            Assert.That(FPVector2.TryDot(source2, normal2, out FPInt64 dot2), Is.True);
            Assert.That(dot2, Is.EqualTo(FPInt64.FromInt(-4)));
            Assert.That(FPVector2.TryReflect(source2, normal2, out FPVector2 reflected2), Is.True);
            Assert.That(reflected2, Is.EqualTo(new FPVector2(3, 4)));
            Assert.That(FPVector2.TryProject(source2, normal2, out FPVector2 projected2), Is.True);
            Assert.That(projected2, Is.EqualTo(new FPVector2(0, -4)));
            Assert.That(FPVector3.TryDot(source3, normal3, out FPInt64 dot3), Is.True);
            Assert.That(dot3, Is.EqualTo(FPInt64.FromInt(-4)));
            Assert.That(FPVector3.TryReflect(source3, normal3, out FPVector3 reflected3), Is.True);
            Assert.That(reflected3, Is.EqualTo(new FPVector3(3, 4, 5)));
            Assert.That(FPVector3.TryProject(source3, normal3, out FPVector3 projected3), Is.True);
            Assert.That(projected3, Is.EqualTo(new FPVector3(0, -4, 0)));
        }

        [Test]
        public void CheckedVectorOperations_ReportUnrepresentableIntermediates()
        {
            FPVector2 large2 = new FPVector2(FPInt64.MaxValue, FPInt64.Zero);
            FPVector2 scale2 = new FPVector2(2, FPInt64.Zero);
            FPVector3 large3 = new FPVector3(FPInt64.MaxValue, FPInt64.Zero, FPInt64.Zero);
            FPVector3 scale3 = new FPVector3(2, FPInt64.Zero, FPInt64.Zero);

            Assert.That(FPVector2.TryDot(large2, scale2, out _), Is.False);
            Assert.That(FPVector2.TryReflect(large2, FPVector2.Right, out _), Is.False);
            Assert.That(FPVector2.TryProject(large2, scale2, out _), Is.False);
            Assert.That(() => FPVector2.Reflect(large2, FPVector2.Right), Throws.TypeOf<OverflowException>());
            Assert.That(() => FPVector2.Project(large2, scale2), Throws.TypeOf<InvalidOperationException>());
            Assert.That(FPVector3.TryDot(large3, scale3, out _), Is.False);
            Assert.That(FPVector3.TryReflect(large3, FPVector3.Right, out _), Is.False);
            Assert.That(FPVector3.TryProject(large3, scale3, out _), Is.False);
            Assert.That(() => FPVector3.Reflect(large3, FPVector3.Right), Throws.TypeOf<OverflowException>());
            Assert.That(() => FPVector3.Project(large3, scale3), Throws.TypeOf<InvalidOperationException>());
        }
    }
}
