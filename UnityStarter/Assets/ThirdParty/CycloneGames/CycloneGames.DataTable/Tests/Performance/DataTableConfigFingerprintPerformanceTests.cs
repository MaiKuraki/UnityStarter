using System;
using System.Diagnostics;

using NUnit.Framework;

using Unity.PerformanceTesting;

namespace CycloneGames.DataTable.Tests.Performance
{
    public sealed class DataTableConfigFingerprintPerformanceTests
    {
        private const int WarmupCount = 5;
        private const int MeasurementCount = 15;
        private const int SmallEntryCount = 1_000;
        private const int LargeEntryCount = 10_000;

        // Regression gates, intentionally generous so slow CI hosts cannot flake them while
        // pathological regressions (quadratic behavior, per-entry allocation) still fail.
        private const long MaxLargeComputeMilliseconds = 1_000;
        private const long MaxLargeComputeAllocatedBytes = 2L * 1024L * 1024L;

        // LargeEntryCount exceeds the default 4,096-table guardrail, so the fixture manifests
        // carry their own admission budget.
        private static readonly DataTableLoadLimits ManifestLimits = new DataTableLoadLimits(
            maxTableCount: 16_384,
            maxBytesPerTable: 64 * 1024 * 1024,
            maxTotalBytes: 512L * 1024L * 1024L);

        private DataTableManifest _smallManifest;
        private DataTableManifest _largeManifest;
        private DataTableConfigFingerprint _fingerprintSink;

        [OneTimeSetUp]
        public void OneTimeSetUp()
        {
            _smallManifest = CreateManifest(SmallEntryCount);
            _largeManifest = CreateManifest(LargeEntryCount);
        }

        [Test, Performance]
        public void ComputeOneThousandEntries()
        {
            Measure.Method(ComputeSmall)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1)
                .SampleGroup(new SampleGroup("DataTableConfigFingerprint.Compute.1k", SampleUnit.Millisecond))
                .GC()
                .Run();
        }

        [Test, Performance]
        public void ComputeTenThousandEntries()
        {
            Measure.Method(ComputeLarge)
                .WarmupCount(WarmupCount)
                .MeasurementCount(MeasurementCount)
                .IterationsPerMeasurement(1)
                .SampleGroup(new SampleGroup("DataTableConfigFingerprint.Compute.10k", SampleUnit.Millisecond))
                .GC()
                .Run();
        }

        [Test]
        public void ComputeTenThousandEntries_StaysWithinTimeAndAllocationGates()
        {
            for (int i = 0; i < WarmupCount; i++)
            {
                ComputeLarge();
            }

            long allocatedBefore = GC.GetAllocatedBytesForCurrentThread();
            var stopwatch = Stopwatch.StartNew();
            ComputeLarge();
            stopwatch.Stop();
            long allocatedBytes = GC.GetAllocatedBytesForCurrentThread() - allocatedBefore;

            Assert.That(
                stopwatch.ElapsedMilliseconds,
                Is.LessThan(MaxLargeComputeMilliseconds),
                $"Fingerprinting {LargeEntryCount} entries took {stopwatch.ElapsedMilliseconds} ms; " +
                $"the regression gate allows {MaxLargeComputeMilliseconds} ms on this host.");
            Assert.That(
                allocatedBytes,
                Is.LessThan(MaxLargeComputeAllocatedBytes),
                $"Fingerprinting {LargeEntryCount} entries allocated {allocatedBytes} bytes; " +
                $"cold-path allocation must stay bounded below {MaxLargeComputeAllocatedBytes} bytes.");
        }

        private void ComputeSmall()
        {
            _fingerprintSink = DataTableConfigFingerprint.Compute(_smallManifest);
        }

        private void ComputeLarge()
        {
            _fingerprintSink = DataTableConfigFingerprint.Compute(_largeManifest);
        }

        private static DataTableManifest CreateManifest(int entryCount)
        {
            var entries = new DataTableManifestEntry[entryCount];
            for (int i = 0; i < entries.Length; i++)
            {
                entries[i] = new DataTableManifestEntry(
                    "Table." + i.ToString("D5"),
                    "config/table_" + i.ToString("D5"),
                    expectedByteLength: 1_024 + i,
                    sha256Hex: i.ToString("x64"));
            }

            return new DataTableManifest(1, entries, ManifestLimits);
        }
    }
}
