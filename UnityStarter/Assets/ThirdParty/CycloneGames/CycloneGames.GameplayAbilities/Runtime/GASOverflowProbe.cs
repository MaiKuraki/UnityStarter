#if UNITY_EDITOR
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Editor-only instrumentation for overflow-effect application chains: counts how many
    /// overflow applications execute and how deeply they nest. Compiled out of Player builds.
    /// Used by GASOverflowAmplificationTests to measure application fan-out.
    /// </summary>
    internal static class GASOverflowProbe
    {
        internal static long OverflowApplications;
        internal static int CurrentDepth;
        internal static int MaxDepth;
        internal static long DepthLimitRefusals;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Enter()
        {
            CurrentDepth++;
            if (CurrentDepth > MaxDepth)
            {
                MaxDepth = CurrentDepth;
            }
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void Exit()
        {
            CurrentDepth--;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CountApplication()
        {
            OverflowApplications++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void CountRefusal()
        {
            DepthLimitRefusals++;
        }

        internal static void Reset()
        {
            OverflowApplications = 0;
            CurrentDepth = 0;
            MaxDepth = 0;
            DepthLimitRefusals = 0;
        }
    }
}
#endif
