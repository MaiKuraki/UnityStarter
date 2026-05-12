using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;

namespace CycloneGames.GameplayAbilities.Core
{
    /// <summary>
    /// Deterministic gameplay numeric value used by the pure Core layer.
    /// Runtime and authoring layers may expose floats, but Core state stores raw fixed-point values.
    /// </summary>
    public readonly struct GASFixedValue
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
        public float ToFloat()
        {
            return FPInt64.FromRaw(RawValue).ToFloat();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public double ToDouble()
        {
            return FPInt64.FromRaw(RawValue).ToDouble();
        }
    }
}
