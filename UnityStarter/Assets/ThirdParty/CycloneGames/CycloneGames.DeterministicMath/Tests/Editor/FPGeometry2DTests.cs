using System;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPGeometry2DTests
    {
        [Test]
        public void Circle_RejectsNegativeRadius()
        {
            Assert.That(
                () => new FPCircle(FPVector2.Zero, FPInt64.MinusOne),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Aabb_RejectsReversedComponents()
        {
            Assert.That(
                () => new FPAABB2D(new FPVector2(2, 0), new FPVector2(1, 1)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new FPAABB2D(new FPVector2(0, 2), new FPVector2(1, 1)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Aabb_CenterAndExtents_AreDerivedWithoutOverflow()
        {
            FPAABB2D aabb = new FPAABB2D(
                new FPVector2(FPInt64.MinValue, FPInt64.FromInt(-10)),
                new FPVector2(FPInt64.MaxValue, FPInt64.FromInt(10)));

            Assert.That(aabb.Center.X.RawValue, Is.EqualTo(-1L));
            Assert.That(aabb.Center.Y, Is.EqualTo(FPInt64.Zero));
            Assert.That(aabb.Extents.X.RawValue, Is.EqualTo(long.MaxValue));
            Assert.That(aabb.Extents.Y, Is.EqualTo(FPInt64.FromInt(10)));
        }

        [Test]
        public void CircleOverlap_TouchingBoundary_CountsAsOverlap()
        {
            FPCircle a = new FPCircle(FPVector2.Zero, 5);
            FPCircle b = new FPCircle(new FPVector2(10, 0), 5);

            Assert.That(FPGeometry2D.CircleOverlap(a, b), Is.True);
        }

        [Test]
        public void CircleQueries_LargeSeparatedValuesRemainExact()
        {
            FPCircle a = new FPCircle(FPVector2.Zero, 50_000);
            FPCircle b = new FPCircle(new FPVector2(200_000, 0), 50_000);

            Assert.That(FPGeometry2D.CircleOverlap(a, b), Is.False);
            Assert.That(
                FPGeometry2D.CircleContainsPoint(a, new FPVector2(100_000, 0)),
                Is.False);
        }

        [Test]
        public void AabbOverlap_TouchingBoundary_CountsAsOverlap()
        {
            FPAABB2D a = new FPAABB2D(new FPVector2(0, 0), new FPVector2(5, 5));
            FPAABB2D b = new FPAABB2D(new FPVector2(5, 1), new FPVector2(7, 3));

            Assert.That(FPGeometry2D.AABBOverlap(a, b), Is.True);
        }

        [Test]
        public void TryRayAabbIntersect_ReturnsExactNearT()
        {
            FPAABB2D aabb = new FPAABB2D(new FPVector2(5, -1), new FPVector2(10, 1));
            FPRay2D ray = new FPRay2D(FPVector2.Zero, FPVector2.Right);

            bool success = FPGeometry2D.TryRayAABB(ray, aabb, out FPInt64 t);

            Assert.That(success, Is.True);
            Assert.That(t, Is.EqualTo(FPInt64.FromInt(5)));
        }

        [Test]
        public void TryRayAabbIntersect_SupportsFarRepresentableSlabs()
        {
            FPInt64 far = FPInt64.FromInt(2_000_000_000);
            FPAABB2D aabb = new FPAABB2D(
                new FPVector2(far, -1),
                new FPVector2(far + FPInt64.One, 1));
            FPRay2D ray = new FPRay2D(FPVector2.Zero, FPVector2.Right);

            bool success = FPGeometry2D.TryRayAABB(ray, aabb, out FPInt64 t);

            Assert.That(success, Is.True);
            Assert.That(t, Is.EqualTo(far));
        }

        [Test]
        public void TryRayAabbIntersect_ZeroDirection_FailsWithDefault()
        {
            FPAABB2D aabb = new FPAABB2D(new FPVector2(-1, -1), new FPVector2(1, 1));
            FPRay2D ray = new FPRay2D(FPVector2.Zero, FPVector2.Zero);

            bool success = FPGeometry2D.TryRayAABB(ray, aabb, out FPInt64 t);

            Assert.That(success, Is.False);
            Assert.That(t, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void TryRayCircle_ReturnsNearestT()
        {
            FPCircle circle = new FPCircle(new FPVector2(10, 0), 2);
            FPRay2D ray = new FPRay2D(FPVector2.Zero, FPVector2.Right);

            bool success = FPGeometry2D.TryRayCircle(ray, circle, out FPInt64 t);

            Assert.That(success, Is.True);
            Assert.That(t, Is.EqualTo(FPInt64.FromInt(8)));
        }

        [Test]
        public void TryRayCircle_ZeroOrTinyDirection_Fails()
        {
            FPCircle circle = new FPCircle(new FPVector2(10, 0), 2);
            FPRay2D zero = new FPRay2D(FPVector2.Zero, FPVector2.Zero);
            FPRay2D tiny = new FPRay2D(
                FPVector2.Zero,
                new FPVector2(FPInt64.FromRaw(1L), FPInt64.Zero));

            Assert.That(FPGeometry2D.TryRayCircle(zero, circle, out FPInt64 zeroT), Is.False);
            Assert.That(zeroT, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPGeometry2D.TryRayCircle(tiny, circle, out FPInt64 tinyT), Is.False);
            Assert.That(tinyT, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void TryRayCircle_UnrepresentableIntermediate_FailsCleanly()
        {
            FPCircle circle = new FPCircle(new FPVector2(FPInt64.MaxValue, FPInt64.Zero), 1);
            FPRay2D ray = new FPRay2D(
                new FPVector2(FPInt64.MinValue, FPInt64.Zero),
                FPVector2.Right);

            bool success = FPGeometry2D.TryRayCircle(ray, circle, out FPInt64 t);

            Assert.That(success, Is.False);
            Assert.That(t, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void ClosestPointQueries_ReturnExpectedSurfacePoints()
        {
            FPAABB2D aabb = new FPAABB2D(new FPVector2(-2, -1), new FPVector2(2, 1));
            FPCircle circle = new FPCircle(FPVector2.Zero, 3);

            Assert.That(
                FPGeometry2D.ClosestPointOnAABB(aabb, new FPVector2(4, FPInt64.Zero)),
                Is.EqualTo(new FPVector2(2, 0)));
            Assert.That(
                FPGeometry2D.ClosestPointOnCircle(circle, FPVector2.Zero),
                Is.EqualTo(new FPVector2(3, 0)));
        }

        [Test]
        public void ClosestPointOnCircle_LargeDirection_RemainsOnSurface()
        {
            FPCircle circle = new FPCircle(FPVector2.Zero, 1);

            bool success = FPGeometry2D.TryClosestPointOnCircle(
                circle,
                new FPVector2(50_000, 0),
                out FPVector2 closest);

            Assert.That(success, Is.True);
            Assert.That(closest, Is.EqualTo(FPVector2.Right));
            Assert.That(
                FPGeometry2D.ClosestPointOnCircle(circle, new FPVector2(50_000, 0)),
                Is.EqualTo(closest));
        }

        [Test]
        public void TryClosestPointOnCircle_UnrepresentableDeltaFailsCleanly()
        {
            FPCircle circle = new FPCircle(
                new FPVector2(FPInt64.MinValue, FPInt64.Zero),
                1);
            FPVector2 point = new FPVector2(FPInt64.MaxValue, FPInt64.Zero);

            bool success = FPGeometry2D.TryClosestPointOnCircle(circle, point, out FPVector2 closest);

            Assert.That(success, Is.False);
            Assert.That(closest, Is.EqualTo(FPVector2.Zero));
            Assert.That(
                () => FPGeometry2D.ClosestPointOnCircle(circle, point),
                Throws.TypeOf<OverflowException>());
        }
    }
}
