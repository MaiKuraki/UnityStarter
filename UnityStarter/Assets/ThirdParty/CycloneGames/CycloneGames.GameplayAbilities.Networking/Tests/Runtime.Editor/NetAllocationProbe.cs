using System;
using NUnit.Framework;

namespace CycloneGames.GameplayAbilities.Networking.Tests.Runtime.Editor
{
    internal static class NetAllocationProbe
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
    }
}
