using System;

using NUnit.Framework;

namespace CycloneGames.DeterministicMath.Tests.Editor
{
    public sealed class FPMatrix4x4Tests
    {
        private const long VECTOR_TOLERANCE_SQR_RAW = 1_000_000L;

        [Test]
        public void Trs_AppliesScaleThenRotationThenTranslation()
        {
            FPVector3 translation = new FPVector3(10, -4, 7);
            FPQuaternion rotation = FPQuaternion.Euler(
                FPInt64.FromDouble(0.2),
                FPInt64.FromDouble(-0.4),
                FPInt64.FromDouble(0.7));
            FPVector3 scale = new FPVector3(2, 3, 4);
            FPVector3 point = new FPVector3(1, -2, 3);
            FPMatrix4x4 matrix = FPMatrix4x4.TRS(translation, rotation, scale);

            FPVector3 scaled = new FPVector3(point.X * scale.X, point.Y * scale.Y, point.Z * scale.Z);
            FPVector3 expected = translation + rotation * scaled;
            FPVector3 actual = matrix.TransformPoint(point);

            AssertVectorClose(actual, expected);
        }

        [Test]
        public void Trs_MatchesComposedMatrices()
        {
            FPVector3 translation = new FPVector3(-2, 5, 9);
            FPQuaternion rotation = FPQuaternion.Euler(
                FPInt64.FromDouble(-0.3),
                FPInt64.FromDouble(0.6),
                FPInt64.FromDouble(0.1));
            FPVector3 scale = new FPVector3(3, 2, 5);
            FPVector3 point = new FPVector3(4, -1, 2);

            FPVector3 direct = FPMatrix4x4.TRS(translation, rotation, scale).TransformPoint(point);
            FPMatrix4x4 composedMatrix =
                FPMatrix4x4.Translate(translation) * FPMatrix4x4.Rotate(rotation) * FPMatrix4x4.Scale(scale);
            FPVector3 composed = composedMatrix.TransformPoint(point);

            AssertVectorClose(direct, composed);
        }

        [Test]
        public void PointAndDirectionTransforms_ApplyTranslationOnlyToPoint()
        {
            FPMatrix4x4 matrix = FPMatrix4x4.Translate(new FPVector3(10, -4, 7));
            FPVector3 value = new FPVector3(2, 3, 5);

            FPVector3 point = matrix.TransformPoint(value);
            FPVector3 direction = matrix.TransformDirection(value);
            bool pointSuccess = matrix.TryTransformPoint(value, out FPVector3 checkedPoint);
            bool directionSuccess = matrix.TryTransformDirection(value, out FPVector3 checkedDirection);

            Assert.That(point, Is.EqualTo(new FPVector3(12, -1, 12)));
            Assert.That(direction, Is.EqualTo(value));
            Assert.That(pointSuccess, Is.True);
            Assert.That(checkedPoint, Is.EqualTo(point));
            Assert.That(directionSuccess, Is.True);
            Assert.That(checkedDirection, Is.EqualTo(direction));
        }

