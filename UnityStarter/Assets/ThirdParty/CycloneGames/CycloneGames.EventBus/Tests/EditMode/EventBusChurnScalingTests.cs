using System;
using System.Collections.Generic;
using NUnit.Framework;
using CycloneGames.EventBus.Core;

namespace CycloneGames.EventBus.Tests
{
    /// <summary>
    /// Scaling checks for the subscription churn path. The per-path tests use a single handle and a
    /// small live set, which does not expose an O(live) reuse scan or a fixed handle-pool ceiling;
    /// these checks do.
    ///
    /// Reuse cost is pinned against the live subscriber count; handle recycling is pinned against the
    /// churn amplitude; dispatch fanout is a coarse budget that must stay green on every revision.
    ///
    /// Timing is min-of-N rounds to shed Mono/JIT noise. Absolute numbers are printed with an
    /// "[EventBus Scaling]" prefix so a measurement excerpt is self-describing.
    ///
    /// Allocation note: a host may report a constant 0 from GC.GetAllocatedBytesForCurrentThread(),
    /// which makes a byte-level assertion vacuous. The handle-recycling check therefore counts the
    /// exact thing the pool controls: whether a subscribe rents a recycled handle or must construct a
    /// new one. Constructing a new handle IS the allocation, so counting new instances is a stricter,
    /// GC-independent signal than a byte counter.
    /// </summary>
    public sealed class EventBusChurnScalingTests
    {
        private const string Prefix = "[EventBus Scaling]";

        private struct ChurnEvent
        {
            public int Value;
        }

        // ------------------------------------------------------------------ reuse cost

        [Test]
        public void SubscribeChurn_ReuseCostDoesNotScaleWithLiveSubscribers()
        {
            const int opsPerRound = 2000;
            const int rounds = 5;

            double cost64 = MeasureChurnNsPerOp(64, opsPerRound, rounds);
            double cost16384 = MeasureChurnNsPerOp(16384, opsPerRound, rounds);
            double ratio = cost16384 / cost64;

            TestContext.WriteLine($"{Prefix} subscribe churn live=64: {cost64:F1} ns/op");
            TestContext.WriteLine($"{Prefix} subscribe churn live=16384: {cost16384:F1} ns/op");
            TestContext.WriteLine($"{Prefix} reuse-cost ratio(16384/64) = {ratio:F2} (expected <= 4)");

            // A tombstone first-fit scan walks the occupied prefix, so its cost tracks the live
            // count. Constant-time reuse (a free list) keeps the ratio near 1 regardless of the live
            // set. Deliberately loose: it separates O(live) from O(1), it is not a micro-benchmark.
            Assert.LessOrEqual(ratio, 4.0,
                $"Reuse cost scaled with live subscribers: {cost64:F1} ns at live=64 vs "
                + $"{cost16384:F1} ns at live=16384 (ratio {ratio:F2}). Expected <= 4.");
        }

        private static double MeasureChurnNsPerOp(int live, int opsPerRound, int rounds)
        {
            var bus = new EventBus<ChurnEvent>(null, live + 4);

            // `live` resident slots, no tombstones: when the reuse scan runs it must walk the whole
            // occupied prefix to find its hole.
            Action<ChurnEvent> resident = _ => { };
            for (int index = 0; index < live; index++)
            {
                bus.Subscribe(resident);
            }

            Action<ChurnEvent> churn = _ => { };
            for (int index = 0; index < 200; index++)
            {
                bus.Subscribe(churn).Dispose();
            }

            double best = double.MaxValue;
            for (int round = 0; round < rounds; round++)
            {
                long start = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int index = 0; index < opsPerRound; index++)
                {
                    bus.Subscribe(churn).Dispose();
                }

                long end = System.Diagnostics.Stopwatch.GetTimestamp();
                double nsPerOp = (end - start) * (1_000_000_000.0 / System.Diagnostics.Stopwatch.Frequency)
                    / opsPerRound;
                if (nsPerOp < best)
                {
                    best = nsPerOp;
                }
            }

            return best;
        }

        // ------------------------------------------------------------------ handle recycling

