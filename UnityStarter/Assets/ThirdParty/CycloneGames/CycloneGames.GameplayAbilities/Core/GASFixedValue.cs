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
        public static GASFixedValue One => FromRaw(FPInt64.RAW_ONE);

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

        /// <summary>
        /// Resolves the effective magnitude of one Multiply/Division modifier for a stack count.
        /// Stack count 1 returns the magnitude unchanged so single-stack behaviour stays bit-identical.
        /// Additive and Override modifiers do not use this path.
        /// </summary>
        public static long ResolveStackScaledMagnitudeRaw(
            long magnitudeRaw,
            int stackCount,
            GASStackMagnitudePolicy policy)
        {
            return ResolveStackScaledMagnitudeRaw(magnitudeRaw, stackCount, policy, out _);
        }

        /// <summary>
        /// Resolves the effective magnitude of one Multiply/Division modifier for a stack count and
        /// reports whether the Q32.32 range was exceeded. An out-of-range result saturates to the
        /// representable limit in the direction of growth; <paramref name="overflowed"/> reports it.
        /// </summary>
        public static long ResolveStackScaledMagnitudeRaw(
            long magnitudeRaw,
            int stackCount,
            GASStackMagnitudePolicy policy,
            out bool overflowed)
        {
            overflowed = false;
            if (stackCount <= 1)
            {
                return magnitudeRaw;
            }

            var magnitude = FPInt64.FromRaw(magnitudeRaw);
            if (policy == GASStackMagnitudePolicy.Exponential)
            {
                var product = FPInt64.One;
                for (int i = 0; i < stackCount; i++)
                {
                    if (!FPInt64.TryMultiply(product, magnitude, out FPInt64 next))
                    {
                        overflowed = true;
                        // Sign follows the magnitude's sign and the stack parity, not the failing step.
                        bool negativeResult = magnitude.RawValue < 0 && (stackCount & 1) != 0;
                        return negativeResult ? FPInt64.MinValue.RawValue : FPInt64.MaxValue.RawValue;
                    }

                    product = next;
                }

                return product.RawValue;
            }

            // LinearBias: (M - 1) * stackCount + 1, so every stack contributes the same delta.
            var delta = magnitude - FPInt64.One;
            var stackMultiplier = FPInt64.FromInt(stackCount);
            if (!FPInt64.TryMultiply(delta, stackMultiplier, out FPInt64 scaled))
            {
                overflowed = true;
                return SaturateForSign(delta, stackMultiplier).RawValue;
            }

            if (!FPInt64.TryAdd(scaled, FPInt64.One, out FPInt64 biased))
            {
                overflowed = true;
                return SaturateForSign(scaled, FPInt64.One).RawValue;
            }

            return biased.RawValue;
        }

        /// <summary>
        /// Returns the limit a product of the two operands exceeds.
        /// </summary>
        internal static FPInt64 SaturateForSign(FPInt64 left, FPInt64 right)
        {
            bool negative = (left.RawValue ^ right.RawValue) < 0;
            return negative ? FPInt64.MinValue : FPInt64.MaxValue;
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
