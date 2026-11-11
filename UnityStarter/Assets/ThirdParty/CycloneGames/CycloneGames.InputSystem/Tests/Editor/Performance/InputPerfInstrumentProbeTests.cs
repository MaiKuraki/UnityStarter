using System;
using System.Collections;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Compares every allocation instrument that could plausibly observe managed garbage on this host.
    /// The module's existing steady-state allocation test depends on
    /// <see cref="GC.GetAllocatedBytesForCurrentThread"/>, so the accuracy of that counter has direct
    /// consequences for whether the existing budget assertion can fail at all.
    /// </summary>
    public sealed class InputPerfInstrumentProbeTests
    {
        private const string ProbeFile = "probe-instruments.csv";
        private const string ProbeHeader = "runId,probe,variant,metric,value,unit";

        private static readonly object Retained;

        static InputPerfInstrumentProbeTests()
        {
            Retained = new byte[1024 * 1024];
        }

        [Test]
        public void Probe_ManagedHeapRespondsToUnreferencedGarbage()
        {
            // Warm the allocator so that heap growth from first-touch bookkeeping is not attributed to the probe.
            for (int i = 0; i < 4; i++) AllocateGarbageMegabytes(1);
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();

            for (int round = 0; round < 4; round++)
            {
                long before = GC.GetTotalMemory(false);
                AllocateGarbageMegabytes(2);
                long after = GC.GetTotalMemory(false);
                long delta = after - before;

                PerfOutput.Row(
                    ProbeFile,
                    ProbeHeader,
                    new[] { PerfOutput.RunId, "managed-heap", "garbage2MiB-round" + round, PerfOutput.Num(delta), "byte" });
                PerfOutput.Log("managed heap delta for 2 MiB garbage, round " + round + " = " + delta + " bytes");
            }

            Assert.Pass();
        }

        [Test]
        public void Probe_ManagedHeapRespondsToRetainedAllocation()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long before = GC.GetTotalMemory(true);

            object[] held = new object[8];
            for (int i = 0; i < held.Length; i++) held[i] = new byte[1024 * 1024];

            long after = GC.GetTotalMemory(true);
            long delta = after - before;
            GC.KeepAlive(held);
            GC.KeepAlive(Retained);

            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "managed-heap", "retained8MiB", PerfOutput.Num(delta), "byte" });
            PerfOutput.Log("managed live heap delta for 8 MiB retained = " + delta + " bytes");

            Assert.That(delta, Is.GreaterThan(4L * 1024L * 1024L), "GC.GetTotalMemory(true) did not observe retained allocation.");
        }

        [UnityTest]
        public IEnumerator Probe_FrameRecorderWithProfilerEnabled()
        {
            bool profilerWasEnabled = UnityEngine.Profiling.Profiler.enabled;
            UnityEngine.Profiling.Profiler.enabled = true;

            try
            {
                using (ProfilerRecorder recorder = ProfilerRecorder.StartNew(
                           ProfilerCategory.Memory,
                           "GC Allocated In Frame",
                           1024,
                           ProfilerRecorderOptions.Default))
                {
                    PerfOutput.Row(
                        ProbeFile,
                        ProbeHeader,
                        new[] { PerfOutput.RunId, "frame-recorder-enabled", "valid", recorder.Valid ? "1" : "0", "bool" });

                    for (int frame = 0; frame < 8; frame++) yield return null;
                    long baselineSum = SumSamples(recorder);
                    int baselineCount = recorder.Count;

                    for (int i = 0; i < 8; i++) AllocateGarbageMegabytes(1);
                    yield return null;
                    yield return null;
                    yield return null;

                    long afterSum = SumSamples(recorder);
                    int afterCount = recorder.Count;

                    PerfOutput.Row(
                        ProbeFile,
                        ProbeHeader,
                        new[]
                        {
                            PerfOutput.RunId, "frame-recorder-enabled", "sumDeltaFor8MiB",
                            PerfOutput.Num(afterSum - baselineSum), "byte"
                        });
                    PerfOutput.Row(
                        ProbeFile,
                        ProbeHeader,
                        new[] { PerfOutput.RunId, "frame-recorder-enabled", "sampleCount", PerfOutput.Num((long)afterCount), "count" });
                    PerfOutput.Row(
                        ProbeFile,
                        ProbeHeader,
                        new[] { PerfOutput.RunId, "frame-recorder-enabled", "baselineSampleCount", PerfOutput.Num((long)baselineCount), "count" });

                    PerfOutput.Log(
                        "frame recorder with profiler enabled: sumDelta=" + (afterSum - baselineSum) +
                        " samples=" + afterCount + " (baseline " + baselineCount + ")");
                }
            }
            finally
            {
                UnityEngine.Profiling.Profiler.enabled = profilerWasEnabled;
            }
        }

        /// <summary>
        /// End-to-end positive control for the allocation pipeline. A body that allocates a fixed 1 KiB array on
        /// every call must be reported as a non-zero per-iteration figure, otherwise a reported zero would be
        /// indistinguishable from a broken instrument.
        /// </summary>
        [Test]
        public void Probe_AllocationPipelineDetectsKnownPerIterationAllocation()
        {
            AllocationSample.Result small = AllocationSample.Measure(static () =>
            {
                var buffer = new byte[1024];
                buffer[0] = 1;
                GC.KeepAlive(buffer);
            }, 400);

            AllocationSample.Result large = AllocationSample.Measure(static () =>
            {
                var buffer = new byte[8192];
                buffer[0] = 1;
                GC.KeepAlive(buffer);
            }, 200);

            AllocationSample.Result zero = AllocationSample.Measure(static () =>
            {
                GC.KeepAlive(null);
            }, 2000);

            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "pipeline", "alloc1KiBPerCall", PerfOutput.Num(small.MedianBytesPerIteration, 1), "byte" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "pipeline", "alloc8KiBPerCall", PerfOutput.Num(large.MedianBytesPerIteration, 1), "byte" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "pipeline", "allocNone", PerfOutput.Num(zero.MedianBytesPerIteration, 3), "byte" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "pipeline", "instrument", small.Kind.ToString(), "kind" });

            PerfOutput.Log(
                "pipeline: 1KiB body=" + PerfOutput.Num(small.MedianBytesPerIteration, 1) +
                "B 8KiB body=" + PerfOutput.Num(large.MedianBytesPerIteration, 1) +
                "B empty body=" + PerfOutput.Num(zero.MedianBytesPerIteration, 3) + "B");

            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[]
                {
                    PerfOutput.RunId, "pipeline", "residualFactor1KiB",
                    PerfOutput.Num(small.MedianBytesPerIteration / 1024d, 4), "ratio"
                });

            // The heap-delta ruler quantizes to page granularity, so a 1 KiB step sits below its resolution and
            // may legitimately read zero. The meaningful positive control for that ruler is the 8 KiB body;
            // finer rulers must resolve the 1 KiB body as well. Exact byte figures come from the frame recorder.
            if (small.Kind != AllocationInstrument.Kind.HeapWindow)
            {
                Assert.That(
                    small.MedianBytesPerIteration,
                    Is.GreaterThan(0d),
                    "The allocation pipeline failed to detect a 1 KiB per-call allocation.");
            }

            Assert.That(
                large.MedianBytesPerIteration,
                Is.GreaterThan(0d),
                "The allocation pipeline failed to detect an 8 KiB per-call allocation.");
            Assert.That(
                large.MedianBytesPerIteration,
                Is.GreaterThan(small.MedianBytesPerIteration),
                "The allocation pipeline did not scale with allocation size.");
            Assert.That(
                zero.MedianBytesPerIteration,
                Is.LessThan(1d),
                "The allocation pipeline reported allocation for an empty body.");
        }

        private static long SumSamples(ProfilerRecorder recorder)
        {
            long sum = 0;
            int count = recorder.Count;
            for (int i = 0; i < count; i++) sum += recorder.GetSample(i).Value;
            return sum;
        }

        private static void AllocateGarbageMegabytes(int megabytes)
        {
            for (int block = 0; block < megabytes; block++)
            {
                var buffer = new byte[1024 * 1024];
                buffer[block] = 1;
                GC.KeepAlive(buffer);
            }
        }
    }
}
