using System.Runtime.CompilerServices;
using CycloneGames.DeterministicMath;

namespace CycloneGames.GameplayAbilities.Networking
{
    public static class GASNetFixed
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FromFloat(float value)
        {
            return FPInt64.FromFloat(value).RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static float ToFloat(long rawValue)
        {
            return FPInt64.FromRaw(rawValue).ToFloat();
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static long FromDouble(double value)
        {
            return FPInt64.FromDouble(value).RawValue;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public static double ToDouble(long rawValue)
        {
            return FPInt64.FromRaw(rawValue).ToDouble();
        }
    }
}
