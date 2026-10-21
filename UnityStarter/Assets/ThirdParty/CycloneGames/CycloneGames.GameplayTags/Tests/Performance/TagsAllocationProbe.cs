using System;
using NUnit.Framework;

namespace CycloneGames.GameplayTags.Tests.Performance
{
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

        internal static void SkipIfCounterIsUnreliable()
        {
            if (!CounterIsReliable)
            {
                Assert.Ignore(
                    "GC.GetAllocatedBytesForCurrentThread does not observe allocations in this host; " +
                    "the zero-allocation claim is unverified here.");
            }
        }

        internal static void AssertNoMeasuredAllocation(long measuredBytes, string message)
        {
            SkipIfCounterIsUnreliable();
            Assert.That(measuredBytes, Is.Zero, message);
        }
    }
}
