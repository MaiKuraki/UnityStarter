using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic 4x4 matrix for 3D transforms.
    /// Column-major layout matching Unity/HLSL convention.
    /// Multiply: result = lhs * rhs applies rhs first, then lhs.
    /// </summary>
    public struct FPMatrix4x4 : IEquatable<FPMatrix4x4>
    {
        // Column-major: m[row + col*4]
        // m00 m10 m20 m30   col0
        // m01 m11 m21 m31   col1
        // m02 m12 m22 m32   col2
        // m03 m13 m23 m33   col3

        public FPInt64 m00, m10, m20, m30;
        public FPInt64 m01, m11, m21, m31;
        public FPInt64 m02, m12, m22, m32;
        public FPInt64 m03, m13, m23, m33;

        public FPMatrix4x4(
            FPInt64 m00, FPInt64 m01, FPInt64 m02, FPInt64 m03,
            FPInt64 m10, FPInt64 m11, FPInt64 m12, FPInt64 m13,
            FPInt64 m20, FPInt64 m21, FPInt64 m22, FPInt64 m23,
            FPInt64 m30, FPInt64 m31, FPInt64 m32, FPInt64 m33)
        {
            this.m00 = m00; this.m01 = m01; this.m02 = m02; this.m03 = m03;
            this.m10 = m10; this.m11 = m11; this.m12 = m12; this.m13 = m13;
            this.m20 = m20; this.m21 = m21; this.m22 = m22; this.m23 = m23;
            this.m30 = m30; this.m31 = m31; this.m32 = m32; this.m33 = m33;
        }

        // ---- Accessors ----

        public FPInt64 this[int row, int col]
        {
            get
            {
                return (row * 4 + col) switch
                {
                    0 => m00, 1 => m01, 2 => m02, 3 => m03,
                    4 => m10, 5 => m11, 6 => m12, 7 => m13,
                    8 => m20, 9 => m21, 10 => m22, 11 => m23,
                    12 => m30, 13 => m31, 14 => m32, 15 => m33,
                    _ => throw new IndexOutOfRangeException()
                };
            }
        }

        public FPVector3 GetColumn(int col)
        {
            return col switch
            {
                0 => new FPVector3(m00, m10, m20),
                1 => new FPVector3(m01, m11, m21),
                2 => new FPVector3(m02, m12, m22),
                _ => new FPVector3(m03, m13, m23),
            };
        }

        public FPVector3 GetRow(int row)
        {
            return row switch
            {
                0 => new FPVector3(m00, m01, m02),
                1 => new FPVector3(m10, m11, m12),
                2 => new FPVector3(m20, m21, m22),
                _ => new FPVector3(m30, m31, m32),
            };
        }

        // ---- Static Constructors ----

        public static readonly FPMatrix4x4 Identity = new FPMatrix4x4(
            FPInt64.OneValue, FPInt64.Zero, FPInt64.Zero, FPInt64.Zero,
            FPInt64.Zero, FPInt64.OneValue, FPInt64.Zero, FPInt64.Zero,
            FPInt64.Zero, FPInt64.Zero, FPInt64.OneValue, FPInt64.Zero,
            FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.OneValue
        );

        public static readonly FPMatrix4x4 Zero = default;

        /// <summary>Translation matrix.</summary>
        public static FPMatrix4x4 Translate(FPVector3 translation)
        {
            var m = Identity;
            m.m03 = translation.X;
            m.m13 = translation.Y;
            m.m23 = translation.Z;
            return m;
        }

        /// <summary>Uniform scale matrix.</summary>
        public static FPMatrix4x4 Scale(FPVector3 scale)
        {
            var m = Identity;
            m.m00 = scale.X;
            m.m11 = scale.Y;
            m.m22 = scale.Z;
            return m;
        }

        /// <summary>Rotation matrix from a quaternion.</summary>
        public static FPMatrix4x4 Rotate(FPQuaternion q)
        {
            var qx = q.X; var qy = q.Y; var qz = q.Z; var qw = q.W;
            var x2 = qx + qx; var y2 = qy + qy; var z2 = qz + qz;
            var xx2 = qx * x2; var yy2 = qy * y2; var zz2 = qz * z2;
            var xy2 = qx * y2; var xz2 = qx * z2;
            var yz2 = qy * z2; var wx2 = qw * x2;
            var wy2 = qw * y2; var wz2 = qw * z2;

            var m = Identity;
            m.m00 = FPInt64.OneValue - yy2 - zz2;
            m.m10 = xy2 + wz2;
            m.m20 = xz2 - wy2;

            m.m01 = xy2 - wz2;
            m.m11 = FPInt64.OneValue - xx2 - zz2;
            m.m21 = yz2 + wx2;

            m.m02 = xz2 + wy2;
            m.m12 = yz2 - wx2;
            m.m22 = FPInt64.OneValue - xx2 - yy2;

            return m;
        }

        /// <summary>TRS: Translation * Rotation * Scale.</summary>
        public static FPMatrix4x4 TRS(FPVector3 translation, FPQuaternion rotation, FPVector3 scale)
        {
            return Translate(translation) * Rotate(rotation) * Scale(scale);
        }

        /// <summary>Compute the inverse of this matrix. Returns Identity if singular.</summary>
        public FPMatrix4x4 Inverse
        {
            get
            {
                // Compute determinant and cofactors, then transpose
                var det = Determinant();
                if (FPInt64.Abs(det).RawValue < 100) return Identity; // singular or near-singular

                var invDet = FPInt64.OneValue / det;

                return new FPMatrix4x4(
                    // First column (cofactors transposed)
                    (m11 * (m22 * m33 - m23 * m32) - m12 * (m21 * m33 - m23 * m31) + m13 * (m21 * m32 - m22 * m31)) * invDet,
                    -(m01 * (m22 * m33 - m23 * m32) - m02 * (m21 * m33 - m23 * m31) + m03 * (m21 * m32 - m22 * m31)) * invDet,
                    (m01 * (m12 * m33 - m13 * m32) - m02 * (m11 * m33 - m13 * m31) + m03 * (m11 * m32 - m12 * m31)) * invDet,
                    -(m01 * (m12 * m23 - m13 * m22) - m02 * (m11 * m23 - m13 * m21) + m03 * (m11 * m22 - m12 * m21)) * invDet,

                    -(m10 * (m22 * m33 - m23 * m32) - m12 * (m20 * m33 - m23 * m30) + m13 * (m20 * m32 - m22 * m30)) * invDet,
                    (m00 * (m22 * m33 - m23 * m32) - m02 * (m20 * m33 - m23 * m30) + m03 * (m20 * m32 - m22 * m30)) * invDet,
                    -(m00 * (m12 * m33 - m13 * m32) - m02 * (m10 * m33 - m13 * m30) + m03 * (m10 * m32 - m12 * m30)) * invDet,
                    (m00 * (m12 * m23 - m13 * m22) - m02 * (m10 * m23 - m13 * m20) + m03 * (m10 * m22 - m12 * m20)) * invDet,

                    (m10 * (m21 * m33 - m23 * m31) - m11 * (m20 * m33 - m23 * m30) + m13 * (m20 * m31 - m21 * m30)) * invDet,
                    -(m00 * (m21 * m33 - m23 * m31) - m01 * (m20 * m33 - m23 * m30) + m03 * (m20 * m31 - m21 * m30)) * invDet,
                    (m00 * (m11 * m33 - m13 * m31) - m01 * (m10 * m33 - m13 * m30) + m03 * (m10 * m31 - m11 * m30)) * invDet,
                    -(m00 * (m11 * m23 - m13 * m21) - m01 * (m10 * m23 - m13 * m20) + m03 * (m10 * m21 - m11 * m20)) * invDet,

                    -(m10 * (m21 * m32 - m22 * m31) - m11 * (m20 * m32 - m22 * m30) + m12 * (m20 * m31 - m21 * m30)) * invDet,
                    (m00 * (m21 * m32 - m22 * m31) - m01 * (m20 * m32 - m22 * m30) + m02 * (m20 * m31 - m21 * m30)) * invDet,
                    -(m00 * (m11 * m32 - m12 * m31) - m01 * (m10 * m32 - m12 * m30) + m02 * (m10 * m31 - m11 * m30)) * invDet,
                    (m00 * (m11 * m22 - m12 * m21) - m01 * (m10 * m22 - m12 * m20) + m02 * (m10 * m21 - m11 * m20)) * invDet
                );
            }
        }

        /// <summary>Scalar determinant of this matrix.</summary>
        public FPInt64 Determinant()
        {
            return m00 * (m11 * (m22 * m33 - m23 * m32) - m12 * (m21 * m33 - m23 * m31) + m13 * (m21 * m32 - m22 * m31))
                 - m01 * (m10 * (m22 * m33 - m23 * m32) - m12 * (m20 * m33 - m23 * m30) + m13 * (m20 * m32 - m22 * m30))
                 + m02 * (m10 * (m21 * m33 - m23 * m31) - m11 * (m20 * m33 - m23 * m30) + m13 * (m20 * m31 - m21 * m30))
                 - m03 * (m10 * (m21 * m32 - m22 * m31) - m11 * (m20 * m32 - m22 * m30) + m12 * (m20 * m31 - m21 * m30));
        }

        /// <summary>
        /// Perspective projection matrix (right-handed, 0..1 depth, matching Unity).
        /// </summary>
        public static FPMatrix4x4 Perspective(FPInt64 fovRadians, FPInt64 aspect, FPInt64 near, FPInt64 far)
        {
            FPMath.SinCos(fovRadians / 2, out var sinHalf, out var cosHalf);
            var cotHalf = cosHalf / sinHalf;

            var m = Zero;
            m.m00 = cotHalf / aspect;
            m.m11 = cotHalf;
            m.m22 = far / (near - far);
            m.m23 = FPInt64.MinusOne;
            m.m32 = (far * near) / (near - far);
            return m;
        }

        // ---- Operators ----

        public static FPMatrix4x4 operator *(FPMatrix4x4 a, FPMatrix4x4 b)
        {
            return new FPMatrix4x4(
                a.m00 * b.m00 + a.m01 * b.m10 + a.m02 * b.m20 + a.m03 * b.m30,
                a.m00 * b.m01 + a.m01 * b.m11 + a.m02 * b.m21 + a.m03 * b.m31,
                a.m00 * b.m02 + a.m01 * b.m12 + a.m02 * b.m22 + a.m03 * b.m32,
                a.m00 * b.m03 + a.m01 * b.m13 + a.m02 * b.m23 + a.m03 * b.m33,

                a.m10 * b.m00 + a.m11 * b.m10 + a.m12 * b.m20 + a.m13 * b.m30,
                a.m10 * b.m01 + a.m11 * b.m11 + a.m12 * b.m21 + a.m13 * b.m31,
                a.m10 * b.m02 + a.m11 * b.m12 + a.m12 * b.m22 + a.m13 * b.m32,
                a.m10 * b.m03 + a.m11 * b.m13 + a.m12 * b.m23 + a.m13 * b.m33,

                a.m20 * b.m00 + a.m21 * b.m10 + a.m22 * b.m20 + a.m23 * b.m30,
                a.m20 * b.m01 + a.m21 * b.m11 + a.m22 * b.m21 + a.m23 * b.m31,
                a.m20 * b.m02 + a.m21 * b.m12 + a.m22 * b.m22 + a.m23 * b.m32,
                a.m20 * b.m03 + a.m21 * b.m13 + a.m22 * b.m23 + a.m23 * b.m33,

                a.m30 * b.m00 + a.m31 * b.m10 + a.m32 * b.m20 + a.m33 * b.m30,
                a.m30 * b.m01 + a.m31 * b.m11 + a.m32 * b.m21 + a.m33 * b.m31,
                a.m30 * b.m02 + a.m31 * b.m12 + a.m32 * b.m22 + a.m33 * b.m32,
                a.m30 * b.m03 + a.m31 * b.m13 + a.m32 * b.m23 + a.m33 * b.m33
            );
        }

        /// <summary>Transform a point (translation applied).</summary>
        public static FPVector3 operator *(FPMatrix4x4 m, FPVector3 v)
        {
            var w = m.m30 * v.X + m.m31 * v.Y + m.m32 * v.Z + m.m33;
            if (w.RawValue == 0) return FPVector3.Zero;
            var invW = FPInt64.OneValue / w;
            return new FPVector3(
                (m.m00 * v.X + m.m01 * v.Y + m.m02 * v.Z + m.m03) * invW,
                (m.m10 * v.X + m.m11 * v.Y + m.m12 * v.Z + m.m13) * invW,
                (m.m20 * v.X + m.m21 * v.Y + m.m22 * v.Z + m.m23) * invW
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPMatrix4x4 a, FPMatrix4x4 b) =>
            a.m00 == b.m00 && a.m01 == b.m01 && a.m02 == b.m02 && a.m03 == b.m03 &&
            a.m10 == b.m10 && a.m11 == b.m11 && a.m12 == b.m12 && a.m13 == b.m13 &&
            a.m20 == b.m20 && a.m21 == b.m21 && a.m22 == b.m22 && a.m23 == b.m23 &&
            a.m30 == b.m30 && a.m31 == b.m31 && a.m32 == b.m32 && a.m33 == b.m33;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPMatrix4x4 a, FPMatrix4x4 b) => !(a == b);

        // ---- Equality ----

        public bool Equals(FPMatrix4x4 other) => this == other;
        public override bool Equals(object obj) => obj is FPMatrix4x4 m && this == m;
        public override int GetHashCode() =>
            m00.GetHashCode() ^ m01.GetHashCode() ^ m02.GetHashCode() ^ m03.GetHashCode() ^
            m10.GetHashCode() ^ m11.GetHashCode() ^ m12.GetHashCode() ^ m13.GetHashCode() ^
            m20.GetHashCode() ^ m21.GetHashCode() ^ m22.GetHashCode() ^ m23.GetHashCode() ^
            m30.GetHashCode() ^ m31.GetHashCode() ^ m32.GetHashCode() ^ m33.GetHashCode();
        public override string ToString() => $"[{m00},{m01},{m02},{m03};{m10},{m11},{m12},{m13};{m20},{m21},{m22},{m23};{m30},{m31},{m32},{m33}]";
    }
}
