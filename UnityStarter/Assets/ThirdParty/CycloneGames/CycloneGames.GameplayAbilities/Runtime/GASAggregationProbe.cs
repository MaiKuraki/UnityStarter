#if UNITY_EDITOR
using System.Runtime.CompilerServices;

namespace CycloneGames.GameplayAbilities.Runtime
{
    /// <summary>
    /// Editor-only instrumentation counters for RecalculateDirtyAttributes: pass count, dirty
    /// attribute count, skipped effects, modifier visits/hits, and aggregation elapsed time.
    /// Compiled out of Player builds entirely. Plain static longs: no allocation, no locks
    /// (the GAS runtime is main-thread).
    /// </summary>
    internal static class GASAggregationProbe
    {
        internal static long RecalculatePasses;
        internal static long DirtyAttributesProcessed;
        internal static long EffectsSkippedPeriodicOrInhibited;
        internal static long ModifierVisits;
        internal static long ModifierHits;
        internal static long AggregationTimestampTicks;

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void OnRecalculateBegin(long timestamp)
        {
            RecalculatePasses++;
            AggregationTimestampTicks -= timestamp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void OnRecalculateEnd(long timestamp)
        {
            AggregationTimestampTicks += timestamp;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void OnDirtyAttribute()
        {
            DirtyAttributesProcessed++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void OnEffectSkipped()
        {
            EffectsSkippedPeriodicOrInhibited++;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        internal static void OnModifierVisit(bool hit)
        {
            ModifierVisits++;
            if (hit)
            {
                ModifierHits++;
            }
        }

        internal static void Reset()
        {
            RecalculatePasses = 0;
            DirtyAttributesProcessed = 0;
            EffectsSkippedPeriodicOrInhibited = 0;
            ModifierVisits = 0;
            ModifierHits = 0;
            AggregationTimestampTicks = 0;
        }
    }
}
#endif
