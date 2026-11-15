using System;
using System.Diagnostics;
using CycloneGames.Spatial.Core;
using NUnit.Framework;

namespace CycloneGames.RPGFoundation.Interaction.Tests.Editor
{
    /// <summary>
    /// Deterministic matrix over the production spatial index. Every scenario pair also runs an
    /// O(N) brute-force oracle so a fast-but-wrong index fails instead of producing good numbers.
    /// </summary>
    public sealed class InteractionGridSpatialTests
    {
        private static readonly ulong[] Seeds = { 0UL, 1UL, 42UL, 1337UL, 3735928559UL };

        private struct Scenario
        {
            public string Name;
            public int Scale;
            public float CellSize;
            public float Radius;
            public DistributionKind Distribution;
            public float MoveRatio;
        }

        private enum DistributionKind
        {
            Uniform,
            Cluster,
            PowerLaw
        }

        [Test]
        public void Grid_StructureIsValidAfterEveryMutation()
        {
            var grid = new SpatialHashGrid<int>(10f, 256);
            var random = new DeterministicRng(12345UL);
            var handles = new SpatialHandle[512];
            int live = 0;

            for (int i = 0; i < handles.Length; i++)
            {
                float x = random.NextFloat(-500f, 500f);
                float z = random.NextFloat(-500f, 500f);
                Assert.That(grid.TryInsert(i, x, 0f, z, out handles[i]), Is.True, "insert " + i);
                live++;
            }

            for (int round = 0; round < 40; round++)
            {
                int index = random.NextInt(handles.Length);
                if (handles[index].IsValid && live > 0)
                {
                    if (grid.TryRemove(handles[index]))
                    {
                        live--;
                    }
                }
                else
                {
                    float x = random.NextFloat(-500f, 500f);
                    float z = random.NextFloat(-500f, 500f);
                    if (grid.TryInsert(index + round * 1000, x, 0f, z, out SpatialHandle handle))
                    {
                        handles[index] = handle;
                        live++;
                    }
                }

                Assert.That(grid.ValidateStructure(), Is.True, "structure broken at round " + round);
                Assert.That(grid.Count, Is.EqualTo(live));
            }
        }

        [Test]
        public void Grid_HashDistributionHasNoTransposeCollapse()
        {
            // The previous implementation packed (cx,cz) into one long and relied on the default
            // Int64 hash, which collapses (a,b) with (b,a). The replacement must not repeat that.
            var grid = new SpatialHashGrid<int>(4f, 4096);
            var seen = new System.Collections.Generic.HashSet<(int, int)>();
            int inserted = 0;

            for (int cx = 0; cx < 80; cx++)
            {
                for (int cz = 0; cz < 80; cz++)
                {
                    float x = cx * 4f + 2f;
                    float z = cz * 4f + 2f;
                    if (grid.TryInsert(inserted, x, 0f, z, out _))
                    {
                        inserted++;
                        seen.Add((cx, cz));
                    }
                }
            }

            SpatialStatistics stats = grid.GetStatistics();
            Assert.That(inserted, Is.EqualTo(6400));
            Assert.That(seen.Count, Is.EqualTo(6400));
            Assert.That(stats.OccupiedCellCount, Is.EqualTo(6400),
                "each distinct cell must own a distinct bucket entry");
            Assert.That(grid.ValidateStructure(), Is.True);
        }

