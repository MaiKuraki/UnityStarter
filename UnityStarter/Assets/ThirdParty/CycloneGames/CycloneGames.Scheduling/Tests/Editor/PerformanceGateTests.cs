using System;
using CycloneGames.Scheduling.Core;
using NUnit.Framework;

namespace CycloneGames.Scheduling.Tests.Editor
{
    /// <summary>
    /// Deterministic performance gates. They assert allocation and memory footprint, which are
    /// machine independent, and deliberately avoid wall-clock thresholds, which are not.
    ///
    /// Allocation is measured with <see cref="GC.GetTotalMemory(bool)"/>. Unity's Mono runtime
    /// reports zero from <c>GC.GetAllocatedBytesForCurrentThread</c> even for megabytes of garbage,
    /// so that API must never be used as a gate here.
    /// </summary>
    public sealed class PerformanceGateTests
    {
        private const int SteadyStateOps = 100_000;
        private const int EmptyTickOps = 1_000_000;
        private const int FootprintCapacity = 65_536;
        private const int SoakOps = 300_000;

        private const long AllocationAllowanceBytes = 262_144;

        [Test]
        public void SteadyState_ScheduleCancelDoesNotAllocate()
        {
            var scheduler = new WorkScheduler(64);
            var items = new GateItem[64];
            for (int index = 0; index < items.Length; index++)
            {
                items[index] = new GateItem();
            }

            for (int index = 0; index < 2000; index++)
            {
                scheduler.TrySchedule(items[index % 64], out WorkItemHandle warm);
                scheduler.Cancel(warm);
            }

            long before = GC.GetTotalMemory(true);
            for (int index = 0; index < SteadyStateOps; index++)
            {
                scheduler.TrySchedule(items[index % 64], out WorkItemHandle handle);
                scheduler.Cancel(handle);
            }

            long delta = GC.GetTotalMemory(false) - before;
            double perOp = delta / (double)SteadyStateOps;

            Console.WriteLine("[gate] schedule+cancel ops=" + SteadyStateOps
                + " delta=" + delta + " B perOp=" + perOp.ToString("0.000") + " B");

            Assert.That(delta, Is.LessThan(AllocationAllowanceBytes),
                "Steady-state scheduling allocated " + delta + " B over " + SteadyStateOps + " operations.");
            Assert.That(perOp, Is.LessThan(4.0),
                "Per-operation allocation reached " + perOp.ToString("0.000") + " B.");
        }

        [Test]
        public void SteadyState_EmptyTickDoesNotAllocate()
        {
            var scheduler = new WorkScheduler(16);

            for (int index = 0; index < 10_000; index++)
            {
                scheduler.Tick(64, double.PositiveInfinity);
            }

            long before = GC.GetTotalMemory(true);
            for (int index = 0; index < EmptyTickOps; index++)
            {
                scheduler.Tick(64, double.PositiveInfinity);
            }

            long delta = GC.GetTotalMemory(false) - before;

            Console.WriteLine("[gate] empty tick ops=" + EmptyTickOps + " delta=" + delta + " B");

            Assert.That(delta, Is.LessThan(AllocationAllowanceBytes),
                "An idle Tick allocated " + delta + " B over " + EmptyTickOps + " calls.");
        }

        [Test]
        public void SlotFootprint_MatchesBuildConfiguration()
        {
            // Release player: 24 B per slot. Editor and Development builds additionally carry the
            // duplicate-registration guard: a reference table of 2 * capacity entries, i.e. 16 B/ slot.
#if DEBUG
            const double ExpectedPerSlot = 40.0;
            const string Configuration = "DEBUG (with registration guard)";
#else
            const double ExpectedPerSlot = 24.0;
            const string Configuration = "RELEASE";
#endif

            long before = GC.GetTotalMemory(true);
            var scheduler = new WorkScheduler(FootprintCapacity);
            long after = GC.GetTotalMemory(true);
            GC.KeepAlive(scheduler);

            double perSlot = (after - before) / (double)FootprintCapacity;

            Console.WriteLine("[gate] capacity=" + FootprintCapacity + " config=" + Configuration
                + " delta=" + (after - before) + " B perSlot=" + perSlot.ToString("0.000")
                + " B expected=" + ExpectedPerSlot.ToString("0.0") + " B");

            Assert.That(perSlot, Is.InRange(ExpectedPerSlot - 1.0, ExpectedPerSlot + 1.0),
                "Per-registration footprint is " + perSlot.ToString("0.000") + " B but "
                + Configuration + " expects " + ExpectedPerSlot.ToString("0.0")
                + " B. A field was added to WorkSlot or the intrusive free list was replaced.");
        }

        [Test]
        public void LongRun_KeepsAccountingAndMemoryFlat()
        {
            var scheduler = new WorkScheduler(128);
            var items = new GateItem[128];
            var handles = new WorkItemHandle[128];
            var random = new Random(20261029);

            for (int index = 0; index < items.Length; index++)
            {
                items[index] = new GateItem();
            }

            long accepted = 0;
            long before = GC.GetTotalMemory(true);

            for (int op = 0; op < SoakOps; op++)
            {
                int slot = random.Next(items.Length);
                int roll = random.Next(100);

                if (roll < 45)
                {
                    if (items[slot].Live)
                    {
                        continue;
                    }

                    if (scheduler.TrySchedule(items[slot], out WorkItemHandle handle))
                    {
                        handles[slot] = handle;
                        items[slot].Live = true;
                        accepted++;
                    }
                }
                else if (roll < 60)
                {
                    if (!items[slot].Live)
                    {
                        continue;
                    }

                    scheduler.Cancel(handles[slot]);
                }
                else
                {
                    scheduler.Tick(random.Next(1, 17), double.PositiveInfinity);
                }
            }

            long delta = GC.GetTotalMemory(false) - before;

            long terminations = 0;
            for (int index = 0; index < items.Length; index++)
            {
                terminations += items[index].TerminationCount;
            }

            SchedulerSnapshot snapshot = scheduler.GetSnapshot();
            long accounted = snapshot.CompletedCount + snapshot.CancelledCount
                + snapshot.FaultedCount + snapshot.ShutdownCount + snapshot.OutstandingCount;

            Console.WriteLine("[gate] soak ops=" + SoakOps + " accepted=" + snapshot.AcceptedCount
                + " terminations=" + terminations + " memoryDelta=" + delta + " B");

            Assert.That(snapshot.AcceptedCount, Is.EqualTo(accounted), "Termination accounting drifted");
            Assert.That(snapshot.AcceptedCount, Is.LessThanOrEqualTo(accepted));
            Assert.That(snapshot.OutstandingCount, Is.InRange(0, scheduler.Capacity));
            Assert.That(delta, Is.LessThan(AllocationAllowanceBytes),
                "A long run grew the heap by " + delta + " B.");

            scheduler.Shutdown();
            Assert.That(scheduler.OutstandingCount, Is.Zero);
        }

        private sealed class GateItem : IWorkItem
        {
            private int _steps;

            public bool Live { get; set; }

            public int TerminationCount { get; private set; }

            public WorkStepResult Step()
            {
                _steps++;
                return _steps >= 3 ? WorkStepResult.Completed : WorkStepResult.Running;
            }

            public void OnTerminated(WorkTerminationReason reason)
            {
                TerminationCount++;
                Live = false;
                _steps = 0;
            }
        }
    }
}
