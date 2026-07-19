using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic 4x4 matrix for 3D transforms.
    /// Fields are declared in column-major memory order and multiplication uses column vectors.
    /// Multiply: result = lhs * rhs applies rhs first, then lhs.
    /// </summary>
    public readonly struct FPMatrix4x4 : IEquatable<FPMatrix4x4>
    {
        private const long INVERSE_VALIDATION_TOLERANCE_RAW = 65536L;

        // Column-major storage: element[row + column * 4].
        // M00 M10 M20 M30   column 0
        // M01 M11 M21 M31   column 1
        // M02 M12 M22 M32   column 2
        // M03 M13 M23 M33   column 3

        public readonly FPInt64 M00;
        public readonly FPInt64 M10;
        public readonly FPInt64 M20;
        public readonly FPInt64 M30;
        public readonly FPInt64 M01;
        public readonly FPInt64 M11;
        public readonly FPInt64 M21;
        public readonly FPInt64 M31;
        public readonly FPInt64 M02;
        public readonly FPInt64 M12;
        public readonly FPInt64 M22;
        public readonly FPInt64 M32;
        public readonly FPInt64 M03;
        public readonly FPInt64 M13;
        public readonly FPInt64 M23;
        public readonly FPInt64 M33;

        public FPMatrix4x4(
            FPInt64 m00, FPInt64 m01, FPInt64 m02, FPInt64 m03,
            FPInt64 m10, FPInt64 m11, FPInt64 m12, FPInt64 m13,
            FPInt64 m20, FPInt64 m21, FPInt64 m22, FPInt64 m23,
            FPInt64 m30, FPInt64 m31, FPInt64 m32, FPInt64 m33)
        {
            M00 = m00;
            M01 = m01;
            M02 = m02;
            M03 = m03;
            M10 = m10;
            M11 = m11;
            M12 = m12;
            M13 = m13;
            M20 = m20;
            M21 = m21;
            M22 = m22;
            M23 = m23;
            M30 = m30;
            M31 = m31;
            M32 = m32;
            M33 = m33;
        }

        // ---- Accessors ----

        public FPInt64 this[int row, int col]
        {
            get
            {
                if (unchecked((uint)row) >= 4U)
                {
                    throw new ArgumentOutOfRangeException(nameof(row));
                }

                if (unchecked((uint)col) >= 4U)
                {
                    throw new ArgumentOutOfRangeException(nameof(col));
                }

                return (row * 4 + col) switch
                {
                    0 => M00, 1 => M01, 2 => M02, 3 => M03,
                    4 => M10, 5 => M11, 6 => M12, 7 => M13,
                    8 => M20, 9 => M21, 10 => M22, 11 => M23,
                    12 => M30, 13 => M31, 14 => M32, 15 => M33,
                    _ => throw new IndexOutOfRangeException()
                };
            }
        }

        // ---- Static Constructors ----

        public static readonly FPMatrix4x4 Identity = new FPMatrix4x4(
            FPInt64.One, FPInt64.Zero, FPInt64.Zero, FPInt64.Zero,
            FPInt64.Zero, FPInt64.One, FPInt64.Zero, FPInt64.Zero,
            FPInt64.Zero, FPInt64.Zero, FPInt64.One, FPInt64.Zero,
            FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.One
        );

        public static readonly FPMatrix4x4 Zero = default;

        /// <summary>Translation matrix.</summary>
        public static FPMatrix4x4 Translate(FPVector3 translation)
        {
            return new FPMatrix4x4(
                FPInt64.One, FPInt64.Zero, FPInt64.Zero, translation.X,
                FPInt64.Zero, FPInt64.One, FPInt64.Zero, translation.Y,
                FPInt64.Zero, FPInt64.Zero, FPInt64.One, translation.Z,
                FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.One);
        }

        /// <summary>Non-uniform scale matrix.</summary>
        public static FPMatrix4x4 Scale(FPVector3 scale)
        {
            return new FPMatrix4x4(
                scale.X, FPInt64.Zero, FPInt64.Zero, FPInt64.Zero,
                FPInt64.Zero, scale.Y, FPInt64.Zero, FPInt64.Zero,
                FPInt64.Zero, FPInt64.Zero, scale.Z, FPInt64.Zero,
                FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.One);
        }

        /// <summary>Rotation matrix from a quaternion.</summary>
        public static FPMatrix4x4 Rotate(FPQuaternion q)
        {
            if (!q.TryNormalize(out FPQuaternion normalized))
            {
                throw new ArgumentException("Rotation must be a non-zero quaternion.", nameof(q));
            }

            q = normalized;
            FPInt64 qx = q.X;
            FPInt64 qy = q.Y;
            FPInt64 qz = q.Z;
            FPInt64 qw = q.W;
            FPInt64 x2 = qx + qx;
            FPInt64 y2 = qy + qy;
            FPInt64 z2 = qz + qz;
            FPInt64 xx2 = qx * x2;
            FPInt64 yy2 = qy * y2;
            FPInt64 zz2 = qz * z2;
            FPInt64 xy2 = qx * y2;
            FPInt64 xz2 = qx * z2;
            FPInt64 yz2 = qy * z2;
            FPInt64 wx2 = qw * x2;
            FPInt64 wy2 = qw * y2;
            FPInt64 wz2 = qw * z2;

            return new FPMatrix4x4(
                FPInt64.One - yy2 - zz2, xy2 - wz2, xz2 + wy2, FPInt64.Zero,
                xy2 + wz2, FPInt64.One - xx2 - zz2, yz2 - wx2, FPInt64.Zero,
                xz2 - wy2, yz2 + wx2, FPInt64.One - xx2 - yy2, FPInt64.Zero,
                FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.One);
        }

        /// <summary>TRS: Translation * Rotation * Scale.</summary>
        public static FPMatrix4x4 TRS(FPVector3 translation, FPQuaternion rotation, FPVector3 scale)
        {
            FPMatrix4x4 r = Rotate(rotation);
            return new FPMatrix4x4(
                r.M00 * scale.X, r.M01 * scale.Y, r.M02 * scale.Z, translation.X,
                r.M10 * scale.X, r.M11 * scale.Y, r.M12 * scale.Z, translation.Y,
                r.M20 * scale.X, r.M21 * scale.Y, r.M22 * scale.Z, translation.Z,
                FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.One);
        }

        /// <summary>Computes the inverse of this matrix.</summary>
        public FPMatrix4x4 Inverse
        {
            get
            {
                if (!TryInverse(out FPMatrix4x4 inverse))
                {
                    throw new InvalidOperationException("The matrix is singular or outside the supported inverse domain.");
                }

                return inverse;
            }
        }

        /// <summary>Tries to compute the inverse. Returns false for singular or near-singular matrices.</summary>
        public bool TryInverse(out FPMatrix4x4 inverse)
        {
            FPInt64 det = Determinant();
            if (det.RawValue == 0 ||
                (FPInt64.TryAbs(det, out FPInt64 absoluteDeterminant) && absoluteDeterminant.RawValue < 100))
            {
                inverse = default;
                return false;
            }

            FPInt64 invDet = FPInt64.One / det;
            FPMatrix4x4 candidate = new FPMatrix4x4(
                (M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31)) * invDet,
                -(M01 * (M22 * M33 - M23 * M32) - M02 * (M21 * M33 - M23 * M31) + M03 * (M21 * M32 - M22 * M31)) * invDet,
                (M01 * (M12 * M33 - M13 * M32) - M02 * (M11 * M33 - M13 * M31) + M03 * (M11 * M32 - M12 * M31)) * invDet,
                -(M01 * (M12 * M23 - M13 * M22) - M02 * (M11 * M23 - M13 * M21) + M03 * (M11 * M22 - M12 * M21)) * invDet,

                -(M10 * (M22 * M33 - M23 * M32) - M12 * (M20 * M33 - M23 * M30) + M13 * (M20 * M32 - M22 * M30)) * invDet,
                (M00 * (M22 * M33 - M23 * M32) - M02 * (M20 * M33 - M23 * M30) + M03 * (M20 * M32 - M22 * M30)) * invDet,
                -(M00 * (M12 * M33 - M13 * M32) - M02 * (M10 * M33 - M13 * M30) + M03 * (M10 * M32 - M12 * M30)) * invDet,
                (M00 * (M12 * M23 - M13 * M22) - M02 * (M10 * M23 - M13 * M20) + M03 * (M10 * M22 - M12 * M20)) * invDet,

                (M10 * (M21 * M33 - M23 * M31) - M11 * (M20 * M33 - M23 * M30) + M13 * (M20 * M31 - M21 * M30)) * invDet,
                -(M00 * (M21 * M33 - M23 * M31) - M01 * (M20 * M33 - M23 * M30) + M03 * (M20 * M31 - M21 * M30)) * invDet,
                (M00 * (M11 * M33 - M13 * M31) - M01 * (M10 * M33 - M13 * M30) + M03 * (M10 * M31 - M11 * M30)) * invDet,
                -(M00 * (M11 * M23 - M13 * M21) - M01 * (M10 * M23 - M13 * M20) + M03 * (M10 * M21 - M11 * M20)) * invDet,

                -(M10 * (M21 * M32 - M22 * M31) - M11 * (M20 * M32 - M22 * M30) + M12 * (M20 * M31 - M21 * M30)) * invDet,
                (M00 * (M21 * M32 - M22 * M31) - M01 * (M20 * M32 - M22 * M30) + M02 * (M20 * M31 - M21 * M30)) * invDet,
                -(M00 * (M11 * M32 - M12 * M31) - M01 * (M10 * M32 - M12 * M30) + M02 * (M10 * M31 - M11 * M30)) * invDet,
                (M00 * (M11 * M22 - M12 * M21) - M01 * (M10 * M22 - M12 * M20) + M02 * (M10 * M21 - M11 * M20)) * invDet);

            if (!IsApproximatelyIdentityProduct(this, candidate) ||
                !IsApproximatelyIdentityProduct(candidate, this))
            {
                inverse = default;
                return false;
            }

            inverse = candidate;
            return true;
        }

        private static bool IsApproximatelyIdentityProduct(in FPMatrix4x4 left, in FPMatrix4x4 right)
        {
            for (int row = 0; row < 4; row++)
            {
                for (int column = 0; column < 4; column++)
                {
                    if (!TryGetProductElement(left, right, row, column, out FPInt64 actual))
                    {
                        return false;
                    }

                    FPInt64 expected = row == column ? FPInt64.One : FPInt64.Zero;
                    if (!FPInt64.TrySubtract(actual, expected, out FPInt64 difference) ||
                        !FPInt64.TryAbs(difference, out FPInt64 absoluteDifference) ||
                        absoluteDifference.RawValue > INVERSE_VALIDATION_TOLERANCE_RAW)
                    {
                        return false;
                    }
                }
            }

            return true;
        }

        private static bool TryGetProductElement(
            in FPMatrix4x4 left,
            in FPMatrix4x4 right,
            int row,
            int column,
            out FPInt64 result)
        {
            if (!FPInt64.TryMultiply(left[row, 0], right[0, column], out FPInt64 p0) ||
                !FPInt64.TryMultiply(left[row, 1], right[1, column], out FPInt64 p1) ||
                !FPInt64.TryMultiply(left[row, 2], right[2, column], out FPInt64 p2) ||
                !FPInt64.TryMultiply(left[row, 3], right[3, column], out FPInt64 p3) ||
                !FPInt64.TryAdd(p0, p1, out FPInt64 sum01) ||
                !FPInt64.TryAdd(p2, p3, out FPInt64 sum23))
            {
                result = default;
                return false;
            }

            return FPInt64.TryAdd(sum01, sum23, out result);
        }

        /// <summary>Scalar determinant of this matrix.</summary>
        public FPInt64 Determinant()
        {
            return M00 * (M11 * (M22 * M33 - M23 * M32) - M12 * (M21 * M33 - M23 * M31) + M13 * (M21 * M32 - M22 * M31))
                 - M01 * (M10 * (M22 * M33 - M23 * M32) - M12 * (M20 * M33 - M23 * M30) + M13 * (M20 * M32 - M22 * M30))
                 + M02 * (M10 * (M21 * M33 - M23 * M31) - M11 * (M20 * M33 - M23 * M30) + M13 * (M20 * M31 - M21 * M30))
                 - M03 * (M10 * (M21 * M32 - M22 * M31) - M11 * (M20 * M32 - M22 * M30) + M12 * (M20 * M31 - M21 * M30));
        }

        /// <summary>
        /// Perspective projection matrix using column vectors, a right-handed view space, and [0, 1] depth.
        /// </summary>
        public static FPMatrix4x4 Perspective(FPInt64 fovRadians, FPInt64 aspect, FPInt64 near, FPInt64 far)
        {
            if (fovRadians.RawValue <= 0 || fovRadians >= FPInt64.Pi)
            {
                throw new ArgumentOutOfRangeException(nameof(fovRadians), "Field of view must be in (0, Pi).");
            }

            if (aspect.RawValue <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(aspect), "Aspect ratio must be positive.");
            }

            if (near.RawValue <= 0)
            {
                throw new ArgumentOutOfRangeException(nameof(near), "Near distance must be positive.");
            }

            if (far <= near)
            {
                throw new ArgumentOutOfRangeException(nameof(far), "Far distance must be greater than near distance.");
            }

            FPMath.SinCos(fovRadians / 2, out FPInt64 sinHalf, out FPInt64 cosHalf);
            if (!FPInt64.TryDivide(cosHalf, sinHalf, out FPInt64 cotHalf) ||
                !FPInt64.TryDivide(cotHalf, aspect, out FPInt64 horizontalScale) ||
                !FPInt64.TrySubtract(near, far, out FPInt64 depthRange) ||
                !FPInt64.TryDivide(far, depthRange, out FPInt64 depthScale) ||
                !FPInt64.TryMultiplyDivide(far, near, depthRange, out FPInt64 depthOffset))
            {
                throw new OverflowException("Perspective parameters produce values outside the Q32.32 range.");
            }

            return new FPMatrix4x4(
                horizontalScale, FPInt64.Zero, FPInt64.Zero, FPInt64.Zero,
                FPInt64.Zero, cotHalf, FPInt64.Zero, FPInt64.Zero,
                FPInt64.Zero, FPInt64.Zero, depthScale, depthOffset,
                FPInt64.Zero, FPInt64.Zero, FPInt64.MinusOne, FPInt64.Zero);
        }

        // ---- Operators ----

        public static FPMatrix4x4 operator *(FPMatrix4x4 a, FPMatrix4x4 b)
        {
            return new FPMatrix4x4(
                a.M00 * b.M00 + a.M01 * b.M10 + a.M02 * b.M20 + a.M03 * b.M30,
                a.M00 * b.M01 + a.M01 * b.M11 + a.M02 * b.M21 + a.M03 * b.M31,
                a.M00 * b.M02 + a.M01 * b.M12 + a.M02 * b.M22 + a.M03 * b.M32,
                a.M00 * b.M03 + a.M01 * b.M13 + a.M02 * b.M23 + a.M03 * b.M33,

                a.M10 * b.M00 + a.M11 * b.M10 + a.M12 * b.M20 + a.M13 * b.M30,
                a.M10 * b.M01 + a.M11 * b.M11 + a.M12 * b.M21 + a.M13 * b.M31,
                a.M10 * b.M02 + a.M11 * b.M12 + a.M12 * b.M22 + a.M13 * b.M32,
                a.M10 * b.M03 + a.M11 * b.M13 + a.M12 * b.M23 + a.M13 * b.M33,

                a.M20 * b.M00 + a.M21 * b.M10 + a.M22 * b.M20 + a.M23 * b.M30,
                a.M20 * b.M01 + a.M21 * b.M11 + a.M22 * b.M21 + a.M23 * b.M31,
                a.M20 * b.M02 + a.M21 * b.M12 + a.M22 * b.M22 + a.M23 * b.M32,
                a.M20 * b.M03 + a.M21 * b.M13 + a.M22 * b.M23 + a.M23 * b.M33,

                a.M30 * b.M00 + a.M31 * b.M10 + a.M32 * b.M20 + a.M33 * b.M30,
                a.M30 * b.M01 + a.M31 * b.M11 + a.M32 * b.M21 + a.M33 * b.M31,
                a.M30 * b.M02 + a.M31 * b.M12 + a.M32 * b.M22 + a.M33 * b.M32,
                a.M30 * b.M03 + a.M31 * b.M13 + a.M32 * b.M23 + a.M33 * b.M33
            );
        }

        /// <summary>
        /// Transforms an affine point with translation and without a homogeneous divide.
        /// Scalar operations use the module's wrapping arithmetic policy.
        /// </summary>
        public FPVector3 TransformPoint(FPVector3 point)
        {
            return new FPVector3(
                M00 * point.X + M01 * point.Y + M02 * point.Z + M03,
                M10 * point.X + M11 * point.Y + M12 * point.Z + M13,
                M20 * point.X + M21 * point.Y + M22 * point.Z + M23);
        }

        /// <summary>Tries to transform an affine point using checked fixed-point intermediates.</summary>
        public bool TryTransformPoint(FPVector3 point, out FPVector3 result)
        {
            if (!TryTransformCoordinate(M00, M01, M02, M03, point, out FPInt64 x) ||
                !TryTransformCoordinate(M10, M11, M12, M13, point, out FPInt64 y) ||
                !TryTransformCoordinate(M20, M21, M22, M23, point, out FPInt64 z))
            {
                result = default;
                return false;
            }

            result = new FPVector3(x, y, z);
            return true;
        }

        /// <summary>
        /// Transforms a direction with the linear 3x3 portion of the matrix and ignores translation.
        /// Scalar operations use the module's wrapping arithmetic policy.
        /// </summary>
        public FPVector3 TransformDirection(FPVector3 direction)
        {
            return new FPVector3(
                M00 * direction.X + M01 * direction.Y + M02 * direction.Z,
                M10 * direction.X + M11 * direction.Y + M12 * direction.Z,
                M20 * direction.X + M21 * direction.Y + M22 * direction.Z);
        }

        /// <summary>Tries to transform a direction using checked fixed-point intermediates.</summary>
        public bool TryTransformDirection(FPVector3 direction, out FPVector3 result)
        {
            if (!TryTransformDirectionCoordinate(M00, M01, M02, direction, out FPInt64 x) ||
                !TryTransformDirectionCoordinate(M10, M11, M12, direction, out FPInt64 y) ||
                !TryTransformDirectionCoordinate(M20, M21, M22, direction, out FPInt64 z))
            {
                result = default;
                return false;
            }

            result = new FPVector3(x, y, z);
            return true;
        }

        /// <summary>
        /// Projects a point with all four matrix rows and performs the homogeneous divide.
        /// </summary>
        /// <exception cref="InvalidOperationException">
        /// The homogeneous divisor is zero or a checked intermediate cannot be represented in Q32.32.
        /// </exception>
        public FPVector3 ProjectPoint(FPVector3 point)
        {
            if (!TryProjectPoint(point, out FPVector3 result))
            {
                throw new InvalidOperationException(
                    "The point cannot be projected because the homogeneous divisor is zero or the result is outside the Q32.32 range.");
            }

            return result;
        }

        /// <summary>Tries to project a point using checked fixed-point intermediates.</summary>
        public bool TryProjectPoint(FPVector3 point, out FPVector3 result)
        {
            if (!TryTransformCoordinate(M00, M01, M02, M03, point, out FPInt64 numeratorX) ||
                !TryTransformCoordinate(M10, M11, M12, M13, point, out FPInt64 numeratorY) ||
                !TryTransformCoordinate(M20, M21, M22, M23, point, out FPInt64 numeratorZ) ||
                !TryTransformCoordinate(M30, M31, M32, M33, point, out FPInt64 w) ||
                w.RawValue == 0 ||
                !FPInt64.TryDivide(numeratorX, w, out FPInt64 x) ||
                !FPInt64.TryDivide(numeratorY, w, out FPInt64 y) ||
                !FPInt64.TryDivide(numeratorZ, w, out FPInt64 z))
            {
                result = default;
                return false;
            }

            result = new FPVector3(x, y, z);
            return true;
        }

        private static bool TryTransformCoordinate(
            FPInt64 coefficientX,
            FPInt64 coefficientY,
            FPInt64 coefficientZ,
            FPInt64 offset,
            in FPVector3 value,
            out FPInt64 result)
        {
            if (!TryTransformDirectionCoordinate(
                    coefficientX,
                    coefficientY,
                    coefficientZ,
                    value,
                    out FPInt64 linear) ||
                !FPInt64.TryAdd(linear, offset, out result))
            {
                result = default;
                return false;
            }

            return true;
        }

        private static bool TryTransformDirectionCoordinate(
            FPInt64 coefficientX,
            FPInt64 coefficientY,
            FPInt64 coefficientZ,
            in FPVector3 value,
            out FPInt64 result)
        {
            if (!FPInt64.TryMultiply(coefficientX, value.X, out FPInt64 productX) ||
                !FPInt64.TryMultiply(coefficientY, value.Y, out FPInt64 productY) ||
                !FPInt64.TryMultiply(coefficientZ, value.Z, out FPInt64 productZ) ||
                !FPInt64.TryAdd(productX, productY, out FPInt64 sumXY) ||
                !FPInt64.TryAdd(sumXY, productZ, out result))
            {
                result = default;
                return false;
            }

            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPMatrix4x4 a, FPMatrix4x4 b) =>
            a.M00 == b.M00 && a.M01 == b.M01 && a.M02 == b.M02 && a.M03 == b.M03 &&
            a.M10 == b.M10 && a.M11 == b.M11 && a.M12 == b.M12 && a.M13 == b.M13 &&
            a.M20 == b.M20 && a.M21 == b.M21 && a.M22 == b.M22 && a.M23 == b.M23 &&
            a.M30 == b.M30 && a.M31 == b.M31 && a.M32 == b.M32 && a.M33 == b.M33;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPMatrix4x4 a, FPMatrix4x4 b) => !(a == b);

        // ---- Equality ----

        public bool Equals(FPMatrix4x4 other) => this == other;
        public override bool Equals(object obj) => obj is FPMatrix4x4 m && this == m;
        public override int GetHashCode() =>
            M00.GetHashCode() ^ M01.GetHashCode() ^ M02.GetHashCode() ^ M03.GetHashCode() ^
            M10.GetHashCode() ^ M11.GetHashCode() ^ M12.GetHashCode() ^ M13.GetHashCode() ^
            M20.GetHashCode() ^ M21.GetHashCode() ^ M22.GetHashCode() ^ M23.GetHashCode() ^
            M30.GetHashCode() ^ M31.GetHashCode() ^ M32.GetHashCode() ^ M33.GetHashCode();
        public override string ToString() =>
            $"[{M00},{M01},{M02},{M03};{M10},{M11},{M12},{M13};{M20},{M21},{M22},{M23};{M30},{M31},{M32},{M33}]";
    }
}
