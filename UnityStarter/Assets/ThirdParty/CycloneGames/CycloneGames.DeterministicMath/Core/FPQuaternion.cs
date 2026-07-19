using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Deterministic quaternion for 3D rotations.
    /// Core math (multiply, Slerp, vector rotation) is engine-agnostic.
    /// Static constructors use a Y-up basis. Use the explicit <see cref="EulerOrder"/> overload when the
    /// consuming simulation requires a specific intrinsic rotation order.
    /// </summary>
    public readonly struct FPQuaternion : IEquatable<FPQuaternion>
    {
        private const long GIMBAL_LOCK_EPSILON_RAW = 65536L;

        public readonly FPInt64 X;
        public readonly FPInt64 Y;
        public readonly FPInt64 Z;
        public readonly FPInt64 W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPQuaternion(FPInt64 x, FPInt64 y, FPInt64 z, FPInt64 w)
        {
            X = x;
            Y = y;
            Z = z;
            W = w;
        }

        // ---- Properties ----

        /// <summary>Gets the squared magnitude, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        public FPInt64 SqrMagnitude => FPMagnitudeUtility.GetSaturatedSquaredMagnitude(X, Y, Z, W);

        /// <summary>Gets the magnitude, saturated at <see cref="FPInt64.MaxValue"/> when out of range.</summary>
        public FPInt64 Magnitude => FPMagnitudeUtility.GetMagnitude(X, Y, Z, W);

        public FPQuaternion Normalized
        {
            get
            {
                if (!TryNormalize(out FPQuaternion normalized))
                {
                    throw new InvalidOperationException("A zero quaternion cannot be normalized.");
                }

                return normalized;
            }
        }

        public bool TryNormalize(out FPQuaternion normalized)
        {
            if (FPMagnitudeUtility.Normalize(
                    X,
                    Y,
                    Z,
                    W,
                    out FPInt64 x,
                    out FPInt64 y,
                    out FPInt64 z,
                    out FPInt64 w))
            {
                normalized = new FPQuaternion(x, y, z, w);
                return true;
            }

            normalized = default;
            return false;
        }

        public FPQuaternion Conjugate => new FPQuaternion(-X, -Y, -Z, W);

        public FPQuaternion Inverse
        {
            get
            {
                if (!TryInverse(out FPQuaternion inverse))
                {
                    throw new InvalidOperationException("The quaternion has no representable inverse.");
                }

                return inverse;
            }
        }

        public bool TryInverse(out FPQuaternion inverse)
        {
            ulong squaredMagnitudeHigh = 0;
            ulong squaredMagnitudeLow = 0;
            if (!TryAccumulateSquare(X.RawValue, ref squaredMagnitudeHigh, ref squaredMagnitudeLow) ||
                !TryAccumulateSquare(Y.RawValue, ref squaredMagnitudeHigh, ref squaredMagnitudeLow) ||
                !TryAccumulateSquare(Z.RawValue, ref squaredMagnitudeHigh, ref squaredMagnitudeLow) ||
                !TryAccumulateSquare(W.RawValue, ref squaredMagnitudeHigh, ref squaredMagnitudeLow) ||
                (squaredMagnitudeHigh | squaredMagnitudeLow) == 0 ||
                !TryDivideInverseComponent(
                    X.RawValue,
                    true,
                    squaredMagnitudeHigh,
                    squaredMagnitudeLow,
                    out FPInt64 x) ||
                !TryDivideInverseComponent(
                    Y.RawValue,
                    true,
                    squaredMagnitudeHigh,
                    squaredMagnitudeLow,
                    out FPInt64 y) ||
                !TryDivideInverseComponent(
                    Z.RawValue,
                    true,
                    squaredMagnitudeHigh,
                    squaredMagnitudeLow,
                    out FPInt64 z) ||
                !TryDivideInverseComponent(
                    W.RawValue,
                    false,
                    squaredMagnitudeHigh,
                    squaredMagnitudeLow,
                    out FPInt64 w) ||
                (x.RawValue | y.RawValue | z.RawValue | w.RawValue) == 0)
            {
                inverse = default;
                return false;
            }

            inverse = new FPQuaternion(x, y, z, w);
            return true;
        }

        // ---- Static Constructors ----

        /// <summary>
        /// Creates a rotation from an axis and an angle in radians (right-hand rule).
        /// The axis is normalized internally and must be non-zero.
        /// </summary>
        public static FPQuaternion AngleAxis(FPInt64 angle, FPVector3 axis)
        {
            if (!TryAngleAxis(angle, axis, out FPQuaternion rotation))
            {
                throw new ArgumentException("Axis must be non-zero and produce a representable rotation.", nameof(axis));
            }

            return rotation;
        }

        public static bool TryAngleAxis(FPInt64 angle, FPVector3 axis, out FPQuaternion rotation)
        {
            if (!axis.TryNormalize(out FPVector3 normalizedAxis))
            {
                rotation = default;
                return false;
            }

            FPInt64 halfAngle = angle / 2;
            FPMath.SinCos(halfAngle, out FPInt64 sin, out FPInt64 cos);
            if (!FPInt64.TryMultiply(normalizedAxis.X, sin, out FPInt64 x) ||
                !FPInt64.TryMultiply(normalizedAxis.Y, sin, out FPInt64 y) ||
                !FPInt64.TryMultiply(normalizedAxis.Z, sin, out FPInt64 z))
            {
                rotation = default;
                return false;
            }

            rotation = new FPQuaternion(x, y, z, cos);
            return true;
        }

        /// <summary>
        /// Creates a rotation from Euler angles with explicit rotation order.
        /// Intrinsic (local-axis) rotation: the second rotation happens in the frame of the first, etc.
        /// Parameters always map to the X, Y, and Z axes; <paramref name="order"/> controls composition order.
        /// </summary>
        public static FPQuaternion Euler(FPInt64 xRadians, FPInt64 yRadians, FPInt64 zRadians, EulerOrder order)
        {
            FPMath.SinCos(xRadians / 2, out FPInt64 sinX, out FPInt64 cosX);
            FPMath.SinCos(yRadians / 2, out FPInt64 sinY, out FPInt64 cosY);
            FPMath.SinCos(zRadians / 2, out FPInt64 sinZ, out FPInt64 cosZ);

            var qx = new FPQuaternion(sinX, FPInt64.Zero, FPInt64.Zero, cosX);
            var qy = new FPQuaternion(FPInt64.Zero, sinY, FPInt64.Zero, cosY);
            var qz = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, sinZ, cosZ);

            switch (order)
            {
                case EulerOrder.XYZ:
                    return qz * qy * qx;

                case EulerOrder.XZY:
                    return qy * qz * qx;

                case EulerOrder.YXZ:
                    return qz * qx * qy;

                case EulerOrder.YZX:
                    return qx * qz * qy;

                case EulerOrder.ZXY:
                    return qy * qx * qz;

                case EulerOrder.ZYX:
                    return qx * qy * qz;

                default:
                    throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown Euler order.");
            }
        }

        /// <summary>
        /// Creates a rotation from X, Y, and Z angles in radians using ZXY intrinsic order.
        /// </summary>
        public static FPQuaternion Euler(FPInt64 xRadians, FPInt64 yRadians, FPInt64 zRadians) =>
            Euler(xRadians, yRadians, zRadians, EulerOrder.ZXY);

        /// <summary>Creates a rotation from X, Y, and Z radians stored in a vector, using ZXY intrinsic order.</summary>
        public static FPQuaternion Euler(FPVector3 eulerRadians) =>
            Euler(eulerRadians.X, eulerRadians.Y, eulerRadians.Z, EulerOrder.ZXY);

        /// <summary>
        /// Creates a rotation that rotates from direction 'from' to direction 'to'.
        /// </summary>
        public static FPQuaternion FromToRotation(FPVector3 from, FPVector3 to)
        {
            if (!TryFromToRotation(from, to, out FPQuaternion rotation))
            {
                throw new ArgumentException("Both directions must be non-zero and produce a representable rotation.");
            }

            return rotation;
        }

        public static bool TryFromToRotation(FPVector3 from, FPVector3 to, out FPQuaternion rotation)
        {
            if (!from.TryNormalize(out FPVector3 fromN) ||
                !to.TryNormalize(out FPVector3 toN) ||
                !FPVector3.TryDot(fromN, toN, out FPInt64 dot))
            {
                rotation = default;
                return false;
            }

            if (dot.RawValue >= FPInt64.One.RawValue - 1)
            {
                rotation = Identity;
                return true;
            }

            if (dot.RawValue <= -FPInt64.One.RawValue + 1)
            {
                var axis = FPVector3.Cross(fromN, FPVector3.Right);
                if (!axis.TryNormalize(out FPVector3 normalizedAxis))
                {
                    axis = FPVector3.Cross(fromN, FPVector3.Up);
                    if (!axis.TryNormalize(out normalizedAxis))
                    {
                        rotation = default;
                        return false;
                    }
                }

                return TryAngleAxis(FPInt64.Pi, normalizedAxis, out rotation);
            }

            FPVector3 cross = FPVector3.Cross(fromN, toN);
            if (!FPInt64.TryAdd(FPInt64.One, dot, out FPInt64 onePlusDot) ||
                !FPInt64.TryAdd(onePlusDot, onePlusDot, out FPInt64 twiceOnePlusDot) ||
                !FPInt64.TrySqrt(twiceOnePlusDot, out FPInt64 scale) ||
                scale.RawValue == 0 ||
                !FPInt64.TryDivide(FPInt64.One, scale, out FPInt64 inverseScale) ||
                !FPInt64.TryMultiply(cross.X, inverseScale, out FPInt64 x) ||
                !FPInt64.TryMultiply(cross.Y, inverseScale, out FPInt64 y) ||
                !FPInt64.TryMultiply(cross.Z, inverseScale, out FPInt64 z) ||
                !FPInt64.TryDivide(scale, 2, out FPInt64 w))
            {
                rotation = default;
                return false;
            }

            return new FPQuaternion(x, y, z, w).TryNormalize(out rotation);
        }

        /// <summary>
        /// Tries to create a rotation looking in the specified forward direction.
        /// A zero forward vector is rejected. A missing or collinear up vector is replaced by a deterministic
        /// orthogonal reference axis.
        /// </summary>
        public static bool TryLookRotation(FPVector3 forward, FPVector3 up, out FPQuaternion rotation)
        {
            if (!forward.TryNormalize(out FPVector3 f))
            {
                rotation = default;
                return false;
            }

            FPVector3 upNormalized = up.NormalizedOrZero;
            FPVector3 right = upNormalized == FPVector3.Zero
                ? FPVector3.Zero
                : FPVector3.Cross(upNormalized, f);

            if (right.SqrMagnitude.RawValue < 100L)
            {
                FPInt64 absX = FPInt64.Abs(f.X);
                FPInt64 absY = FPInt64.Abs(f.Y);
                FPInt64 absZ = FPInt64.Abs(f.Z);
                FPVector3 reference = absX <= absY && absX <= absZ
                    ? FPVector3.Right
                    : absY <= absZ
                        ? FPVector3.Up
                        : FPVector3.Forward;
                right = FPVector3.Cross(reference, f);
            }

            if (!right.TryNormalize(out FPVector3 r))
            {
                rotation = default;
                return false;
            }

            if (!FPVector3.Cross(f, r).TryNormalize(out FPVector3 u))
            {
                rotation = default;
                return false;
            }

            // Build quaternion from rotation matrix columns (right, up, forward)
            // Trace = r.x + u.y + f.z
            var trace = r.X + u.Y + f.Z;

            FPInt64 qx, qy, qz, qw;
            if (trace.RawValue > 0)
            {
                var s = FPInt64.Sqrt(trace + FPInt64.One);
                if (s.RawValue == 0)
                {
                    rotation = default;
                    return false;
                }
                var halfInvS = FPInt64.One / (s * 2);
                qw = s / 2;
                qx = (u.Z - f.Y) * halfInvS;
                qy = (f.X - r.Z) * halfInvS;
                qz = (r.Y - u.X) * halfInvS;
            }
            else if (r.X.RawValue > u.Y.RawValue && r.X.RawValue > f.Z.RawValue)
            {
                var s = FPInt64.Sqrt(r.X - u.Y - f.Z + FPInt64.One);
                if (s.RawValue == 0)
                {
                    rotation = default;
                    return false;
                }
                var halfInvS = FPInt64.One / (s * 2);
                qw = (u.Z - f.Y) * halfInvS;
                qx = s / 2;
                qy = (u.X + r.Y) * halfInvS;
                qz = (f.X + r.Z) * halfInvS;
            }
            else if (u.Y.RawValue > f.Z.RawValue)
            {
                var s = FPInt64.Sqrt(u.Y - f.Z - r.X + FPInt64.One);
                if (s.RawValue == 0)
                {
                    rotation = default;
                    return false;
                }
                var halfInvS = FPInt64.One / (s * 2);
                qw = (f.X - r.Z) * halfInvS;
                qx = (u.X + r.Y) * halfInvS;
                qy = s / 2;
                qz = (f.Y + u.Z) * halfInvS;
            }
            else
            {
                var s = FPInt64.Sqrt(f.Z - r.X - u.Y + FPInt64.One);
                if (s.RawValue == 0)
                {
                    rotation = default;
                    return false;
                }
                var halfInvS = FPInt64.One / (s * 2);
                qw = (r.Y - u.X) * halfInvS;
                qx = (f.X + r.Z) * halfInvS;
                qy = (f.Y + u.Z) * halfInvS;
                qz = s / 2;
            }

            rotation = new FPQuaternion(qx, qy, qz, qw).Normalized;
            return true;
        }

        /// <summary>Creates a rotation looking in the specified non-zero forward direction.</summary>
        public static FPQuaternion LookRotation(FPVector3 forward, FPVector3 up)
        {
            if (!TryLookRotation(forward, up, out FPQuaternion rotation))
            {
                throw new ArgumentException("Forward must be non-zero and produce a representable rotation.", nameof(forward));
            }

            return rotation;
        }

        /// <summary>Creates a rotation looking in the specified forward direction (up defaults to world up).</summary>
        public static FPQuaternion LookRotation(FPVector3 forward) => LookRotation(forward, FPVector3.Up);

        // ---- Operators ----

        /// <summary>Hamilton product (rotation composition). q1 * q2 applies q2 then q1.</summary>
        public static FPQuaternion operator *(FPQuaternion a, FPQuaternion b)
        {
            return new FPQuaternion(
                a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
                a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
                a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
                a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z
            );
        }

        /// <summary>Rotates a 3D vector by a normalized quaternion using wrapping hot-path arithmetic.</summary>
        public static FPVector3 operator *(FPQuaternion rotation, FPVector3 point)
        {
            if ((rotation.X.RawValue | rotation.Y.RawValue | rotation.Z.RawValue | rotation.W.RawValue) == 0)
            {
                throw new InvalidOperationException("A zero quaternion cannot rotate a vector.");
            }

            // q * p * q^-1, optimized
            FPInt64 qx = rotation.X;
            FPInt64 qy = rotation.Y;
            FPInt64 qz = rotation.Z;
            FPInt64 qw = rotation.W;
            FPInt64 px = point.X;
            FPInt64 py = point.Y;
            FPInt64 pz = point.Z;

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

            return new FPVector3(
                (FPInt64.One - yy2 - zz2) * px + (xy2 - wz2) * py + (xz2 + wy2) * pz,
                (xy2 + wz2) * px + (FPInt64.One - xx2 - zz2) * py + (yz2 - wx2) * pz,
                (xz2 - wy2) * px + (yz2 + wx2) * py + (FPInt64.One - xx2 - yy2) * pz
            );
        }

        /// <summary>Normalizes the quaternion and attempts a checked vector rotation.</summary>
        public static bool TryRotate(FPQuaternion rotation, FPVector3 point, out FPVector3 result)
        {
            if (!rotation.TryNormalize(out FPQuaternion normalized))
            {
                result = default;
                return false;
            }

            return TryRotateNormalized(normalized, point, out result);
        }

        internal static bool TryRotateNormalized(FPQuaternion rotation, FPVector3 point, out FPVector3 result)
        {
            if ((rotation.X.RawValue | rotation.Y.RawValue | rotation.Z.RawValue | rotation.W.RawValue) == 0 ||
                !FPInt64.TryAdd(rotation.X, rotation.X, out FPInt64 x2) ||
                !FPInt64.TryAdd(rotation.Y, rotation.Y, out FPInt64 y2) ||
                !FPInt64.TryAdd(rotation.Z, rotation.Z, out FPInt64 z2) ||
                !FPInt64.TryMultiply(rotation.X, x2, out FPInt64 xx2) ||
                !FPInt64.TryMultiply(rotation.Y, y2, out FPInt64 yy2) ||
                !FPInt64.TryMultiply(rotation.Z, z2, out FPInt64 zz2) ||
                !FPInt64.TryMultiply(rotation.X, y2, out FPInt64 xy2) ||
                !FPInt64.TryMultiply(rotation.X, z2, out FPInt64 xz2) ||
                !FPInt64.TryMultiply(rotation.Y, z2, out FPInt64 yz2) ||
                !FPInt64.TryMultiply(rotation.W, x2, out FPInt64 wx2) ||
                !FPInt64.TryMultiply(rotation.W, y2, out FPInt64 wy2) ||
                !FPInt64.TryMultiply(rotation.W, z2, out FPInt64 wz2) ||
                !FPInt64.TrySubtract(FPInt64.One, yy2, out FPInt64 oneMinusYy) ||
                !FPInt64.TrySubtract(oneMinusYy, zz2, out FPInt64 m00) ||
                !FPInt64.TrySubtract(xy2, wz2, out FPInt64 m01) ||
                !FPInt64.TryAdd(xz2, wy2, out FPInt64 m02) ||
                !FPInt64.TryAdd(xy2, wz2, out FPInt64 m10) ||
                !FPInt64.TrySubtract(FPInt64.One, xx2, out FPInt64 oneMinusXx) ||
                !FPInt64.TrySubtract(oneMinusXx, zz2, out FPInt64 m11) ||
                !FPInt64.TrySubtract(yz2, wx2, out FPInt64 m12) ||
                !FPInt64.TrySubtract(xz2, wy2, out FPInt64 m20) ||
                !FPInt64.TryAdd(yz2, wx2, out FPInt64 m21) ||
                !FPInt64.TrySubtract(oneMinusXx, yy2, out FPInt64 m22) ||
                !FPMagnitudeUtility.TryDot(m00, m01, m02, point.X, point.Y, point.Z, out FPInt64 x) ||
                !FPMagnitudeUtility.TryDot(m10, m11, m12, point.X, point.Y, point.Z, out FPInt64 y) ||
                !FPMagnitudeUtility.TryDot(m20, m21, m22, point.X, point.Y, point.Z, out FPInt64 z))
            {
                result = default;
                return false;
            }

            result = new FPVector3(x, y, z);
            return true;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPQuaternion operator -(FPQuaternion q) => new FPQuaternion(-q.X, -q.Y, -q.Z, -q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPQuaternion a, FPQuaternion b) =>
            a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPQuaternion a, FPQuaternion b) => !(a == b);

        // ---- Interpolation ----

        /// <summary>Spherical interpolation with constant angular speed and <paramref name="t"/> clamped to [0, 1].</summary>
        public static FPQuaternion Slerp(FPQuaternion a, FPQuaternion b, FPInt64 t)
        {
            if (t.RawValue <= 0)
            {
                return a;
            }

            if (t.RawValue >= FPInt64.One.RawValue)
            {
                return b;
            }

            return SlerpUnclamped(a, b, t);
        }

        /// <summary>Spherical interpolation or extrapolation without clamping <paramref name="t"/>.</summary>
        public static FPQuaternion SlerpUnclamped(FPQuaternion a, FPQuaternion b, FPInt64 t)
        {

            var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

            // Take the shorter arc
            if (dot.RawValue < 0)
            {
                dot = -dot;
                b = -b;
            }

            // If nearly parallel, fall back to Nlerp (avoids Acos domain clamping + zero sinAngle)
            if (dot.RawValue > FPInt64.One.RawValue - 65536L)
            {
                return NlerpUnclamped(a, b, t);
            }

            // Clamp to [-1, 1] to guard against FP precision overflow
            dot = FPInt64.Clamp(dot, -FPInt64.One, FPInt64.One);

            var angle = FPMath.Acos(dot);
            FPMath.SinCos(angle, out var sinAngle, out var _);

            // Guard against sin(angle) approx 0 (exactly opposite quaternions with precision loss)
            if (FPInt64.Abs(sinAngle).RawValue < 100) // ~2.3e-8
            {
                return NlerpUnclamped(a, b, t);
            }

            var invSin = FPInt64.One / sinAngle;
            FPMath.SinCos((FPInt64.One - t) * angle, out var sinT0, out var _2);
            FPMath.SinCos(t * angle, out var sinT1, out var _3);

            var t0 = sinT0 * invSin;
            var t1 = sinT1 * invSin;

            return new FPQuaternion(
                a.X * t0 + b.X * t1,
                a.Y * t0 + b.Y * t1,
                a.Z * t0 + b.Z * t1,
                a.W * t0 + b.W * t1
            ).Normalized;
        }

        /// <summary>Normalized linear interpolation with <paramref name="t"/> clamped to [0, 1].</summary>
        public static FPQuaternion Nlerp(FPQuaternion a, FPQuaternion b, FPInt64 t)
        {
            if (t.RawValue <= 0)
            {
                return a;
            }

            if (t.RawValue >= FPInt64.One.RawValue)
            {
                return b;
            }

            return NlerpUnclamped(a, b, t);
        }

        /// <summary>Normalized linear interpolation or extrapolation without clamping <paramref name="t"/>.</summary>
        public static FPQuaternion NlerpUnclamped(FPQuaternion a, FPQuaternion b, FPInt64 t)
        {
            var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;
            if (dot.RawValue < 0)
            {
                b = -b;
            }

            return new FPQuaternion(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t
            ).Normalized;
        }

        // ---- Euler Extraction ----

        /// <summary>
        /// Extracts X, Y, and Z Euler angles in radians. ZXY intrinsic order is the default.
        /// </summary>
        public FPVector3 ToEuler(EulerOrder order = EulerOrder.ZXY)
        {
            FPMatrix4x4 m = FPMatrix4x4.Rotate(Normalized);

            switch (order)
            {
                case EulerOrder.XYZ:
                {
                    FPInt64 sinY = FPInt64.Clamp(-m.M20, -FPInt64.One, FPInt64.One);
                    FPInt64 y = FPMath.Asin(sinY);
                    if (IsGimbalLocked(sinY))
                    {
                        FPInt64 x = sinY.RawValue >= 0
                            ? FPMath.Atan2(m.M01, m.M02)
                            : FPMath.Atan2(-m.M01, -m.M02);
                        return new FPVector3(x, y, FPInt64.Zero);
                    }

                    return new FPVector3(
                        FPMath.Atan2(m.M21, m.M22),
                        y,
                        FPMath.Atan2(m.M10, m.M00));
                }
                case EulerOrder.XZY:
                {
                    FPInt64 sinZ = FPInt64.Clamp(m.M10, -FPInt64.One, FPInt64.One);
                    FPInt64 z = FPMath.Asin(sinZ);
                    if (IsGimbalLocked(sinZ))
                    {
                        return new FPVector3(
                            FPMath.Atan2(m.M21, m.M22),
                            FPInt64.Zero,
                            z);
                    }

                    return new FPVector3(
                        FPMath.Atan2(-m.M12, m.M11),
                        FPMath.Atan2(-m.M20, m.M00),
                        z);
                }
                case EulerOrder.YXZ:
                {
                    FPInt64 sinX = FPInt64.Clamp(m.M21, -FPInt64.One, FPInt64.One);
                    FPInt64 x = FPMath.Asin(sinX);
                    if (IsGimbalLocked(sinX))
                    {
                        return new FPVector3(
                            x,
                            FPMath.Atan2(m.M02, m.M00),
                            FPInt64.Zero);
                    }

                    return new FPVector3(
                        x,
                        FPMath.Atan2(-m.M20, m.M22),
                        FPMath.Atan2(-m.M01, m.M11));
                }
                case EulerOrder.YZX:
                {
                    FPInt64 sinZ = FPInt64.Clamp(-m.M01, -FPInt64.One, FPInt64.One);
                    FPInt64 z = FPMath.Asin(sinZ);
                    if (IsGimbalLocked(sinZ))
                    {
                        return new FPVector3(
                            FPInt64.Zero,
                            FPMath.Atan2(-m.M20, m.M22),
                            z);
                    }

                    return new FPVector3(
                        FPMath.Atan2(m.M21, m.M11),
                        FPMath.Atan2(m.M02, m.M00),
                        z);
                }
                case EulerOrder.ZXY:
                {
                    FPInt64 sinX = FPInt64.Clamp(-m.M12, -FPInt64.One, FPInt64.One);
                    FPInt64 x = FPMath.Asin(sinX);
                    if (IsGimbalLocked(sinX))
                    {
                        return new FPVector3(
                            x,
                            FPMath.Atan2(-m.M20, m.M00),
                            FPInt64.Zero);
                    }

                    return new FPVector3(
                        x,
                        FPMath.Atan2(m.M02, m.M22),
                        FPMath.Atan2(m.M10, m.M11));
                }
                case EulerOrder.ZYX:
                {
                    FPInt64 sinY = FPInt64.Clamp(m.M02, -FPInt64.One, FPInt64.One);
                    FPInt64 y = FPMath.Asin(sinY);
                    if (IsGimbalLocked(sinY))
                    {
                        return new FPVector3(
                            FPMath.Atan2(m.M21, m.M11),
                            y,
                            FPInt64.Zero);
                    }

                    return new FPVector3(
                        FPMath.Atan2(-m.M12, m.M22),
                        y,
                        FPMath.Atan2(-m.M01, m.M00));
                }
                default:
                    throw new ArgumentOutOfRangeException(nameof(order), order, "Unknown Euler order.");
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static bool IsGimbalLocked(FPInt64 sine)
        {
            return FPInt64.Abs(sine).RawValue >= FPInt64.One.RawValue - GIMBAL_LOCK_EPSILON_RAW;
        }

        private static bool TryDivideInverseComponent(
            long componentRaw,
            bool conjugate,
            ulong squaredMagnitudeHigh,
            ulong squaredMagnitudeLow,
            out FPInt64 result)
        {
            ulong magnitude = AbsRaw(componentRaw);
            if (magnitude == 0)
            {
                result = default;
                return true;
            }

            DivideUnsigned128(
                magnitude,
                0,
                squaredMagnitudeHigh,
                squaredMagnitudeLow,
                out ulong quotientHigh,
                out ulong quotientLow);

            bool isNegative = conjugate ? componentRaw > 0 : componentRaw < 0;
            ulong maxMagnitude = isNegative ? 0x8000000000000000UL : (ulong)long.MaxValue;
            if (quotientHigh != 0 || quotientLow > maxMagnitude)
            {
                result = default;
                return false;
            }

            result = FPInt64.FromRaw(ApplySign(quotientLow, isNegative));
            return true;
        }

        private static bool TryAccumulateSquare(
            long raw,
            ref ulong accumulatorHigh,
            ref ulong accumulatorLow)
        {
            ulong magnitude = AbsRaw(raw);
            MultiplyUnsigned64(magnitude, magnitude, out ulong squareHigh, out ulong squareLow);

            ulong newLow = unchecked(accumulatorLow + squareLow);
            ulong carry = newLow < accumulatorLow ? 1UL : 0UL;
            if (accumulatorHigh > ulong.MaxValue - squareHigh)
            {
                return false;
            }

            ulong newHigh = accumulatorHigh + squareHigh;
            if (newHigh > ulong.MaxValue - carry)
            {
                return false;
            }

            accumulatorHigh = newHigh + carry;
            accumulatorLow = newLow;
            return true;
        }

        private static void DivideUnsigned128(
            ulong numeratorHigh,
            ulong numeratorLow,
            ulong denominatorHigh,
            ulong denominatorLow,
            out ulong quotientHigh,
            out ulong quotientLow)
        {
            quotientHigh = 0;
            quotientLow = 0;
            if (CompareUnsigned128(
                    numeratorHigh,
                    numeratorLow,
                    denominatorHigh,
                    denominatorLow) < 0)
            {
                return;
            }

            int shift = GetBitLength(numeratorHigh, numeratorLow) -
                        GetBitLength(denominatorHigh, denominatorLow);
            ShiftLeftUnsigned128(
                denominatorHigh,
                denominatorLow,
                shift,
                out ulong shiftedDenominatorHigh,
                out ulong shiftedDenominatorLow);

            ulong remainderHigh = numeratorHigh;
            ulong remainderLow = numeratorLow;
            for (int bit = shift; bit >= 0; bit--)
            {
                if (CompareUnsigned128(
                        remainderHigh,
                        remainderLow,
                        shiftedDenominatorHigh,
                        shiftedDenominatorLow) >= 0)
                {
                    SubtractUnsigned128(
                        remainderHigh,
                        remainderLow,
                        shiftedDenominatorHigh,
                        shiftedDenominatorLow,
                        out remainderHigh,
                        out remainderLow);

                    if (bit >= 64)
                    {
                        quotientHigh |= 1UL << (bit - 64);
                    }
                    else
                    {
                        quotientLow |= 1UL << bit;
                    }
                }

                ShiftRightOne(ref shiftedDenominatorHigh, ref shiftedDenominatorLow);
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int CompareUnsigned128(
            ulong leftHigh,
            ulong leftLow,
            ulong rightHigh,
            ulong rightLow)
        {
            if (leftHigh != rightHigh)
            {
                return leftHigh < rightHigh ? -1 : 1;
            }

            if (leftLow == rightLow)
            {
                return 0;
            }

            return leftLow < rightLow ? -1 : 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void SubtractUnsigned128(
            ulong leftHigh,
            ulong leftLow,
            ulong rightHigh,
            ulong rightLow,
            out ulong resultHigh,
            out ulong resultLow)
        {
            ulong borrow = leftLow < rightLow ? 1UL : 0UL;
            resultLow = unchecked(leftLow - rightLow);
            resultHigh = leftHigh - rightHigh - borrow;
        }

        private static int GetBitLength(ulong high, ulong low)
        {
            return high != 0 ? 64 + GetBitLength(high) : GetBitLength(low);
        }

        private static int GetBitLength(ulong value)
        {
            int length = 0;
            while (value != 0)
            {
                value >>= 1;
                length++;
            }

            return length;
        }

        private static void ShiftLeftUnsigned128(
            ulong high,
            ulong low,
            int shift,
            out ulong resultHigh,
            out ulong resultLow)
        {
            if (shift == 0)
            {
                resultHigh = high;
                resultLow = low;
                return;
            }

            if (shift < 64)
            {
                resultHigh = (high << shift) | (low >> (64 - shift));
                resultLow = low << shift;
                return;
            }

            resultHigh = low << (shift - 64);
            resultLow = 0;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void ShiftRightOne(ref ulong high, ref ulong low)
        {
            low = (low >> 1) | (high << 63);
            high >>= 1;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ulong AbsRaw(long raw) =>
            raw < 0 ? (ulong)(-(raw + 1)) + 1UL : (ulong)raw;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static long ApplySign(ulong magnitude, bool isNegative)
        {
            if (!isNegative || magnitude == 0)
            {
                return (long)magnitude;
            }

            return magnitude == 0x8000000000000000UL
                ? long.MinValue
                : -(long)magnitude;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void MultiplyUnsigned64(ulong a, ulong b, out ulong high, out ulong low)
        {
            const ulong MASK_32 = 0xFFFFFFFFUL;

            ulong aLow = a & MASK_32;
            ulong aHigh = a >> FPInt64.FractionalBits;
            ulong bLow = b & MASK_32;
            ulong bHigh = b >> FPInt64.FractionalBits;

            ulong lowLow = aLow * bLow;
            ulong lowHigh = aLow * bHigh;
            ulong highLow = aHigh * bLow;
            ulong highHigh = aHigh * bHigh;

            ulong middle = (lowLow >> FPInt64.FractionalBits) +
                           (lowHigh & MASK_32) +
                           (highLow & MASK_32);
            low = (middle << FPInt64.FractionalBits) | (lowLow & MASK_32);
            high = highHigh +
                   (lowHigh >> FPInt64.FractionalBits) +
                   (highLow >> FPInt64.FractionalBits) +
                   (middle >> FPInt64.FractionalBits);
        }

        // ---- Default ----

        public static readonly FPQuaternion Identity = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.One);

        // ---- Equality ----

        public bool Equals(FPQuaternion other) => this == other;
        public override bool Equals(object obj) => obj is FPQuaternion q && this == q;
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }
}
