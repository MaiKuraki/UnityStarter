using System;
using System.Runtime.CompilerServices;

namespace CycloneGames.DeterministicMath
{
    /// <summary>
    /// Intrinsic Euler angle rotation order.
    /// The second rotation happens in the frame of the first, and the third in the frame of the second.
    /// </summary>
    public enum EulerOrder
    {
        XYZ, XZY, YXZ, YZX, ZXY, ZYX
    }

    /// <summary>
    /// Deterministic quaternion for 3D rotations.
    /// Core math (multiply, Slerp, vector rotation) is engine-agnostic.
    /// Static constructors (<see cref="Euler(FPInt64,FPInt64,FPInt64,EulerOrder)"/>,
    /// <see cref="LookRotation"/>) default to Unity's left-handed Y-up convention.
    /// Use the explicit <see cref="EulerOrder"/> overload to match your engine's convention.
    /// All operations produce bit-identical results across all platforms.
    /// </summary>
    public struct FPQuaternion : IEquatable<FPQuaternion>
    {
        public FPInt64 X, Y, Z, W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPQuaternion(FPInt64 x, FPInt64 y, FPInt64 z, FPInt64 w)
        {
            X = x; Y = y; Z = z; W = w;
        }

        // ---- Properties ----

        public FPInt64 SqrMagnitude => X * X + Y * Y + Z * Z + W * W;
        public FPInt64 Magnitude => FPInt64.Sqrt(SqrMagnitude);

        public FPQuaternion Normalized
        {
            get
            {
                var mag = Magnitude;
                if (mag.RawValue == 0) return Identity;
                return new FPQuaternion(X / mag, Y / mag, Z / mag, W / mag);
            }
        }

        public FPQuaternion Conjugate => new FPQuaternion(-X, -Y, -Z, W);

        public FPQuaternion Inverse
        {
            get
            {
                var sqrMag = SqrMagnitude;
                if (sqrMag.RawValue == 0) return Identity;
                return new FPQuaternion(-X / sqrMag, -Y / sqrMag, -Z / sqrMag, W / sqrMag);
            }
        }

        // ---- Static Constructors ----

        /// <summary>
        /// Creates a rotation from an axis and an angle in radians (right-hand rule).
        /// Axis must be normalized for correct results.
        /// </summary>
        public static FPQuaternion AngleAxis(FPInt64 angle, FPVector3 axis)
        {
            FPInt64 halfAngle = angle / 2;
            FPMath.SinCos(halfAngle, out var sin, out var cos);
            var n = axis.Normalized;
            if (n.SqrMagnitude.RawValue == 0) return Identity;
            return new FPQuaternion(n.X * sin, n.Y * sin, n.Z * sin, cos);
        }

        /// <summary>
        /// Creates a rotation from Euler angles with explicit rotation order.
        /// Intrinsic (local-axis) rotation: the second rotation happens in the frame of the first, etc.
        /// <para>
        /// Common conventions:
        ///   Unity (Y-up, left-handed) -> ZXY: Euler(pitch, yaw, roll, EulerOrder.ZXY)
        ///   Godot (Y-up, right-handed) -> YXZ: Euler(pitch, yaw, roll, EulerOrder.YXZ)
        ///   Unreal (Z-up, left-handed) -> ZYX: Euler(roll, pitch, yaw, EulerOrder.ZYX) (note: axis swap)
        /// </para>
        /// </summary>
        public static FPQuaternion Euler(FPInt64 first, FPInt64 second, FPInt64 third, EulerOrder order)
        {
            FPInt64 h0 = first / 2;
            FPInt64 h1 = second / 2;
            FPInt64 h2 = third / 2;

            FPMath.SinCos(h0, out var s0, out var c0);
            FPMath.SinCos(h1, out var s1, out var c1);
            FPMath.SinCos(h2, out var s2, out var c2);

            // Build three axis-aligned quaternions, then multiply in the specified order.
            // Qaxis = (sin(half)*axis, cos(half))
            // For intrinsic order A->B->C: q_result = qC * qB * qA

            FPQuaternion q0, q1, q2;

            switch (order)
            {
                case EulerOrder.XYZ:
                    q0 = new FPQuaternion(s0, FPInt64.Zero, FPInt64.Zero, c0);
                    q1 = new FPQuaternion(FPInt64.Zero, s1, FPInt64.Zero, c1);
                    q2 = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, s2, c2);
                    return q2 * q1 * q0;

                case EulerOrder.XZY:
                    q0 = new FPQuaternion(s0, FPInt64.Zero, FPInt64.Zero, c0);
                    q1 = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, s1, c1);
                    q2 = new FPQuaternion(FPInt64.Zero, s2, FPInt64.Zero, c2);
                    return q2 * q1 * q0;

