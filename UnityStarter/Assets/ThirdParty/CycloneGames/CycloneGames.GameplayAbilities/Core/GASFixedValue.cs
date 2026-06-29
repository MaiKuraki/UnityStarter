using System;
using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Deterministic gameplay numeric value used by the pure Core layer.
    /// Runtime and authoring layers may expose floats, but Core state stores raw fixed-point values.
    /// </summary>
    public readonly struct GASFixedValue : IEquatable<GASFixedValue>, IComparable<GASFixedValue>
    {
        public readonly long RawValue;

        private GASFixedValue(long rawValue)
        {
            RawValue = rawValue;
        }

        public static GASFixedValue Zero => default;
        public static GASFixedValue One => FromRaw(FPInt64.One);

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue FromRaw(long rawValue)
        {
            return new GASFixedValue(rawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue FromFloat(float value)
        {
            return new GASFixedValue(FPInt64.FromFloat(value).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue FromDouble(double value)
        {
            return new GASFixedValue(FPInt64.FromDouble(value).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue FromInt(int value)
        {
            return new GASFixedValue(FPInt64.FromInt(value).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public FPInt64 ToFPInt64()
        {
            return FPInt64.FromRaw(RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public float ToFloat()
        {
            return FPInt64.FromRaw(RawValue).ToFloat();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble()
        {
            return FPInt64.FromRaw(RawValue).ToDouble();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public int CompareTo(GASFixedValue other)
        {
            return RawValue.CompareTo(other.RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool Equals(GASFixedValue other)
        {
            return RawValue == other.RawValue;
        }

        public override bool Equals(object obj)
        {
            return obj is GASFixedValue other && Equals(other);
        }

        public override int GetHashCode()
        {
            return RawValue.GetHashCode();
        }

        public override string ToString()
        {
            return ToDouble().ToString(System.Globalization.CultureInfo.InvariantCulture);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue operator +(GASFixedValue left, GASFixedValue right)
        {
            return FromRaw((left.ToFPInt64() + right.ToFPInt64()).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue operator -(GASFixedValue left, GASFixedValue right)
        {
            return FromRaw((left.ToFPInt64() - right.ToFPInt64()).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue operator -(GASFixedValue value)
        {
            return FromRaw((-value.ToFPInt64()).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue operator *(GASFixedValue left, GASFixedValue right)
        {
            return FromRaw((left.ToFPInt64() * right.ToFPInt64()).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue operator /(GASFixedValue left, GASFixedValue right)
        {
            return FromRaw((left.ToFPInt64() / right.ToFPInt64()).RawValue);
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator ==(GASFixedValue left, GASFixedValue right)
        {
            return left.RawValue == right.RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator !=(GASFixedValue left, GASFixedValue right)
        {
            return left.RawValue != right.RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <(GASFixedValue left, GASFixedValue right)
        {
            return left.RawValue < right.RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >(GASFixedValue left, GASFixedValue right)
        {
            return left.RawValue > right.RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator <=(GASFixedValue left, GASFixedValue right)
        {
            return left.RawValue <= right.RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static bool operator >=(GASFixedValue left, GASFixedValue right)
        {
            return left.RawValue >= right.RawValue;
        }

        /// <summary>
        /// Returns the smaller of two deterministic values using a raw integer comparison.
        /// Zero allocation; bit-identical across platforms and backends.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue Min(GASFixedValue a, GASFixedValue b)
        {
            return a.RawValue <= b.RawValue ? a : b;
        }

        /// <summary>
        /// Returns the larger of two deterministic values using a raw integer comparison.
        /// Zero allocation; bit-identical across platforms and backends.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue Max(GASFixedValue a, GASFixedValue b)
        {
            return a.RawValue >= b.RawValue ? a : b;
        }

        /// <summary>
        /// Clamps a value into the inclusive range [min, max] using raw integer comparisons only.
        /// Fully deterministic; no float is involved. If max is less than min the lower bound wins
        /// (result == min), which avoids throwing on degenerate ranges in hot paths.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static GASFixedValue Clamp(GASFixedValue value, GASFixedValue min, GASFixedValue max)
        {
            long raw = value.RawValue;
            if (raw > max.RawValue)
            {
                raw = max.RawValue;
            }
            if (raw < min.RawValue)
            {
                raw = min.RawValue;
            }
            return new GASFixedValue(raw);
        }
    }
}
