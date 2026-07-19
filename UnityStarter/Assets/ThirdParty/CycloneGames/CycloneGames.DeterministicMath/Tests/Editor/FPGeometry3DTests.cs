using System;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPGeometry3DTests
    {
        [Test]
        public void Sphere_RejectsNegativeRadius()
        {
            Assert.That(
                () => new FPSphere(FPVector3.Zero, FPInt64.MinusOne),
                Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void Bounds_RejectsReversedComponents()
        {
            Assert.That(
                () => new FPAABB3D(new FPVector3(2, 0, 0), new FPVector3(1, 1, 1)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new FPAABB3D(new FPVector3(0, 2, 0), new FPVector3(1, 1, 1)),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => new FPAABB3D(new FPVector3(0, 0, 2), new FPVector3(1, 1, 1)),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Obb_RejectsNegativeHalfExtentsAndZeroOrientation()
        {
            Assert.That(
                () => new FPOBB3D(FPVector3.Zero, new FPVector3(-1, 1, 1), FPQuaternion.Identity),
                Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(
                () => new FPOBB3D(FPVector3.Zero, FPVector3.One, default),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void Obb_MicroOrientation_IsNormalizedInsteadOfRejected()
        {
            FPQuaternion microOrientation = new FPQuaternion(
                FPInt64.FromRaw(1L),
                FPInt64.Zero,
                FPInt64.Zero,
                FPInt64.Zero);

            FPOBB3D obb = new FPOBB3D(FPVector3.Zero, FPVector3.One, microOrientation);

            Assert.That(obb.IsValid, Is.True);
            Assert.That(
                obb.Orientation,
                Is.EqualTo(new FPQuaternion(
                    FPInt64.One,
                    FPInt64.Zero,
                    FPInt64.Zero,
                    FPInt64.Zero)));
        }

        [Test]
        public void Bounds_CenterExtentsAndSize_AreDerivedWithoutOverflow()
        {
            FPAABB3D bounds = new FPAABB3D(
                new FPVector3(FPInt64.MinValue, -10, -4),
                new FPVector3(FPInt64.MaxValue, 10, 8));

            Assert.That(bounds.Center.X.RawValue, Is.EqualTo(-1L));
            Assert.That(bounds.Center.Y, Is.EqualTo(FPInt64.Zero));
            Assert.That(bounds.Center.Z, Is.EqualTo(FPInt64.FromInt(2)));
            Assert.That(bounds.Extents.X.RawValue, Is.EqualTo(long.MaxValue));
            Assert.That(bounds.Size.X, Is.EqualTo(FPInt64.MaxValue));
            Assert.That(bounds.Size.Y, Is.EqualTo(FPInt64.FromInt(20)));
        }

        [Test]
        public void SphereOverlap_TouchingBoundary_CountsAsOverlap()
        {
            FPSphere a = new FPSphere(FPVector3.Zero, 5);
            FPSphere b = new FPSphere(new FPVector3(10, 0, 0), 5);

            Assert.That(FPGeometry3D.SphereOverlap(a, b), Is.True);
        }

        [Test]
        public void SphereQueries_LargeSeparatedValuesRemainExact()
        {
            FPSphere a = new FPSphere(FPVector3.Zero, 50_000);
            FPSphere b = new FPSphere(new FPVector3(200_000, 0, 0), 50_000);

            Assert.That(FPGeometry3D.SphereOverlap(a, b), Is.False);
            Assert.That(
                FPGeometry3D.SphereContainsPoint(a, new FPVector3(100_000, 0, 0)),
                Is.False);
        }

        [Test]
        public void TryRaySphere_ReturnsNearestT()
        {
            FPSphere sphere = new FPSphere(new FPVector3(10, 0, 0), 2);
            FPRay3D ray = new FPRay3D(FPVector3.Zero, FPVector3.Right);

            bool success = FPGeometry3D.TryRaySphere(ray, sphere, out FPInt64 t);

            Assert.That(success, Is.True);
            Assert.That(t, Is.EqualTo(FPInt64.FromInt(8)));
        }

        [Test]
        public void TryRaySphere_ZeroOrTinyDirection_FailsWithDefault()
        {
            FPSphere sphere = new FPSphere(new FPVector3(10, 0, 0), 2);
            FPRay3D zero = new FPRay3D(FPVector3.Zero, FPVector3.Zero);
            FPRay3D tiny = new FPRay3D(
                FPVector3.Zero,
                new FPVector3(FPInt64.FromRaw(1L), FPInt64.Zero, FPInt64.Zero));

            Assert.That(FPGeometry3D.TryRaySphere(zero, sphere, out FPInt64 zeroT), Is.False);
            Assert.That(zeroT, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPGeometry3D.TryRaySphere(tiny, sphere, out FPInt64 tinyT), Is.False);
            Assert.That(tinyT, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void TryRaySphere_UnrepresentableIntermediate_FailsCleanly()
        {
            FPSphere sphere = new FPSphere(
                new FPVector3(FPInt64.MaxValue, FPInt64.Zero, FPInt64.Zero),
                1);
            FPRay3D ray = new FPRay3D(
                new FPVector3(FPInt64.MinValue, FPInt64.Zero, FPInt64.Zero),
                FPVector3.Right);

            bool success = FPGeometry3D.TryRaySphere(ray, sphere, out FPInt64 t);

            Assert.That(success, Is.False);
            Assert.That(t, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void TryRayAABB_SupportsFarRepresentableSlabs()
        {
            FPInt64 far = FPInt64.FromInt(2_000_000_000);
            FPAABB3D bounds = new FPAABB3D(
                new FPVector3(far, -1, -1),
                new FPVector3(far + FPInt64.One, 1, 1));
            FPRay3D ray = new FPRay3D(FPVector3.Zero, FPVector3.Right);

            bool success = FPGeometry3D.TryRayAABB(ray, bounds, out FPInt64 t);

            Assert.That(success, Is.True);
            Assert.That(t, Is.EqualTo(far));
        }

        [Test]
        public void TryRayAABB_ParallelOutsideOrZeroDirection_Fails()
        {
            FPAABB3D bounds = new FPAABB3D(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));
            FPRay3D parallelOutside = new FPRay3D(new FPVector3(-1, 10, 0), FPVector3.Right);
            FPRay3D zero = new FPRay3D(new FPVector3(1, 1, 1), FPVector3.Zero);

            Assert.That(FPGeometry3D.TryRayAABB(parallelOutside, bounds, out FPInt64 parallelT), Is.False);
            Assert.That(parallelT, Is.EqualTo(FPInt64.Zero));
            Assert.That(FPGeometry3D.TryRayAABB(zero, bounds, out FPInt64 zeroT), Is.False);
            Assert.That(zeroT, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void ObbOverlap_RotatedNonUniformSeparatedBoxes_ReturnsFalse()
        {
            FPQuaternion rotation = FPQuaternion.AngleAxis(FPInt64.Pi / 4, FPVector3.Forward);
            FPOBB3D elongated = new FPOBB3D(FPVector3.Zero, new FPVector3(4, FPInt64.FromDouble(0.5), FPInt64.FromDouble(0.5)), rotation);
            FPInt64 diagonal = FPInt64.FromDouble(Math.Sqrt(0.5));
            FPOBB3D separated = new FPOBB3D(
                new FPVector3(-diagonal, diagonal, FPInt64.Zero),
                new FPVector3(FPInt64.FromDouble(0.25), FPInt64.FromDouble(0.25), FPInt64.FromDouble(0.25)),
                FPQuaternion.Identity);

            Assert.That(FPGeometry3D.OBBOverlap(elongated, separated), Is.False);
        }

        [Test]
        public void ObbOverlap_IdenticalRotatedBoxes_ReturnsTrue()
        {
            FPQuaternion rotation = FPQuaternion.AngleAxis(FPInt64.Pi / 3, FPVector3.Up);
            FPOBB3D a = new FPOBB3D(FPVector3.Zero, new FPVector3(3, 1, 2), rotation);
            FPOBB3D b = new FPOBB3D(FPVector3.Zero, new FPVector3(3, 1, 2), rotation);

            Assert.That(FPGeometry3D.OBBOverlap(a, b), Is.True);
        }

        [Test]
        public void ObbOverlap_MaximumExtents_DoNotOverflowProjectionSums()
        {
            FPVector3 maximumExtents = new FPVector3(
                FPInt64.MaxValue,
                FPInt64.MaxValue,
                FPInt64.MaxValue);
            FPOBB3D a = new FPOBB3D(FPVector3.Zero, maximumExtents, FPQuaternion.Identity);
            FPOBB3D b = new FPOBB3D(FPVector3.Zero, maximumExtents, FPQuaternion.Identity);

            Assert.That(FPGeometry3D.OBBOverlap(a, b), Is.True);
        }

        [Test]
        public void ObbOverlap_WideCenterDelta_DistinguishesTouchingAndSeparatedBoxes()
        {
            FPInt64 negativeMaximum = FPInt64.FromRaw(-long.MaxValue);
            FPVector3 centerA = new FPVector3(negativeMaximum, FPInt64.Zero, FPInt64.Zero);
            FPVector3 centerB = new FPVector3(FPInt64.MaxValue, FPInt64.Zero, FPInt64.Zero);
            FPVector3 maximumExtents = new FPVector3(
                FPInt64.MaxValue,
                FPInt64.MaxValue,
                FPInt64.MaxValue);
            FPVector3 slightlySmallerExtents = new FPVector3(
                FPInt64.FromRaw(long.MaxValue - 1L),
                FPInt64.MaxValue,
                FPInt64.MaxValue);
            FPOBB3D a = new FPOBB3D(centerA, maximumExtents, FPQuaternion.Identity);
            FPOBB3D touching = new FPOBB3D(centerB, maximumExtents, FPQuaternion.Identity);
            FPOBB3D separated = new FPOBB3D(centerB, slightlySmallerExtents, FPQuaternion.Identity);

            Assert.That(FPGeometry3D.OBBOverlap(a, touching), Is.True);
            Assert.That(FPGeometry3D.OBBOverlap(a, separated), Is.False);
        }

        [Test]
        public void TryRayObb_TransformsRayIntoLocalSpace()
        {
            FPOBB3D obb = new FPOBB3D(
                FPVector3.Zero,
                new FPVector3(2, 1, 1),
                FPQuaternion.AngleAxis(FPInt64.HalfPi, FPVector3.Forward));
            FPRay3D ray = new FPRay3D(new FPVector3(0, -5, 0), FPVector3.Up);

            bool success = FPGeometry3D.TryRayOBB(ray, obb, out FPInt64 t);

            Assert.That(success, Is.True);
            Assert.That(
                FPInt64.Abs(t - FPInt64.FromInt(3)).RawValue,
                Is.LessThanOrEqualTo(2L));
        }

        [Test]
        public void TryRayObb_UnrepresentableRotatedOriginFailsCleanly()
        {
            FPOBB3D obb = new FPOBB3D(
                FPVector3.Zero,
                FPVector3.One,
                FPQuaternion.AngleAxis(FPInt64.Pi / 4, FPVector3.Forward));
            FPRay3D ray = new FPRay3D(
                new FPVector3(2_000_000_000, -2_000_000_000, 0),
                FPVector3.Left);

            bool success = FPGeometry3D.TryRayOBB(ray, obb, out FPInt64 t);

            Assert.That(success, Is.False);
            Assert.That(t, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void ClosestPointQueries_ReturnExpectedSurfacePoints()
        {
            FPAABB3D bounds = new FPAABB3D(new FPVector3(-2, -1, -3), new FPVector3(2, 1, 3));
            FPSphere sphere = new FPSphere(FPVector3.Zero, 3);

            Assert.That(
                FPGeometry3D.ClosestPointOnAABB(bounds, new FPVector3(4, 0, -4)),
                Is.EqualTo(new FPVector3(2, 0, -3)));
            Assert.That(
                FPGeometry3D.ClosestPointOnSphere(sphere, FPVector3.Zero),
                Is.EqualTo(new FPVector3(3, 0, 0)));
        }

        [Test]
        public void ClosestPointOnSphere_LargeDirection_RemainsOnSurface()
        {
            FPSphere sphere = new FPSphere(FPVector3.Zero, 1);

            bool success = FPGeometry3D.TryClosestPointOnSphere(
                sphere,
                new FPVector3(50_000, 0, 0),
                out FPVector3 closest);

            Assert.That(success, Is.True);
            Assert.That(closest, Is.EqualTo(FPVector3.Right));
            Assert.That(
                FPGeometry3D.ClosestPointOnSphere(sphere, new FPVector3(50_000, 0, 0)),
                Is.EqualTo(closest));
        }

        [Test]
        public void TryClosestPointOnSphere_UnrepresentableDeltaFailsCleanly()
        {
            FPSphere sphere = new FPSphere(
                new FPVector3(FPInt64.MinValue, FPInt64.Zero, FPInt64.Zero),
                1);
            FPVector3 point = new FPVector3(FPInt64.MaxValue, FPInt64.Zero, FPInt64.Zero);

            bool success = FPGeometry3D.TryClosestPointOnSphere(sphere, point, out FPVector3 closest);

            Assert.That(success, Is.False);
            Assert.That(closest, Is.EqualTo(FPVector3.Zero));
            Assert.That(
                () => FPGeometry3D.ClosestPointOnSphere(sphere, point),
                Throws.TypeOf<OverflowException>());
        }
    }
}