        [Test]
        public void HandlePool_RecyclesAboveThePoolCeiling()
        {
            const int amplitude = 256;
            const int measuredCycles = 200;
            const int warmupCycles = 4;

            int newAt256 = CountNewHandleInstances(amplitude, warmupCycles, measuredCycles);
            TestContext.WriteLine(
                $"{Prefix} handle recycling amplitude={amplitude}: new handle instances over {measuredCycles} cycles = {newAt256} (expected 0)");

            // amplitude exceeds the handle-pool ceiling, so each cycle needs `amplitude - ceiling` fresh
            // handles; a pool sized to the bus capacity recycles all of them, leaving zero new instances.
            Assert.AreEqual(0, newAt256,
                $"Churn amplitude {amplitude} constructed {newAt256} new handle instances over "
                + $"{measuredCycles} cycles; the pool failed to recycle.");

            // Boundary control: amplitude 32 is within the pool ceiling too, so both revisions
            // must recycle. This pins the boundary at the pool ceiling.
            int newAt32 = CountNewHandleInstances(32, warmupCycles, measuredCycles);
            TestContext.WriteLine(
                $"{Prefix} handle recycling amplitude=32 (control): new handle instances = {newAt32} (expected 0)");
            Assert.AreEqual(0, newAt32,
                $"Control amplitude 32 constructed {newAt32} new handles; the boundary moved.");
        }

        private static int CountNewHandleInstances(int amplitude, int warmupCycles, int measuredCycles)
        {
            // Capacity above the amplitude: nothing grows on the subscribe path, so the only
            // possible allocation is a handle the pool could not recycle.
            var bus = new EventBus<ChurnEvent>(null, amplitude + 4);
            Action<ChurnEvent> handler = _ => { };
            var handles = new IEventSubscription[amplitude];
            var seen = new HashSet<object>(ReferenceEqualityComparer.Instance);
            int novel = 0;

            Action cycle = () =>
            {
                for (int index = 0; index < amplitude; index++)
                {
                    handles[index] = bus.Subscribe(handler);
                }

                for (int index = 0; index < amplitude; index++)
                {
                    // A recycled handle reappears in `seen`; a fresh one is counted.
                    if (seen.Add(handles[index]))
                    {
                        novel++;
                    }

                    handles[index].Dispose();
                }
            };

            for (int index = 0; index < warmupCycles; index++)
            {
                cycle();
            }

            novel = 0; // The warm-up fills the pool; only the measured phase is counted.
            for (int index = 0; index < measuredCycles; index++)
            {
                cycle();
            }

            return novel;
        }

        // Reference identity comparer: handle equality is reference equality, and this stays exact
        // even if a type were to override Equals later.
        private sealed class ReferenceEqualityComparer : IEqualityComparer<object>
        {
            public static readonly ReferenceEqualityComparer Instance = new ReferenceEqualityComparer();

            public new bool Equals(object x, object y)
            {
                return ReferenceEquals(x, y);
            }

            public int GetHashCode(object obj)
            {
                return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(obj);
            }
        }

        // ------------------------------------------------------------------ dispatch fanout

        [Test]
        public void DispatchFanout_NoCatastrophicRegression()
        {
            const int subscribers = 16;
            const int iterations = 100_000;

            var bus = new EventBus<ChurnEvent>(null, subscribers);
            Action<ChurnEvent> handler = _ => { };
            for (int index = 0; index < subscribers; index++)
            {
                bus.Subscribe(handler);
            }

            var evt = new ChurnEvent { Value = 1 };
            for (int index = 0; index < 1000; index++)
            {
                bus.Publish(in evt);
            }

            long start = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int index = 0; index < iterations; index++)
            {
                bus.Publish(in evt);
            }

            long end = System.Diagnostics.Stopwatch.GetTimestamp();
            double nsPerPublish = (end - start) * (1_000_000_000.0 / System.Diagnostics.Stopwatch.Frequency)
                / iterations;

            TestContext.WriteLine(
                $"{Prefix} dispatch fanout publish({subscribers} subs) = {nsPerPublish:F1} ns/op over {iterations} ops");

            // Generous: the real cost is a few ns/subscriber. This catches only an accidental
            // O(n^2) on the dispatch path.
            Assert.Less(nsPerPublish, 1000.0,
                $"Dispatch regressed to {nsPerPublish:F1} ns/publish for {subscribers} subscribers.");
        }
    }
}