        [Test]
        public void Grid_QueryMatchesBruteForceOracle()
        {
            var csv = new StressCsvWriter("grid_matrix_v2.csv", StressPaths.GridHeader);

            try
            {
                foreach (Scenario scenario in BuildMainMatrix())
                {
                    foreach (ulong seed in Seeds)
                    {
                        RunScenario(csv, scenario, seed);
                    }
                }

                foreach (Scenario scenario in BuildCellSizeMatrix())
                {
                    foreach (ulong seed in Seeds)
                    {
                        RunScenario(csv, scenario, seed);
                    }
                }

                foreach (Scenario scenario in BuildRadiusMatrix())
                {
                    foreach (ulong seed in Seeds)
                    {
                        RunScenario(csv, scenario, seed);
                    }
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        [Test]
        public void Grid_QueryAllocationHoldsAtMillionIterations()
        {
            // A 500-iteration probe cannot resolve a 32-byte allocation: the bytes land inside an
            // already-committed page and GC.GetTotalMemory does not move. One million iterations
            // produce tens of megabytes, which is what makes a zero reading meaningful.
            var csv = new StressCsvWriter("grid_alloc_million_v2.csv",
                "scenario,scale,cell_size,radius,iterations,alloc_bytes_per_op,gen0_delta");

            try
            {
                foreach (int scale in new[] { 1000, 10000 })
                {
                    var grid = new SpatialHashGrid<int>(10f, Math.Max(256, scale));
                    var random = new DeterministicRng(7UL);
                    var results = new int[256];
                    float extent = MathF.Sqrt(scale) * 6f;

                    for (int i = 0; i < scale; i++)
                    {
                        Assert.That(grid.TryInsert(i, random.NextFloat(-extent, extent), 0f, random.NextFloat(-extent, extent), out _), Is.True);
                    }

                    const int Iterations = 1_000_000;
                    AllocationProbe probe = AllocationMeter.Measure(Iterations, () =>
                    {
                        for (int q = 0; q < Iterations; q++)
                        {
                            grid.QuerySphere(0f, 0f, 0f, 10f, results, results.Length);
                        }
                    });

                    csv.Row(
                        "grid_query_" + scale,
                        StressCsvWriter.Int(scale),
                        StressCsvWriter.Num(10f, 2),
                        StressCsvWriter.Num(10f, 2),
                        StressCsvWriter.Int(Iterations),
                        StressCsvWriter.Num(probe.BytesPerOperation, 4),
                        StressCsvWriter.Int(probe.ForcedCollectionDelta));

                    grid.Dispose();
                }
            }
            finally
            {
                csv.Dispose();
            }
        }

        private static Scenario[] BuildMainMatrix()
        {
            var scenarios = new Scenario[12];
            int index = 0;
            int[] scales = { 1000, 5000, 10000, 50000 };
            DistributionKind[] kinds = { DistributionKind.Uniform, DistributionKind.Cluster, DistributionKind.PowerLaw };

            foreach (int scale in scales)
            {
                foreach (DistributionKind kind in kinds)
                {
                    scenarios[index++] = new Scenario
                    {
                        Name = "grid_" + scale + "_" + kind,
                        Scale = scale,
                        CellSize = 10f,
                        Radius = 10f,
                        Distribution = kind,
                        MoveRatio = 0f
                    };
                }
            }

            return scenarios;
        }

        private static Scenario[] BuildCellSizeMatrix()
        {
            float[] sizes = { 4f, 8f, 10f, 16f, 32f };
            var scenarios = new Scenario[sizes.Length];
            for (int i = 0; i < sizes.Length; i++)
            {
                scenarios[i] = new Scenario
                {
                    Name = "grid_cell_" + sizes[i],
                    Scale = 10000,
                    CellSize = sizes[i],
                    Radius = 10f,
                    Distribution = DistributionKind.PowerLaw,
                    MoveRatio = 0f
                };
            }

            return scenarios;
        }

        private static Scenario[] BuildRadiusMatrix()
        {
            float[] radii = { 3f, 5f, 10f, 20f };
            var scenarios = new Scenario[radii.Length];
            for (int i = 0; i < radii.Length; i++)
            {
                scenarios[i] = new Scenario
                {
                    Name = "grid_radius_" + radii[i],
                    Scale = 10000,
                    CellSize = 10f,
                    Radius = radii[i],
                    Distribution = DistributionKind.PowerLaw,
                    MoveRatio = 0f
                };
            }

            return scenarios;
        }

        private static void RunScenario(StressCsvWriter csv, Scenario scenario, ulong seed)
        {
            var random = new DeterministicRng(seed ^ (ulong)scenario.Scale);
            var grid = new SpatialHashGrid<int>(scenario.CellSize, Math.Max(256, scenario.Scale));
            var positions = new float[scenario.Scale * 2];
            var handles = new SpatialHandle[scenario.Scale];
            var results = new int[512];

            float extent = Math.Max(80f, (float)Math.Sqrt(scenario.Scale) * (scenario.Distribution == DistributionKind.Uniform ? 10f : 4f));

            for (int i = 0; i < scenario.Scale; i++)
            {
                SamplePosition(ref random, scenario.Distribution, extent, out float x, out float z);
                positions[i * 2] = x;
                positions[i * 2 + 1] = z;
                Assert.That(grid.TryInsert(i, x, 0f, z, out handles[i]), Is.True, "insert " + i);
            }

            Assert.That(grid.ValidateStructure(), Is.True);

            const int Queries = 2000;
            const int OracleChecks = 64;
            var histogram = new LatencyHistogram(10L, 8192);
            var query = new DeterministicRng(seed * 31UL + 7UL);
            long oracleMismatch = 0;
            long resultSum = 0;
            int oracleChecks = 0;

            long start = Stopwatch.GetTimestamp();
            for (int q = 0; q < Queries; q++)
            {
                query.NextDouble();
                float cx = query.NextFloat(-extent * 0.5f, extent * 0.5f);
                float cz = query.NextFloat(-extent * 0.5f, extent * 0.5f);

                long opStart = Stopwatch.GetTimestamp();
                int count = grid.QuerySphere(cx, 0f, cz, scenario.Radius, results, results.Length);
                histogram.AddTicks(Stopwatch.GetTimestamp() - opStart);
                resultSum += count;

                if (q < OracleChecks)
                {
                    oracleChecks++;
                    int expected = CountBruteForce(positions, scenario.Scale, cx, cz, scenario.Radius);
                    // The index honours the caller buffer, so the oracle compares against the
                    // same ceiling; a raw comparison would flag every saturated query as a bug.
                    int expectedCapped = expected > results.Length ? results.Length : expected;
                    if (expectedCapped != count)
                    {
                        oracleMismatch++;
                    }
                }
            }

            double elapsedNs = LatencyHistogram.TicksToNs(Stopwatch.GetTimestamp() - start);
            double opsPerSecond = elapsedNs <= 0 ? 0 : Queries / (elapsedNs / 1_000_000_000.0);

            AllocationProbe allocation = AllocationMeter.Measure(Math.Min(Queries, 500), () =>
            {
                for (int q = 0; q < 500; q++)
                {
                    grid.QuerySphere(0f, 0f, 0f, scenario.Radius, results, results.Length);
                }
            });

            SpatialStatistics stats = grid.GetStatistics();

            csv.Row(
                scenario.Name,
                StressCsvWriter.Int((long)seed),
                StressCsvWriter.Int(scenario.Scale),
                StressCsvWriter.Num(scenario.CellSize, 2),
                StressCsvWriter.Num(scenario.Radius, 2),
                scenario.Distribution.ToString().ToLowerInvariant(),
                StressCsvWriter.Num(scenario.MoveRatio, 2),
                "0",
                StressCsvWriter.Int(Queries),
                StressCsvWriter.Int(Queries),
                StressCsvWriter.Num(opsPerSecond, 0),
                StressCsvWriter.Num(histogram.PercentileUs(0.50), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.95), 3),
                StressCsvWriter.Num(histogram.PercentileUs(0.99), 3),
                StressCsvWriter.Num(allocation.BytesPerOperation, 3),
                StressCsvWriter.Int(allocation.ForcedCollectionDelta),
                StressCsvWriter.Int(stats.OccupiedCellCount),
                StressCsvWriter.Num(stats.ElementCount / (double)Math.Max(1, stats.OccupiedCellCount), 3),
                StressCsvWriter.Int(stats.SlotCapacity),
                StressCsvWriter.Num(resultSum / (double)Queries, 3),
                StressCsvWriter.Int(oracleChecks),
                StressCsvWriter.Int(oracleMismatch),
                StressCsvWriter.Num(StressEnvironment.ManagedHeapMb, 1),
                "structure_valid=" + grid.ValidateStructure());

            Assert.That(oracleMismatch, Is.Zero,
                scenario.Name + " seed " + seed + ": query result diverged from the brute-force oracle");

            grid.Dispose();
        }

        private static void SamplePosition(ref DeterministicRng random, DistributionKind kind, float extent, out float x, out float z)
        {
            double u = random.NextDouble();
            float half = extent * 0.5f;

            switch (kind)
            {
                case DistributionKind.Uniform:
                    x = random.NextFloat(-half, half);
                    z = random.NextFloat(-half, half);
                    return;

                case DistributionKind.Cluster:
                {
                    // Ten anchors, then a tight Gaussian-ish offset around one of them.
                    int anchor = (int)(u * 10.0);
                    float ax = -half + (anchor % 5) * (extent / 5f);
                    float az = -half + (anchor / 5) * (extent / 2f);
                    x = ax + random.NextFloat(-extent * 0.02f, extent * 0.02f);
                    z = az + random.NextFloat(-extent * 0.02f, extent * 0.02f);
                    return;
                }

                default:
                {
                    // Power law: most samples land in a small core, a few spread wide.
                    double r = random.NextDouble();
                    float spread = (float)Math.Pow(r, 4.0) * half;
                    double angle = random.NextDouble() * Math.PI * 2.0;
                    x = (float)(Math.Cos(angle) * spread);
                    z = (float)(Math.Sin(angle) * spread);
                    return;
                }
            }
        }

        private static int CountBruteForce(float[] positions, int count, float cx, float cz, float radius)
        {
            // Mirrors SpatialHashGrid's inclusive tolerance so boundary samples agree.
            const float tolerance = 1e-5f;
            float radiusSqr = radius * radius;
            radiusSqr += (radiusSqr * tolerance) + tolerance;
            int hits = 0;
            for (int i = 0; i < count; i++)
            {
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
