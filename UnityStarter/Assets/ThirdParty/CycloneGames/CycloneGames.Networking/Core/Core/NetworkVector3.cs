using System;
using System.Runtime.CompilerServices;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking
{
    [Serializable]
    public struct NetworkVector3 : IEquatable<NetworkVector3>
    {
        public float X;
        public float Y;
        public float Z;

        public static readonly NetworkVector3 Zero;
        public static readonly NetworkVector3 One = new(1f, 1f, 1f);
        public static readonly NetworkVector3 Up = new(0f, 1f, 0f);
        public static readonly NetworkVector3 Down = new(0f, -1f, 0f);
        public static readonly NetworkVector3 Forward = new(0f, 0f, 1f);
        public static readonly NetworkVector3 Back = new(0f, 0f, -1f);
        public static readonly NetworkVector3 Right = new(1f, 0f, 0f);
        public static readonly NetworkVector3 Left = new(-1f, 0f, 0f);

        public NetworkVector3(float x, float y, float z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        public readonly float Magnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => MathF.Sqrt(X * X + Y * Y + Z * Z);
        }

        public readonly float SqrMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get => X * X + Y * Y + Z * Z;
        }

        public readonly NetworkVector3 Normalized
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                float sqrMag = X * X + Y * Y + Z * Z;
                if (sqrMag < 1E-10f) return Zero;
                float invMag = 1f / MathF.Sqrt(sqrMag);
                return new NetworkVector3(X * invMag, Y * invMag, Z * invMag);
            }
        }

        // Read-only property for magnitude approximation (avoids sqrt for fast comparisons)
        public readonly float ApproxMagnitude
        {
            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            get
            {
                float ax = Math.Abs(X);
                float ay = Math.Abs(Y);
                float az = Math.Abs(Z);
                return ax + ay + az; // Manhattan distance for quick estimates
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Dot(NetworkVector3 a, NetworkVector3 b)
            => a.X * b.X + a.Y * b.Y + a.Z * b.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 Cross(NetworkVector3 a, NetworkVector3 b)
            => new(a.Y * b.Z - a.Z * b.Y,
                   a.Z * b.X - a.X * b.Z,
                   a.X * b.Y - a.Y * b.X);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Distance(NetworkVector3 a, NetworkVector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return MathF.Sqrt(dx * dx + dy * dy + dz * dz);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float SqrDistance(NetworkVector3 a, NetworkVector3 b)
        {
            float dx = a.X - b.X;
            float dy = a.Y - b.Y;
            float dz = a.Z - b.Z;
            return dx * dx + dy * dy + dz * dz;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 Lerp(NetworkVector3 a, NetworkVector3 b, float t)
            => new(a.X + (b.X - a.X) * t,
                   a.Y + (b.Y - a.Y) * t,
                   a.Z + (b.Z - a.Z) * t);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 MoveTowards(NetworkVector3 current, NetworkVector3 target, float maxDelta)
        {
            float dx = target.X - current.X;
            float dy = target.Y - current.Y;
            float dz = target.Z - current.Z;
            float sqrMag = dx * dx + dy * dy + dz * dz;
            if (sqrMag <= maxDelta * maxDelta || sqrMag == 0f)
                return target;
            float scale = maxDelta / MathF.Sqrt(sqrMag);
            return new NetworkVector3(current.X + dx * scale, current.Y + dy * scale, current.Z + dz * scale);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 Max(NetworkVector3 a, NetworkVector3 b)
            => new(MathF.Max(a.X, b.X), MathF.Max(a.Y, b.Y), MathF.Max(a.Z, b.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 Min(NetworkVector3 a, NetworkVector3 b)
            => new(MathF.Min(a.X, b.X), MathF.Min(a.Y, b.Y), MathF.Min(a.Z, b.Z));

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 Scale(NetworkVector3 a, NetworkVector3 b)
            => new(a.X * b.X, a.Y * b.Y, a.Z * b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float Angle(NetworkVector3 from, NetworkVector3 to)
        {
            float dot = Dot(from.Normalized, to.Normalized);
            return MathF.Acos(Math.Clamp(dot, -1f, 1f));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 Project(NetworkVector3 vector, NetworkVector3 onNormal)
        {
            float sqrMag = onNormal.SqrMagnitude;
            if (sqrMag < 1E-10f) return Zero;
            return onNormal * (Dot(vector, onNormal) / sqrMag);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 operator +(NetworkVector3 a, NetworkVector3 b)
            => new(a.X + b.X, a.Y + b.Y, a.Z + b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 operator -(NetworkVector3 a, NetworkVector3 b)
            => new(a.X - b.X, a.Y - b.Y, a.Z - b.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 operator -(NetworkVector3 v)
            => new(-v.X, -v.Y, -v.Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 operator *(NetworkVector3 v, float s)
            => new(v.X * s, v.Y * s, v.Z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 operator *(float s, NetworkVector3 v)
            => new(v.X * s, v.Y * s, v.Z * s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 operator /(NetworkVector3 v, float s)
            => new(v.X / s, v.Y / s, v.Z / s);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(NetworkVector3 a, NetworkVector3 b)
            => a.X == b.X && a.Y == b.Y && a.Z == b.Z;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(NetworkVector3 a, NetworkVector3 b) => !(a == b);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool ApproxEquals(NetworkVector3 other, float epsilon = 0.0001f)
        {
            float dx = X - other.X;
            float dy = Y - other.Y;
            float dz = Z - other.Z;
            return dx * dx + dy * dy + dz * dz <= epsilon * epsilon;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool IsFinite()
            => float.IsFinite(X) && float.IsFinite(Y) && float.IsFinite(Z);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly void WriteTo(INetWriter writer)
        {
            writer.WriteFloat(X);
            writer.WriteFloat(Y);
            writer.WriteFloat(Z);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 ReadFrom(INetReader reader)
            => new(reader.ReadFloat(), reader.ReadFloat(), reader.ReadFloat());

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool Equals(NetworkVector3 other) => this == other;

        public override readonly bool Equals(object obj) => obj is NetworkVector3 other && Equals(other);

        public override readonly int GetHashCode() => HashCode.Combine(X, Y, Z);

        public override readonly string ToString() => $"({X:F2}, {Y:F2}, {Z:F2})";
    }
}
