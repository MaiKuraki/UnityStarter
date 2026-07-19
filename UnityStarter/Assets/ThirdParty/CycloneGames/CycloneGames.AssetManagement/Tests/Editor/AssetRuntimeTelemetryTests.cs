using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;

using NUnit.Framework;

using CycloneGames.AssetManagement.Runtime;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetRuntimeTelemetryTests
    {
        [Test]
        public void Recorder_Keeps_Bounded_Window_In_Order()
        {
            var recorder = new AssetRuntimeTelemetryRecorder(new AssetRuntimeTelemetryOptions(capacity: 2));

            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 1), timestampUtcTicks: 100L));
            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 2), timestampUtcTicks: 200L));
            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 3), timestampUtcTicks: 300L));

            var buffer = new AssetRuntimeTelemetrySample[2];
            int count = recorder.CopyTo(buffer);

            Assert.AreEqual(2, count);
            Assert.AreEqual(2L, buffer[0].Sequence);
            Assert.AreEqual(3L, buffer[1].Sequence);
            Assert.AreEqual(2, buffer[0].Snapshot.ActiveCount);
            Assert.AreEqual(3, buffer[1].Snapshot.ActiveCount);
            Assert.AreEqual(3L, recorder.TotalRecorded);
            Assert.AreEqual(1L, recorder.OverwrittenSampleCount);
        }

        [Test]
        public void Recorder_Respects_Minimum_Sample_Interval()
        {
            var options = new AssetRuntimeTelemetryOptions(
                capacity: 4,
                minimumSampleInterval: TimeSpan.FromTicks(10));
            long monotonicTicks = 100L;
            var recorder = new AssetRuntimeTelemetryRecorder(
                options,
                () => monotonicTicks,
                TimeSpan.TicksPerSecond);

            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 1), timestampUtcTicks: 100L));
            monotonicTicks = 105L;
            Assert.IsFalse(recorder.TryRecord(CreateSnapshot(activeCount: 2), timestampUtcTicks: 105L));
            monotonicTicks = 110L;
            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 3), timestampUtcTicks: 110L));

            var buffer = new AssetRuntimeTelemetrySample[4];
            int count = recorder.CopyTo(buffer);

            Assert.AreEqual(2, count);
            Assert.AreEqual(1, buffer[0].Snapshot.ActiveCount);
            Assert.AreEqual(3, buffer[1].Snapshot.ActiveCount);
        }

        [Test]
        public void Recorder_Can_Skip_Zero_Activity_Samples()
        {
            var options = new AssetRuntimeTelemetryOptions(
                capacity: 4,
                includeZeroActivitySamples: false);
            var recorder = new AssetRuntimeTelemetryRecorder(options);

            Assert.IsFalse(recorder.TryRecord(CreateSnapshot(activeCount: 0, idleCount: 0, idleBytesApprox: 0L), timestampUtcTicks: 100L));
            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 1, idleCount: 0, idleBytesApprox: 0L), timestampUtcTicks: 110L));
            Assert.IsTrue(recorder.TryRecord(
                new AssetRuntimeCacheSnapshot(
                    "Default",
                    "Test",
                    activeCount: 0,
                    idleCount: 0,
                    idleBytesApprox: 0L,
                    idleBytesBudget: 1024L,
                    cacheMissCount: 1L),
                timestampUtcTicks: 120L));

            Assert.AreEqual(2, recorder.Count);
        }

        [Test]
        public async Task FileSink_Writes_Bounded_JsonLines_Atomically()
        {
            string directory = Path.Combine(Path.GetTempPath(), "CycloneGames.AssetManagement.Tests", Guid.NewGuid().ToString("N"));
            string filePath = Path.Combine(directory, "asset-runtime-telemetry.jsonl");

            try
            {
                var recorder = new AssetRuntimeTelemetryRecorder(new AssetRuntimeTelemetryOptions(capacity: 4));
                recorder.TryRecord(CreateSnapshot(activeCount: 1, idleCount: 2, idleBytesApprox: 32L, idleBytesBudget: 64L), timestampUtcTicks: 100L);
                recorder.TryRecord(CreateSnapshot(activeCount: 2, idleCount: 3, idleBytesApprox: 96L, idleBytesBudget: 64L), timestampUtcTicks: 200L);

                var sink = new AssetRuntimeTelemetryFileSink();
                var samples = new AssetRuntimeTelemetrySample[4];
                var builder = new StringBuilder(512);

                int written = await sink.WriteJsonLinesAsync(filePath, recorder, samples, builder);

                Assert.AreEqual(2, written);
                Assert.IsTrue(File.Exists(filePath));

                string text = File.ReadAllText(filePath, Encoding.UTF8);
                Assert.IsTrue(text.Contains("{\"schemaVersion\":1,\"sequence\":1"));
                Assert.AreEqual(2, File.ReadAllLines(filePath, Encoding.UTF8).Length);
                Assert.IsTrue(text.Contains("\"sequence\":1"));
                Assert.IsTrue(text.Contains("\"packageName\":\"Default\""));
                Assert.IsTrue(text.Contains("\"providerName\":\"Test\""));
                Assert.IsTrue(text.Contains("\"idleBudgetUsage\":1.5"));
                Assert.IsTrue(text.Contains("\"idleBudgetExceeded\":true"));
                Assert.IsTrue(text.Contains("\"cacheLookupCount\":0"));
                Assert.IsTrue(text.Contains("\"admissionRejectionCount\":0"));
                Assert.IsTrue(text.Contains("\"evictionCount\":0"));
                Assert.IsTrue(text.Contains("\"peakIdleBytesApprox\":0"));
            }
            finally
            {
                if (Directory.Exists(directory))
                {
                    Directory.Delete(directory, recursive: true);
                }
            }
        }

        [Test]
        public void DefaultPersistentPath_Rejects_Directory_Traversal_File_Names()
        {
            Assert.Throws<ArgumentException>(() => AssetRuntimeTelemetryPaths.GetDefaultPersistentJsonLinesPath("../bad.jsonl"));
            Assert.Throws<ArgumentException>(() => AssetRuntimeTelemetryPaths.GetDefaultPersistentJsonLinesPath("bad/path.jsonl"));
        }

        [Test]
        public void Telemetry_Rejects_Unbounded_Capacity()
        {
            Assert.Throws<ArgumentOutOfRangeException>(() =>
                new AssetRuntimeTelemetryOptions(AssetRuntimeTelemetryOptions.MAX_CAPACITY + 1));
        }

        [Test]
        public void Recorder_Uses_Monotonic_Time_When_Utc_Moves_Backward()
        {
            long monotonicTicks = 100L;
            var recorder = new AssetRuntimeTelemetryRecorder(
                new AssetRuntimeTelemetryOptions(
                    capacity: 4,
                    minimumSampleInterval: TimeSpan.FromTicks(10L)),
                () => monotonicTicks,
                TimeSpan.TicksPerSecond);

            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(1), timestampUtcTicks: 1_000L));
            monotonicTicks = 110L;
            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(2), timestampUtcTicks: 500L));
        }

        [Test]
        public void Recorder_Skips_Unchanged_Lifetime_Counters_After_Initial_Sample()
        {
            var recorder = new AssetRuntimeTelemetryRecorder(
                new AssetRuntimeTelemetryOptions(capacity: 4, includeZeroActivitySamples: false));
            AssetRuntimeCacheSnapshot snapshot = CreateSnapshotWithCounters(cacheMissCount: 7L);

            Assert.IsTrue(recorder.TryRecord(snapshot, timestampUtcTicks: 100L));
            Assert.IsFalse(recorder.TryRecord(snapshot, timestampUtcTicks: 110L));
            Assert.AreEqual(1, recorder.Count);
        }

        [Test]
        public void Recorder_Records_Gauge_Transition_Back_To_Zero_Once()
        {
            var recorder = new AssetRuntimeTelemetryRecorder(
                new AssetRuntimeTelemetryOptions(capacity: 4, includeZeroActivitySamples: false));

            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 1), timestampUtcTicks: 100L));
            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 0), timestampUtcTicks: 110L));
            Assert.IsFalse(recorder.TryRecord(CreateSnapshot(activeCount: 0), timestampUtcTicks: 120L));
            Assert.AreEqual(2, recorder.Count);
        }

        [Test]
        public void Recorder_Treats_Counter_Reset_Or_Wrap_As_Activity()
        {
            var recorder = new AssetRuntimeTelemetryRecorder(
                new AssetRuntimeTelemetryOptions(capacity: 4, includeZeroActivitySamples: false));

            Assert.IsTrue(recorder.TryRecord(
                CreateSnapshotWithCounters(cacheMissCount: long.MaxValue),
                timestampUtcTicks: 100L));
            Assert.IsTrue(recorder.TryRecord(
                CreateSnapshotWithCounters(cacheMissCount: long.MinValue),
                timestampUtcTicks: 110L));
            Assert.IsFalse(recorder.TryRecord(
                CreateSnapshotWithCounters(cacheMissCount: long.MinValue),
                timestampUtcTicks: 120L));
        }

        [Test]
        public void Recorder_Does_Not_Consume_Activity_While_Interval_Throttled()
        {
            long monotonicTicks = 100L;
            var recorder = new AssetRuntimeTelemetryRecorder(
                new AssetRuntimeTelemetryOptions(
                    capacity: 4,
                    minimumSampleInterval: TimeSpan.FromTicks(10L),
                    includeZeroActivitySamples: false),
                () => monotonicTicks,
                TimeSpan.TicksPerSecond);

            Assert.IsTrue(recorder.TryRecord(
                CreateSnapshotWithCounters(cacheMissCount: 1L),
                timestampUtcTicks: 100L));
            monotonicTicks = 105L;
            Assert.IsFalse(recorder.TryRecord(
                CreateSnapshotWithCounters(cacheMissCount: 2L),
                timestampUtcTicks: 105L));
            monotonicTicks = 110L;
            Assert.IsTrue(recorder.TryRecord(
                CreateSnapshotWithCounters(cacheMissCount: 2L),
                timestampUtcTicks: 110L));
        }

        [Test]
        public void JsonLine_Declares_Versioned_Additive_Schema()
        {
            var builder = new StringBuilder(512);
            var sample = new AssetRuntimeTelemetrySample(
                sequence: 5L,
                timestampUtcTicks: 100L,
                snapshot: CreateSnapshot(activeCount: 1));

            AssetRuntimeTelemetryFileSink.AppendJsonLine(builder, sample);
            string line = builder.ToString();

            Assert.AreEqual(1, AssetRuntimeTelemetryFileSink.JSON_LINES_SCHEMA_VERSION);
            StringAssert.StartsWith("{\"schemaVersion\":1,\"sequence\":5", line);
            StringAssert.Contains("\"packageName\":\"Default\"", line);
            StringAssert.Contains("\"providerName\":\"Test\"", line);
            StringAssert.Contains("\"activeCount\":1", line);
        }

        private static AssetRuntimeCacheSnapshot CreateSnapshot(
            int activeCount,
            int idleCount = 0,
            long idleBytesApprox = 0L,
            long idleBytesBudget = 1024L)
        {
            return new AssetRuntimeCacheSnapshot(
                "Default",
                "Test",
                activeCount,
                idleCount,
                idleBytesApprox,
                idleBytesBudget);
        }

        private static AssetRuntimeCacheSnapshot CreateSnapshotWithCounters(long cacheMissCount)
        {
            return new AssetRuntimeCacheSnapshot(
                "Default",
                "Test",
                activeCount: 0,
                idleCount: 0,
                idleBytesApprox: 0L,
                idleBytesBudget: 1024L,
                cacheMissCount: cacheMissCount);
        }
    }
}
