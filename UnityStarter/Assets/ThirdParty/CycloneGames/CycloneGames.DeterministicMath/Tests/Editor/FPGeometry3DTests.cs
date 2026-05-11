using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPGeometry3DTests
    {
        [Test]
        public void SphereOverlap_SameRadius_Touching()
        {
            var a = new FPSphere(FPVector3.Zero, 5);
            var b = new FPSphere(new FPVector3(10, 0, 0), 5);
            Assert.That(FPRaycast3D.SphereOverlap(a, b), Is.True);
        }

        [Test]
        public void SphereOverlap_FarApart_NotOverlapping()
        {
            var a = new FPSphere(FPVector3.Zero, 1);
            var b = new FPSphere(new FPVector3(10, 0, 0), 1);
            Assert.That(FPRaycast3D.SphereOverlap(a, b), Is.False);
        }

        [Test]
        public void BoundsOverlap_Touching_Overlaps()
        {
            var a = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));
            var b = new FPBounds(new FPVector3(5, 5, 5), new FPVector3(10, 10, 10));
            Assert.That(FPRaycast3D.BoundsOverlap(a, b), Is.True);
        }

        [Test]
        public void BoundsOverlap_Separated_NotOverlapping()
        {
            var a = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(1, 1, 1));
            var b = new FPBounds(new FPVector3(2, 0, 0), new FPVector3(3, 1, 1));
            Assert.That(FPRaycast3D.BoundsOverlap(a, b), Is.False);
        }

        [Test]
        public void Vector_Division_ByScalar_Is_ComponentWise()
        {
            var value = new FPVector3(10, -20, 30);
            var result = value / FPInt64.FromInt(10);

            Assert.That(result.X.RawValue, Is.EqualTo(FPInt64.FromInt(1).RawValue));
            Assert.That(result.Y.RawValue, Is.EqualTo(FPInt64.FromInt(-2).RawValue));
            Assert.That(result.Z.RawValue, Is.EqualTo(FPInt64.FromInt(3).RawValue));
        }

        [Test]
        public void RaySphere_HitCenter_ReturnsZero()
        {
            var sphere = new FPSphere(FPVector3.Zero, 5);
            var ray = new FPRay(new FPVector3(10, 0, 0), new FPVector3(-1, 0, 0));
            var t = FPRaycast3D.RaySphere(ray, sphere);
            Assert.That(t.RawValue, Is.GreaterThan(0));
        }

        [Test]
        public void RaySphere_Away_NoHit()
        {
            var sphere = new FPSphere(FPVector3.Zero, 5);
            var ray = new FPRay(new FPVector3(10, 0, 0), new FPVector3(1, 0, 0));
            var t = FPRaycast3D.RaySphere(ray, sphere);
            Assert.That(t.RawValue, Is.EqualTo(FPInt64.MinusOne.RawValue));
        }

        [Test]
        public void RayBounds_Hit_Returns_PositiveT()
        {
            var bounds = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));
            var ray = new FPRay(new FPVector3(-1, 2, 2), new FPVector3(1, 0, 0));
            var t = FPRaycast3D.RayBounds(ray, bounds);
            Assert.That(t.RawValue, Is.GreaterThan(0));
        }

        [Test]
        public void RayBounds_Parallel_NoHit()
        {
            var bounds = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));
            var ray = new FPRay(new FPVector3(-1, 10, 0), new FPVector3(1, 0, 0));
            var t = FPRaycast3D.RayBounds(ray, bounds);
            Assert.That(t.RawValue, Is.EqualTo(FPInt64.MinusOne.RawValue));
        }

        [Test]
        public void OBB_Identity_SameAsAABB()
        {
            var bounds = new FPBounds(new FPVector3(0, 0, 0), new FPVector3(5, 5, 5));
            var obb = new FPOBB(new FPVector3(2, 2, 2), new FPVector3(2, 2, 2), FPQuaternion.Identity);
            var ray = new FPRay(new FPVector3(-1, 2, 2), new FPVector3(1, 0, 0));
            var tOBB = FPRaycast3D.RayOBB(ray, obb);
            Assert.That(tOBB.RawValue, Is.GreaterThan(0));
        }

        [Test]
        public void OBBOverlap_Identical_Overlaps()
        {
            var a = new FPOBB(FPVector3.Zero, FPVector3.One, FPQuaternion.Identity);
            var b = new FPOBB(FPVector3.Zero, FPVector3.One, FPQuaternion.Identity);
            Assert.That(FPRaycast3D.OBBOverlap(a, b), Is.True);
        }

        [Test]
        public void Matrix_Translate_Multiply_Order()
        {
            var trans = FPMatrix4x4.Translate(new FPVector3(5, 0, 0));
            var point = new FPVector3(1, 2, 3);
            var result = trans * point;
            Assert.That(result.X.RawValue, Is.GreaterThan(FPInt64.FromInt(5).RawValue));
            Assert.That(result.Y.RawValue, Is.EqualTo(FPInt64.FromInt(2).RawValue));
        }

        [Test]
        public void Matrix_Rotate_Preserves_Magnitude()
        {
            var rot = FPQuaternion.Euler(0, FPInt64.Pi / 4, 0);
            var mat = FPMatrix4x4.Rotate(rot);
            var v = new FPVector3(1, 0, 0);
            var result = mat * v;
            var diff = FPInt64.Abs(result.SqrMagnitude - FPInt64.OneValue);
            Assert.That(diff.RawValue, Is.LessThan(FPInt64.FromRaw(10000).RawValue));
        }
    }
}