                case EulerOrder.YXZ:
                    q0 = new FPQuaternion(FPInt64.Zero, s0, FPInt64.Zero, c0);
                    q1 = new FPQuaternion(s1, FPInt64.Zero, FPInt64.Zero, c1);
                    q2 = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, s2, c2);
                    return q2 * q1 * q0;

                case EulerOrder.YZX:
                    q0 = new FPQuaternion(FPInt64.Zero, s0, FPInt64.Zero, c0);
                    q1 = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, s1, c1);
                    q2 = new FPQuaternion(s2, FPInt64.Zero, FPInt64.Zero, c2);
                    return q2 * q1 * q0;

                case EulerOrder.ZXY: // Unity default
                    q0 = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, s0, c0);
                    q1 = new FPQuaternion(s1, FPInt64.Zero, FPInt64.Zero, c1);
                    q2 = new FPQuaternion(FPInt64.Zero, s2, FPInt64.Zero, c2);
                    return q2 * q1 * q0;

                case EulerOrder.ZYX: // Unreal convention (Z-up)
                    q0 = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, s0, c0);
                    q1 = new FPQuaternion(FPInt64.Zero, s1, FPInt64.Zero, c1);
                    q2 = new FPQuaternion(s2, FPInt64.Zero, FPInt64.Zero, c2);
                    return q2 * q1 * q0;

                default:
                    return Identity;
            }
        }

        /// <summary>
        /// Unity-compatible: creates a rotation from pitch/yaw/roll in radians, ZXY intrinsic order.
        /// For cross-engine usage, prefer the explicit <see cref="Euler(FPInt64,FPInt64,FPInt64,EulerOrder)"/> overload.
        /// </summary>
        public static FPQuaternion Euler(FPInt64 pitch, FPInt64 yaw, FPInt64 roll) =>
            Euler(pitch, yaw, roll, EulerOrder.ZXY);

        /// <summary>Creates a rotation from Euler angles stored in a vector (pitch, yaw, roll, ZXY order).</summary>
        public static FPQuaternion Euler(FPVector3 eulerRadians) =>
            Euler(eulerRadians.X, eulerRadians.Y, eulerRadians.Z, EulerOrder.ZXY);

        /// <summary>
        /// Creates a rotation that rotates from direction 'from' to direction 'to'.
        /// </summary>
        public static FPQuaternion FromToRotation(FPVector3 from, FPVector3 to)
        {
            var fromN = from.Normalized;
            var toN = to.Normalized;

            var dot = FPVector3.Dot(fromN, toN);

            // If directions are nearly identical, return identity
            if (dot.RawValue >= FPInt64.OneValue.RawValue - 1)
                return Identity;

            // If directions are nearly opposite, return 180-degree rotation around perpendicular axis
            if (dot.RawValue <= -FPInt64.OneValue.RawValue + 1)
            {
                var axis = FPVector3.Cross(fromN, FPVector3.Right);
                if (axis.SqrMagnitude.RawValue == 0)
                    axis = FPVector3.Cross(fromN, FPVector3.Up);
                return AngleAxis(FPInt64.Pi, axis.Normalized);
            }

            var s = FPInt64.Sqrt((FPInt64.OneValue + dot) * 2);
            var invS = FPInt64.OneValue / s;
            var c = FPVector3.Cross(fromN, toN);
            return new FPQuaternion(c.X * invS, c.Y * invS, c.Z * invS, s / 2).Normalized;
        }

        /// <summary>Creates a rotation looking in the specified forward direction.</summary>
        public static FPQuaternion LookRotation(FPVector3 forward, FPVector3 up)
        {
            var f = forward.Normalized;
            var r = FPVector3.Cross(up.Normalized, f).Normalized;
            var u = FPVector3.Cross(f, r);

            // Build quaternion from rotation matrix columns (right, up, forward)
            // Trace = r.x + u.y + f.z
            var trace = r.X + u.Y + f.Z;

            FPInt64 qx, qy, qz, qw;
            if (trace.RawValue > 0)
            {
                var s = FPInt64.Sqrt(trace + FPInt64.OneValue);
                var halfInvS = FPInt64.OneValue / (s * 2);
                qw = s / 2;
                qx = (u.Z - f.Y) * halfInvS;
                qy = (f.X - r.Z) * halfInvS;
                qz = (r.Y - u.X) * halfInvS;
            }
            else if (r.X.RawValue > u.Y.RawValue && r.X.RawValue > f.Z.RawValue)
            {
                var s = FPInt64.Sqrt(r.X - u.Y - f.Z + FPInt64.OneValue);
                var halfInvS = FPInt64.OneValue / (s * 2);
                qw = (u.Z - f.Y) * halfInvS;
                qx = s / 2;
                qy = (u.X + r.Y) * halfInvS;
                qz = (f.X + r.Z) * halfInvS;
            }
            else if (u.Y.RawValue > f.Z.RawValue)
            {
                var s = FPInt64.Sqrt(u.Y - f.Z - r.X + FPInt64.OneValue);
                var halfInvS = FPInt64.OneValue / (s * 2);
                qw = (f.X - r.Z) * halfInvS;
                qx = (u.X + r.Y) * halfInvS;
                qy = s / 2;
                qz = (f.Y + u.Z) * halfInvS;
            }
            else
            {
                var s = FPInt64.Sqrt(f.Z - r.X - u.Y + FPInt64.OneValue);
                var halfInvS = FPInt64.OneValue / (s * 2);
                qw = (r.Y - u.X) * halfInvS;
                qx = (f.X + r.Z) * halfInvS;
                qy = (f.Y + u.Z) * halfInvS;
                qz = s / 2;
            }

            return new FPQuaternion(qx, qy, qz, qw).Normalized;
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

        /// <summary>Rotate a 3D vector by this quaternion.</summary>
        public static FPVector3 operator *(FPQuaternion rotation, FPVector3 point)
        {
            // q * p * q^-1, optimized
            var qx = rotation.X; var qy = rotation.Y; var qz = rotation.Z; var qw = rotation.W;
            var px = point.X; var py = point.Y; var pz = point.Z;

            var x2 = qx + qx; var y2 = qy + qy; var z2 = qz + qz;
            var xx2 = qx * x2; var yy2 = qy * y2; var zz2 = qz * z2;
            var xy2 = qx * y2; var xz2 = qx * z2;
            var yz2 = qy * z2; var wx2 = qw * x2;
            var wy2 = qw * y2; var wz2 = qw * z2;

            return new FPVector3(
                (FPInt64.OneValue - yy2 - zz2) * px + (xy2 - wz2) * py + (xz2 + wy2) * pz,
                (xy2 + wz2) * px + (FPInt64.OneValue - xx2 - zz2) * py + (yz2 - wx2) * pz,
                (xz2 - wy2) * px + (yz2 + wx2) * py + (FPInt64.OneValue - xx2 - yy2) * pz
            );
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static FPQuaternion operator -(FPQuaternion q) => new FPQuaternion(-q.X, -q.Y, -q.Z, -q.W);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(FPQuaternion a, FPQuaternion b) =>
            a.X == b.X && a.Y == b.Y && a.Z == b.Z && a.W == b.W;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(FPQuaternion a, FPQuaternion b) => !(a == b);

        // ---- Interpolation ----

        /// <summary>
        /// Spherical linear interpolation. Constant angular speed.
        /// </summary>
        public static FPQuaternion Slerp(FPQuaternion a, FPQuaternion b, FPInt64 t)
        {
            if (t.RawValue <= 0) return a;
            if (t.RawValue >= FPInt64.OneValue.RawValue) return b;

            var dot = a.X * b.X + a.Y * b.Y + a.Z * b.Z + a.W * b.W;

            // Take the shorter arc
            if (dot.RawValue < 0)
            {
                dot = -dot;
                b = -b;
            }

            // If nearly parallel, fall back to Nlerp (avoids Acos domain clamping + zero sinAngle)
            if (dot.RawValue > FPInt64.OneValue.RawValue - 65536L)
                return Nlerp(a, b, t);

            // Clamp to [-1, 1] to guard against FP precision overflow
            dot = FPInt64.Clamp(dot, -FPInt64.OneValue, FPInt64.OneValue);

            var angle = FPMath.Acos(dot);
            FPMath.SinCos(angle, out var sinAngle, out var _);

            // Guard against sin(angle) approx 0 (exactly opposite quaternions with precision loss)
            if (FPInt64.Abs(sinAngle).RawValue < 100) // ~2.3e-8
                return Nlerp(a, b, t);

            var invSin = FPInt64.OneValue / sinAngle;
            FPMath.SinCos((FPInt64.OneValue - t) * angle, out var sinT0, out var _2);
            FPMath.SinCos(t * angle, out var sinT1, out var _3);

            var t0 = sinT0 * invSin;
            var t1 = sinT1 * invSin;

            return new FPQuaternion(
                a.X * t0 + b.X * t1,
                a.Y * t0 + b.Y * t1,
                a.Z * t0 + b.Z * t1,
                a.W * t0 + b.W * t1
            );
        }

        /// <summary>
        /// Normalized linear interpolation. Faster than Slerp, slight speed variation.
        /// </summary>
        public static FPQuaternion Nlerp(FPQuaternion a, FPQuaternion b, FPInt64 t)
        {
            return new FPQuaternion(
                a.X + (b.X - a.X) * t,
                a.Y + (b.Y - a.Y) * t,
                a.Z + (b.Z - a.Z) * t,
                a.W + (b.W - a.W) * t
            ).Normalized;
        }

        // ---- Euler Extraction ----

        /// <summary>
        /// Extracts Euler angles (radians) from this quaternion. ZXY order by default (Unity).
        /// For cross-engine use: <c>ToEuler(EulerOrder.YXZ)</c> for Godot, <c>EulerOrder.ZYX</c> for Unreal.
        /// </summary>
        public FPVector3 ToEuler(EulerOrder order = EulerOrder.ZXY)
        {
            switch (order)
            {
                case EulerOrder.ZXY:
                {
                    var two = FPInt64.FromInt(2);
                    var sinPitch = two * (W * X + Y * Z);
                    var cosPitch = FPInt64.OneValue - two * (X * X + Y * Y);
                    var pitch = FPMath.Atan2(sinPitch, cosPitch);
                    var sinYaw = two * (W * Y - Z * X);
                    var yaw = FPInt64.Abs(sinYaw).RawValue >= FPInt64.OneValue.RawValue
                        ? (sinYaw.RawValue >= 0 ? FPInt64.HalfPi : -FPInt64.HalfPi)
                        : FPMath.Asin(sinYaw);
                    var sinRoll = two * (W * Z + X * Y);
                    var cosRoll = FPInt64.OneValue - two * (Y * Y + Z * Z);
                    var roll = FPMath.Atan2(sinRoll, cosRoll);
                    return new FPVector3(pitch, yaw, roll);
                }
                case EulerOrder.YXZ:
                {
                    var two = FPInt64.FromInt(2);
                    var sinYaw = two * (W * Y - X * Z);
                    var cosYaw = FPInt64.OneValue - two * (Y * Y + Z * Z);
                    var yaw = FPMath.Atan2(sinYaw, cosYaw);
                    var sinPitch = two * (W * X + Y * Z);
                    var pitch = FPInt64.Abs(sinPitch).RawValue >= FPInt64.OneValue.RawValue
                        ? (sinPitch.RawValue >= 0 ? FPInt64.HalfPi : -FPInt64.HalfPi)
                        : FPMath.Asin(sinPitch);
                    var sinRoll = two * (W * Z + X * Y);
                    var cosRoll = FPInt64.OneValue - two * (X * X + Z * Z);
                    var roll = FPMath.Atan2(sinRoll, cosRoll);
                    return new FPVector3(pitch, yaw, roll);
                }
                default:
                {
                    var m = FPMatrix4x4.Rotate(this);
                    return order switch
                    {
                        EulerOrder.XYZ => new FPVector3(
                            FPMath.Atan2(-m.m21, m.m22),
                            FPMath.Asin(FPInt64.Clamp(m.m20, -FPInt64.OneValue, FPInt64.OneValue)),
                            FPMath.Atan2(-m.m10, m.m00)),
                        EulerOrder.XZY => new FPVector3(
                            FPMath.Atan2(m.m12, m.m11),
                            FPMath.Asin(FPInt64.Clamp(-m.m10, -FPInt64.OneValue, FPInt64.OneValue)),
                            FPMath.Atan2(m.m20, m.m00)),
                        EulerOrder.ZYX => new FPVector3(
                            FPMath.Atan2(m.m21, m.m22),
                            FPMath.Asin(FPInt64.Clamp(-m.m20, -FPInt64.OneValue, FPInt64.OneValue)),
                            FPMath.Atan2(m.m10, m.m00)),
                        _ => new FPVector3(
                            FPMath.Atan2(m.m21, m.m22),
                            FPMath.Asin(FPInt64.Clamp(-m.m20, -FPInt64.OneValue, FPInt64.OneValue)),
                            FPMath.Atan2(m.m10, m.m00)),
                    };
                }
            }
        }

        // ---- Default ----

        public static readonly FPQuaternion Identity = new FPQuaternion(FPInt64.Zero, FPInt64.Zero, FPInt64.Zero, FPInt64.OneValue);

        // ---- Equality ----

        public bool Equals(FPQuaternion other) => this == other;
        public override bool Equals(object obj) => obj is FPQuaternion q && this == q;
        public override int GetHashCode() => X.GetHashCode() ^ Y.GetHashCode() ^ Z.GetHashCode() ^ W.GetHashCode();
        public override string ToString() => $"({X}, {Y}, {Z}, {W})";
    }
}
