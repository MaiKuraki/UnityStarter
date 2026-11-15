using System;
using System.Diagnostics;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Long-run and randomised coverage for the integrated spatial index. Member churn is part of
    /// every window so slot recycling is exercised; a soak that only moves entities never touches
    /// the free list and cannot detect a slot leak.
    /// </summary>
    public sealed class InteractionGridSoakFuzzTests
    {
        private const int Scale = 10_000;
        private const float CellSize = 10f;
        private const float QueryRadius = 12f;

        [Test]
        [Timeout(900_000)]
        public void Soak_ChurnQueryAndUpdateStayBounded()
        {
            var grid = new SpatialHashGrid<int>(CellSize, Scale);
            var handles = new SpatialHandle[Scale + 8_192];
            var alive = new bool[Scale + 8_192];
            var positions = new float[(Scale + 8_192) * 2];
            var results = new int[256];
            var random = new DeterministicRng(20261115UL);

            for (int i = 0; i < Scale; i++)
            {
                float x = random.NextFloat(-500f, 500f);
                float z = random.NextFloat(-500f, 500f);
                positions[i * 2] = x;
                positions[i * 2 + 1] = z;
                Assert.That(grid.TryInsert(i, x, 0f, z, out handles[i]), Is.True);
                alive[i] = true;
            }

            long soakStartTicks = Stopwatch.GetTimestamp();
            int slotCapacityStart = grid.GetStatistics().SlotCapacity;
            int gen0Start = GC.CollectionCount(0);
            const int Windows = 60;
            const int QueriesPerWindow = 500_000;
            const int MovesPerWindow = 5_000;
            const int ChurnPerWindow = 1_000;

            double firstSteady = 0;
            double lastSteady = 0;
            double worstSteady = double.MaxValue;
            long totalChurnInsert = 0;
            long totalChurnRemove = 0;
            int windows = 0;

            var csv = new StressCsvWriter("soak_grid_v2.csv", StressPaths.SoakHeader);
            try
            {
                for (int window = 0; window < Windows; window++)
                {
                    var histogram = new LatencyHistogram(10L, 8192);
                    long start = Stopwatch.GetTimestamp();

                    for (int q = 0; q < QueriesPerWindow; q++)
                    {
                        float cx = random.NextFloat(-500f, 500f);
                        float cz = random.NextFloat(-500f, 500f);
                        long opStart = Stopwatch.GetTimestamp();
                        grid.QuerySphere(cx, 0f, cz, QueryRadius, results, results.Length);
                        histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);
                    }

                    double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
                    double opsPerSecond = elapsedNs <= 0 ? 0 : QueriesPerWindow / (elapsedNs / 1_000_000_000.0);

                    for (int m = 0; m < MovesPerWindow; m++)
                    {
                        int id = random.NextInt(Scale);
                        if (!alive[id]) continue;
                        float x = random.NextFloat(-500f, 500f);
                        float z = random.NextFloat(-500f, 500f);
                        positions[id * 2] = x;
                        positions[id * 2 + 1] = z;
                        grid.TryUpdate(handles[id], x, 0f, z);
                    }

                    // Churn must be strictly 1:1 and must reuse the freed slot: remove one member,
                    // then immediately re-insert it with a new position. Pairing on the same id keeps
                    // the population constant and exercises free-list recycling every window.
                    for (int c = 0; c < ChurnPerWindow; c++)
                    {
                        int victim = random.NextInt(Scale);
                        if (!alive[victim]) continue;
                        if (!grid.TryRemove(handles[victim])) continue;

                        alive[victim] = false;
                        totalChurnRemove++;

                        float x = random.NextFloat(-500f, 500f);
                        float z = random.NextFloat(-500f, 500f);
                        positions[victim * 2] = x;
                        positions[victim * 2 + 1] = z;
                        if (grid.TryInsert(victim, x, 0f, z, out SpatialHandle churnHandle))
                        {
                            handles[victim] = churnHandle;
                            alive[victim] = true;
                            totalChurnInsert++;
                        }
                    }

                    Assert.That(grid.ValidateStructure(), Is.True, "window " + window);

                    SpatialStatistics stats = grid.GetStatistics();
                    csv.Row(
                        StressCsvWriter.Int(window + 1),
                        StressCsvWriter.Num(opsPerSecond, 0),
                        StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                        StressCsvWriter.Num(histogram.PercentileUs(0.95), 3),
                        StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                        StressCsvWriter.Num(StressEnvironment.ManagedHeapMb, 1),
                        StressCsvWriter.Int(stats.ElementCount),
                        StressCsvWriter.Int(stats.SlotCapacity),
                        StressCsvWriter.Int(stats.OccupiedCellCount),
                        StressCsvWriter.Int(GC.CollectionCount(0)),
                        StressCsvWriter.Int(stats.GrowthCount));

                    if (window == 2)
                    {
                        firstSteady = opsPerSecond;
                    }

                    if (window >= 2)
                    {
                        lastSteady = opsPerSecond;
                        if (opsPerSecond < worstSteady) worstSteady = opsPerSecond;
                    }

                    windows++;
                }
            }
            finally
            {
                csv.Dispose();
            }

            SpatialStatistics final = grid.GetStatistics();
            double elapsedSeconds = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - soakStartTicks) / 1_000_000_000.0;
            int gen0End = GC.CollectionCount(0);
            double steadyDecay = firstSteady <= 0 ? 0 : lastSteady / firstSteady;

            var summary = new StressCsvWriter("soak_grid_v2_summary.csv", StressPaths.SoakSummaryHeader);
            try
            {
                summary.Row(
                    "soak_grid_churn_query_update",
                    StressCsvWriter.Int(windows),
                    StressCsvWriter.Int(Scale),
                    StressCsvWriter.Int(Windows),
                    StressCsvWriter.Num(elapsedSeconds, 1),
                    StressCsvWriter.Num(firstSteady, 0),
                    StressCsvWriter.Num(lastSteady, 0),
                    StressCsvWriter.Num(worstSteady == double.MaxValue ? 0 : worstSteady, 0),
                    StressCsvWriter.Num(steadyDecay, 4),
                    StressCsvWriter.Num(0, 3),
                    StressCsvWriter.Int(gen0End - gen0Start),
                    StressCsvWriter.Int(slotCapacityStart),
                    StressCsvWriter.Int(final.SlotCapacity),
                    StressCsvWriter.Int(final.SlotCapacity - slotCapacityStart),
                    StressCsvWriter.Int(totalChurnInsert),
                    StressCsvWriter.Int(totalChurnRemove),
                    "true");
            }
            finally
            {
                summary.Dispose();
            }

            Assert.That(totalChurnInsert, Is.GreaterThan(1_000), "churn must actually insert members");
            Assert.That(totalChurnRemove, Is.GreaterThan(1_000), "churn must actually remove members");
            Assert.That(final.ElementCount, Is.GreaterThan(Scale / 2),
                "1:1 churn must keep the population near its starting size, was " + final.ElementCount);
            Assert.That(final.SlotCapacity, Is.EqualTo(slotCapacityStart),
                "slot capacity must not grow once churn recycles freed slots");
            Assert.That(steadyDecay, Is.GreaterThan(0.80),
                "steady-state throughput decayed past 20%: " + steadyDecay.ToString("F4"));

            grid.Dispose();
        }

        [Test]
        public void Fuzz_RandomOperationSequenceKeepsInvariants()
        {
            const int Capacity = 4_096;
            var grid = new SpatialHashGrid<int>(8f, Capacity);
            var handles = new SpatialHandle[Capacity];
            var alive = new bool[Capacity];
            var positions = new float[Capacity * 2];
            var results = new int[64];
            var random = new DeterministicRng(0xF00DUL);

            int live = 0;
            int nextId = 0;
            long oracleChecks = 0;
            long oracleMismatch = 0;

            var csv = new StressCsvWriter("fuzz_grid_v2.csv", StressPaths.FuzzHeader);
            try
            {
                for (int iteration = 0; iteration < 20_000; iteration++)
                {
                    int operation = random.NextInt(100);

                    if (operation < 40 && nextId < Capacity)
                    {
                        float x = random.NextFloat(-400f, 400f);
                        float z = random.NextFloat(-400f, 400f);
                        positions[nextId * 2] = x;
                        positions[nextId * 2 + 1] = z;
                        if (grid.TryInsert(nextId, x, 0f, z, out SpatialHandle handle))
                        {
                            handles[nextId] = handle;
                            alive[nextId] = true;
                            nextId++;
                            live++;
                        }
                    }
                    else if (operation < 70 && live > 0)
                    {
                        int victim = random.NextInt(nextId);
                        if (alive[victim])
                        {
                            Assert.That(grid.TryRemove(handles[victim]), Is.True);
                            alive[victim] = false;
                            live--;
                        }
                    }
                    else if (live > 0)
                    {
                        int mover = random.NextInt(nextId);
                        if (alive[mover])
                        {
                            float x = random.NextFloat(-400f, 400f);
                            float z = random.NextFloat(-400f, 400f);
                            positions[mover * 2] = x;
                            positions[mover * 2 + 1] = z;
                            grid.TryUpdate(handles[mover], x, 0f, z);
                        }
                    }

                    if ((iteration & 255) == 0)
                    {
                        Assert.That(grid.Count, Is.EqualTo(live), "live count drifted at iteration " + iteration);
                        Assert.That(grid.ValidateStructure(), Is.True,
                            "structure broken at iteration " + iteration);

                        float cx = random.NextFloat(-400f, 400f);
                        float cz = random.NextFloat(-400f, 400f);
                        int count = grid.QuerySphere(cx, 0f, cz, 24f, results, results.Length);
                        int expected = CountBruteForce(positions, alive, nextId, cx, cz, 24f);
                        oracleChecks++;
                        if (count != expected)
                        {
                            oracleMismatch++;
                        }

                        csv.Row(
                            StressCsvWriter.Int(iteration),
                            StressCsvWriter.Int(operation),
                            StressCsvWriter.Int(live),
                            StressCsvWriter.Int(grid.GetStatistics().SlotCapacity),
                            StressCsvWriter.Int(grid.GetStatistics().OccupiedCellCount),
                            StressCsvWriter.Int(oracleChecks),
                            StressCsvWriter.Int(oracleMismatch),
                            "true");
                    }
                }
            }
            finally
            {
                csv.Dispose();
            }

            Assert.That(oracleMismatch, Is.Zero, "query results diverged from the brute-force oracle");
            Assert.That(oracleChecks, Is.GreaterThan(50));
            Assert.That(grid.ValidateStructure(), Is.True);

            grid.Dispose();
        }

        private static int CountBruteForce(float[] positions, bool[] alive, int count, float cx, float cz, float radius)
        {
            float radiusSqr = radius * radius;
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
                if (!alive[i]) continue;
                float dx = positions[i * 2] - cx;
                float dz = positions[i * 2 + 1] - cz;
                if (dx * dx + dz * dz <= radiusSqr)
                {
                    hits++;
                }
            }

            return hits;
        }
    }
}
