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
            var recorder = new AssetRuntimeTelemetryRecorder(options);

            Assert.IsTrue(recorder.TryRecord(CreateSnapshot(activeCount: 1), timestampUtcTicks: 100L));
            Assert.IsFalse(recorder.TryRecord(CreateSnapshot(activeCount: 2), timestampUtcTicks: 105L));
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

            Assert.AreEqual(1, recorder.Count);
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
                Assert.IsTrue(text.Contains("\"sequence\":1"));
                Assert.IsTrue(text.Contains("\"packageName\":\"Default\""));
                Assert.IsTrue(text.Contains("\"providerName\":\"Test\""));
                Assert.IsTrue(text.Contains("\"idleBudgetUsage\":1.5"));
                Assert.IsTrue(text.Contains("\"idleBudgetExceeded\":true"));
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
    }
}
