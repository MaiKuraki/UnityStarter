using System;
using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;
using CycloneGames.Networking.Serialization;

namespace CycloneGames.Networking
{
    /// <summary>
    /// Fixed-point (Q32.32) network vector. Integer arithmetic only, so values and
    /// every derived computation are bit-identical across CPU architectures,
    /// floating-point models, and JIT backends. Use for lockstep, rollback, and any
    /// replicated state whose hash is compared between peers. 24 bytes on the wire.
    /// </summary>
    public readonly struct NetworkFixedVector3 : IEquatable<NetworkFixedVector3>
    {
        public static readonly NetworkFixedVector3 Zero = default;

        public readonly FPInt64 X;
        public readonly FPInt64 Y;
        public readonly FPInt64 Z;

        public NetworkFixedVector3(FPInt64 x, FPInt64 y, FPInt64 z)
        {
            X = x;
            Y = y;
            Z = z;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkFixedVector3 FromNetworkVector3(in NetworkVector3 value)
        {
            return new NetworkFixedVector3(
                FPInt64.FromFloat(value.X),
                FPInt64.FromFloat(value.Y),
                FPInt64.FromFloat(value.Z));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public NetworkVector3 ToNetworkVector3()
        {
            return new NetworkVector3(X.ToFloat(), Y.ToFloat(), Z.ToFloat());
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public void WriteTo<TWriter>(TWriter writer) where TWriter : INetWriter
        {
            writer.WriteLong(X.RawValue);
            writer.WriteLong(Y.RawValue);
            writer.WriteLong(Z.RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkFixedVector3 ReadFrom<TReader>(TReader reader) where TReader : INetReader
        {
            return new NetworkFixedVector3(
                FPInt64.FromRaw(reader.ReadLong()),
                FPInt64.FromRaw(reader.ReadLong()),
                FPInt64.FromRaw(reader.ReadLong()));
        }

        public bool Equals(NetworkFixedVector3 other)
        {
            return X == other.X && Y == other.Y && Z == other.Z;
        }

        public override bool Equals(object obj) => obj is NetworkFixedVector3 other && Equals(other);

        public override int GetHashCode()
        {
            unchecked
            {
                int hash = X.RawValue.GetHashCode();
                hash = (hash * 397) ^ Y.RawValue.GetHashCode();
                hash = (hash * 397) ^ Z.RawValue.GetHashCode();
                return hash;
            }
        }

        public static bool operator ==(NetworkFixedVector3 a, NetworkFixedVector3 b) => a.Equals(b);
        public static bool operator !=(NetworkFixedVector3 a, NetworkFixedVector3 b) => !a.Equals(b);
    }

    /// <summary>
    /// Deterministic float quantization for wire transport. Values are snapped to a
    /// fixed integer grid before serialization, so the decoded value is identical on
    /// every architecture regardless of float rounding behavior. 12 bytes per vector
    /// on the wire (three int32), versus 24 bytes for full fixed-point.
    /// </summary>
    public static class NetworkVector3Quantization
    {
        /// <summary>Default grid: 1 millimeter.</summary>
        public const int DefaultUnitsPerMeter = 1000;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static void WriteQuantized<TWriter>(TWriter writer, in NetworkVector3 value, int unitsPerMeter = DefaultUnitsPerMeter)
            where TWriter : INetWriter
        {
            writer.WriteInt(QuantizeAxis(value.X, unitsPerMeter));
            writer.WriteInt(QuantizeAxis(value.Y, unitsPerMeter));
            writer.WriteInt(QuantizeAxis(value.Z, unitsPerMeter));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static NetworkVector3 ReadQuantized<TReader>(TReader reader, int unitsPerMeter = DefaultUnitsPerMeter)
            where TReader : INetReader
        {
            int x = reader.ReadInt();
            int y = reader.ReadInt();
            int z = reader.ReadInt();
            float inv = 1f / unitsPerMeter;
            return new NetworkVector3(x * inv, y * inv, z * inv);
        }

        // Round-to-nearest via double intermediates: the double-precision product is
        // exact for any float input within range, so the result is identical on all
        // IEEE-754 implementations. Values beyond the int32 grid saturate.
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static int QuantizeAxis(float value, int unitsPerMeter)
        {
            double scaled = (double)value * unitsPerMeter;
            if (scaled >= int.MaxValue) return int.MaxValue;
            if (scaled <= int.MinValue) return int.MinValue;
            return (int)Math.Round(scaled, MidpointRounding.AwayFromZero);
        }
    }
}