        [Test]
        public void ProjectPoint_ZeroHomogeneousDivisorFailsExplicitly()
        {
            FPMatrix4x4 matrix = new FPMatrix4x4(
                FPInt64.One, FPInt64.Zero, FPInt64.Zero, FPInt64.Zero,
                FPInt64.Zero, FPInt64.One, FPInt64.Zero, FPInt64.Zero,
                FPInt64.Zero, FPInt64.Zero, FPInt64.One, FPInt64.Zero,
                FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.Zero);

            bool success = matrix.TryProjectPoint(FPVector3.One, out FPVector3 result);

            Assert.That(success, Is.False);
            Assert.That(result, Is.EqualTo(FPVector3.Zero));
            Assert.That(
                () => matrix.ProjectPoint(FPVector3.One),
                Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void CheckedTransformMethods_RejectOverflowingIntermediates()
        {
            FPMatrix4x4 matrix = FPMatrix4x4.Scale(
                new FPVector3(FPInt64.MaxValue, FPInt64.MaxValue, FPInt64.MaxValue));
            FPVector3 value = new FPVector3(2, 2, 2);

            bool pointSuccess = matrix.TryTransformPoint(value, out FPVector3 point);
            bool directionSuccess = matrix.TryTransformDirection(value, out FPVector3 direction);
            bool projectionSuccess = matrix.TryProjectPoint(value, out FPVector3 projection);

            Assert.That(pointSuccess, Is.False);
            Assert.That(point, Is.EqualTo(FPVector3.Zero));
            Assert.That(directionSuccess, Is.False);
            Assert.That(direction, Is.EqualTo(FPVector3.Zero));
            Assert.That(projectionSuccess, Is.False);
            Assert.That(projection, Is.EqualTo(FPVector3.Zero));
        }

        [Test]
        public void TryInverse_RoundTripsAffinePoint()
        {
            FPMatrix4x4 matrix = FPMatrix4x4.TRS(
                new FPVector3(10, -4, 7),
                FPQuaternion.Euler(
                    FPInt64.FromDouble(0.25),
                    FPInt64.FromDouble(-0.5),
                    FPInt64.FromDouble(0.75)),
                new FPVector3(2, 3, 4));
            FPVector3 point = new FPVector3(-3, 6, 2);

            bool success = matrix.TryInverse(out FPMatrix4x4 inverse);
            FPVector3 restored = inverse.TransformPoint(matrix.TransformPoint(point));

            Assert.That(success, Is.True);
            AssertVectorClose(restored, point);
        }

        [Test]
        public void TryInverse_SingularMatrix_FailsWithoutFabricatingAnInverse()
        {
            FPMatrix4x4 singular = FPMatrix4x4.Scale(new FPVector3(1, 0, 1));

            bool success = singular.TryInverse(out FPMatrix4x4 inverse);

            Assert.That(success, Is.False);
            Assert.That(inverse, Is.EqualTo(FPMatrix4x4.Zero));
            Assert.That(() => _ = singular.Inverse, Throws.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void TryInverse_LargeScaleNeverReportsACorruptInverse()
        {
            FPMatrix4x4 matrix = FPMatrix4x4.Scale(new FPVector3(50_000, 50_000, 1));
            FPVector3 point = new FPVector3(1, 1, 1);

            bool success = matrix.TryInverse(out FPMatrix4x4 inverse);

            if (!success)
            {
                Assert.That(inverse, Is.EqualTo(FPMatrix4x4.Zero));
                Assert.That(() => _ = matrix.Inverse, Throws.TypeOf<InvalidOperationException>());
                return;
            }

            AssertVectorClose(inverse.TransformPoint(matrix.TransformPoint(point)), point);
        }

        [Test]
        public void Perspective_UsesRightHandedZeroToOneDepthLayout()
        {
            FPInt64 near = FPInt64.FromDouble(0.5);
            FPInt64 far = FPInt64.FromInt(100);
            FPMatrix4x4 projection = FPMatrix4x4.Perspective(
                FPInt64.Pi / 2,
                FPInt64.FromDouble(16.0 / 9.0),
                near,
                far);
            FPInt64 expectedDepthOffset = far * near / (near - far);

            Assert.That(projection.M23.RawValue, Is.EqualTo(expectedDepthOffset.RawValue));
            Assert.That(projection.M32, Is.EqualTo(FPInt64.MinusOne));
            Assert.That(projection.M33, Is.EqualTo(FPInt64.Zero));
        }

        [Test]
        public void Perspective_MapsNearAndFarPlanesToZeroAndOne()
        {
            FPInt64 near = FPInt64.FromInt(1);
            FPInt64 far = FPInt64.FromInt(100);
            FPMatrix4x4 projection = FPMatrix4x4.Perspective(
                FPInt64.Pi / 2,
                FPInt64.One,
                near,
                far);

            FPVector3 nearProjected = projection.ProjectPoint(new FPVector3(0, 0, -near));
            FPVector3 farProjected = projection.ProjectPoint(new FPVector3(0, 0, -far));

            Assert.That(FPInt64.Abs(nearProjected.Z).RawValue, Is.LessThanOrEqualTo(4L));
            Assert.That(
                FPInt64.Abs(farProjected.Z - FPInt64.One).RawValue,
                Is.LessThanOrEqualTo(128L));
        }

        [Test]
        public void Perspective_LargeNearFarRange_UsesFusedDepthOffset()
        {
            FPInt64 near = FPInt64.FromInt(25_000);
            FPInt64 far = FPInt64.FromInt(100_000);

            FPMatrix4x4 projection = FPMatrix4x4.Perspective(
                FPInt64.Pi / 2,
                FPInt64.One,
                near,
                far);
            FPVector3 nearProjected = projection.ProjectPoint(new FPVector3(0, 0, -near));
            FPVector3 farProjected = projection.ProjectPoint(new FPVector3(0, 0, -far));

            Assert.That(FPInt64.Abs(nearProjected.Z).RawValue, Is.LessThanOrEqualTo(4L));
            Assert.That(
                FPInt64.Abs(farProjected.Z - FPInt64.One).RawValue,
                Is.LessThanOrEqualTo(100_000L));
        }

        [Test]
        public void RotateAndTrs_RejectZeroQuaternion()
        {
            FPQuaternion zero = default;

            Assert.That(
                () => FPMatrix4x4.Rotate(zero),
                Throws.TypeOf<ArgumentException>());
            Assert.That(
                () => FPMatrix4x4.TRS(FPVector3.Zero, zero, FPVector3.One),
                Throws.TypeOf<ArgumentException>());
        }

        [Test]
        public void MatrixIndexer_ReturnsRequestedElements()
        {
            FPMatrix4x4 matrix = FPMatrix4x4.Translate(new FPVector3(3, 4, 5));

            Assert.That(matrix[0, 3], Is.EqualTo(FPInt64.FromInt(3)));
            Assert.That(matrix[1, 3], Is.EqualTo(FPInt64.FromInt(4)));
            Assert.That(matrix[2, 3], Is.EqualTo(FPInt64.FromInt(5)));
            Assert.That(matrix[0, 0], Is.EqualTo(FPInt64.One));
            Assert.That(matrix[3, 3], Is.EqualTo(FPInt64.One));
        }

        [Test]
        public void MatrixIndexer_RejectsInvalidIndices()
        {
            FPMatrix4x4 matrix = FPMatrix4x4.Identity;

            Assert.That(() => _ = matrix[-1, 0], Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _ = matrix[0, -1], Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _ = matrix[4, 0], Throws.TypeOf<ArgumentOutOfRangeException>());
            Assert.That(() => _ = matrix[0, 4], Throws.TypeOf<ArgumentOutOfRangeException>());
        }

        [Test]
        public void MatrixMultiplication_AppliesRightOperandFirst()
        {
            FPMatrix4x4 translate = FPMatrix4x4.Translate(new FPVector3(10, 0, 0));
            FPMatrix4x4 scale = FPMatrix4x4.Scale(new FPVector3(2, 2, 2));
            FPVector3 point = new FPVector3(1, 0, 0);

            FPVector3 result = (translate * scale).TransformPoint(point);

            Assert.That(result, Is.EqualTo(new FPVector3(12, 0, 0)));
        }

        private static void AssertVectorClose(FPVector3 actual, FPVector3 expected)
        {
            Assert.That(
                FPVector3.DistanceSqr(actual, expected).RawValue,
                Is.LessThanOrEqualTo(VECTOR_TOLERANCE_SQR_RAW));
        }
    }
}
