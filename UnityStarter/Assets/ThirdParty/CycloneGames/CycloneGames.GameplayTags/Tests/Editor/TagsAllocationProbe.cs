using System;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests
{
    /// <summary>
    /// Guards zero-allocation assertions against hosts where the per-thread allocation counter is not
    /// populated. The probe allocates a known block once and checks that the counter observes it;
    /// when it does not, the caller is skipped instead of passing vacuously.
    /// </summary>
    internal static class TagsAllocationProbe
    {
        private const int ProbeSizeBytes = 64 * 1024;
        private static bool? counterIsReliable;

        internal static bool CounterIsReliable
        {
            get
            {
                if (counterIsReliable.HasValue)
                {
                    return counterIsReliable.Value;
                }

                long before = GC.GetAllocatedBytesForCurrentThread();
                var probe = new byte[ProbeSizeBytes];
                GC.KeepAlive(probe);
                counterIsReliable = GC.GetAllocatedBytesForCurrentThread() - before >= ProbeSizeBytes;
                return counterIsReliable.Value;
            }
        }

        internal static void AssertNoMeasuredAllocation(long measuredBytes, string message)
        {
            if (!CounterIsReliable)
            {
                Assert.Ignore(
                    "GC.GetAllocatedBytesForCurrentThread does not observe allocations in this host; " +
                    "the zero-allocation claim is unverified here.");
            }

            Assert.That(measuredBytes, Is.Zero, message);
        }

        internal static void SkipIfCounterIsUnreliable()
        {
            if (!CounterIsReliable)
            {
                Assert.Ignore(
                    "GC.GetAllocatedBytesForCurrentThread does not observe allocations in this host; " +
                    "the zero-allocation claim is unverified here.");
            }
        }
    }
}
