using System;
using System.Threading;
using System.Threading.Tasks;
using NUnit.Framework;
using CycloneGames.EventBus.Core;
using CycloneGames.EventBus.Runtime;

namespace CycloneGames.EventBus.Tests
{
    /// <summary>
    /// Long-running stability evidence: sustained churn, soak-level concurrency, and sustained
    /// bursts under a budgeted drain. These complement the per-path zero-allocation tests by
    /// asserting on system-level invariants (flat memory, exact delivery, bounded frame cost)
    /// rather than single-operation costs.
    /// </summary>
    public sealed class EventBusStabilityTests
    {
        [Test]
        public void LongRun_Churn_MemoryFootprintStaysBounded()
        {
            var bus = new EventBus<StabilityEvent>();
            var stream = new EventStream<StabilityEvent>(256);
            var queue = new MpscEventQueue<StabilityEvent>(256);
            Action<StabilityEvent> handler = _ => { };
            bus.Subscribe(handler);

            const int cycles = 50000;
            Action run = () =>
            {
                for (int index = 0; index < cycles; index++)
                {
                    // Steady-state churn across every container: subscribe/release, publish,
                    // stream write/flush, queue enqueue/drain.
                    bus.Subscribe(handler).Dispose();
                    bus.Publish(new StabilityEvent { Value = index });
                    stream.TryWrite(new StabilityEvent { Value = index });
                    stream.FlushTo(bus);
                    queue.TryEnqueue(new StabilityEvent { Value = index });
                    queue.FlushTo(bus);
                }
            };

            run(); // Warm-up: reaches steady-state capacities before measuring.
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            run();

            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long growth = GC.GetTotalMemory(true) - before;

            // A flat-memory design grows by nothing here; the bound only absorbs measurement noise
            // from the surrounding Editor process. A regression (unbounded pool, leaking handles,
            // retained buffers) would exceed it by orders of magnitude.
            Assert.Less(growth, 2 * 1024 * 1024,
                $"Memory grew by {growth} bytes over {cycles} churn cycles after warm-up.");
            Assert.AreEqual(1, bus.SubscriptionCount, "Churn leaked subscriptions.");
        }

        [Test]
        public void MpscSoak_MultiProducerConcurrentConsumer_ExactDelivery()
        {
            const int producerCount = 4;
            const int eventsPerProducer = 20000;
            const int capacity = 1024; // Deliberately smaller than the burst: exercises backpressure.

            var queue = new MpscEventQueue<StabilityEvent>(capacity);
            long deliveredCount = 0;
            long deliveredSum = 0;

            var producers = new Task[producerCount];
            for (int producer = 0; producer < producerCount; producer++)
            {
                int producerId = producer;
                producers[producer] = Task.Run(() =>
                {
                    for (int index = 0; index < eventsPerProducer; index++)
                    {
                        var evt = new StabilityEvent { Value = producerId * eventsPerProducer + index };

                        // Backpressure contract: retry until accepted. Nothing is lost.
                        while (!queue.TryEnqueue(in evt))
                        {
                            Thread.SpinWait(64);
                        }
                    }
                });
            }

            // The consumer drains concurrently, in bursts, the way a frame-scheduled pump would.
            long expectedCount = (long)producerCount * eventsPerProducer;
            while (deliveredCount < expectedCount)
            {
                while (queue.TryDequeue(out StabilityEvent evt))
                {
                    deliveredSum += evt.Value;
                    deliveredCount++;
                }

                if (deliveredCount < expectedCount)
                {
                    Thread.SpinWait(64);
                }
            }

            Task.WaitAll(producers);

            // Sum equality catches both loss and duplication, not just a count mismatch.
            long expectedSum = expectedCount * (expectedCount - 1) / 2;
            Assert.AreEqual(expectedCount, deliveredCount);
            Assert.AreEqual(expectedSum, deliveredSum);
        }

        [Test]
        public void SustainedBurst_BudgetedDrain_FrameCostStaysBounded()
        {
            const int frames = 500;
            const int writesPerFrame = 300;
            const int perTargetBudget = 512;
            const int perTickBudget = 1024;

            var queue = new MpscEventQueue<StabilityEvent>(4096);
            var bus = new EventBus<StabilityEvent>();
            long received = 0;
            bus.Subscribe(_ => received++);
            var pump = new EventBusPump();
            pump.AddQueue(queue, bus);

            for (int frame = 0; frame < frames; frame++)
            {
                for (int index = 0; index < writesPerFrame; index++)
                {
                    Assert.IsTrue(queue.TryEnqueue(new StabilityEvent { Value = index }),
                        "Queue refused a write although capacity exceeds the sustained rate.");
                }

                // Frame-cost invariant: no single drain may exceed the configured tick ceiling.
                int published = pump.Drain(perTargetBudget, perTickBudget);
                Assert.LessOrEqual(published, perTickBudget, $"Frame {frame} exceeded the tick budget.");
            }

            // Trailing drain to empty.
            while (!queue.IsEmpty)
            {
                pump.Drain(perTargetBudget, perTickBudget);
            }

            Assert.AreEqual((long)frames * writesPerFrame, received);
            Assert.AreEqual(0, queue.RejectedCount);
            Assert.AreEqual(0, queue.DroppedCount);
        }

        [Test]
        public void SustainedLoad_MixedPaths_ZeroAllocationAfterWarmup()
        {
            var bus = new EventBus<StabilityEvent>();
            var stream = new EventStream<StabilityEvent>(64);
            var queue = new MpscEventQueue<StabilityEvent>(64);
            Action<StabilityEvent> handler = _ => { };
            bus.Subscribe(handler);

            const int cycles = 20000;
            Action run = () =>
            {
                for (int index = 0; index < cycles; index++)
                {
                    bus.Subscribe(handler).Dispose();
                    bus.Publish(new StabilityEvent { Value = index });
                    stream.TryWrite(new StabilityEvent { Value = index });
                    stream.FlushTo(bus);
                    queue.TryEnqueue(new StabilityEvent { Value = index });
                    queue.FlushTo(bus);
                }
            };

            run();

            long before = GC.GetAllocatedBytesForCurrentThread();
            run();
            long allocated = GC.GetAllocatedBytesForCurrentThread() - before;

            // Proxy assertion: managed allocation on this thread only. Device proof still requires
            // a target Player + Profiler pass under the shipping backend.
            Assert.AreEqual(0, allocated,
                $"Sustained mixed-path load allocated {allocated} bytes after warm-up.");
        }

        private struct StabilityEvent
        {
            public int Value;
        }
    }
}
