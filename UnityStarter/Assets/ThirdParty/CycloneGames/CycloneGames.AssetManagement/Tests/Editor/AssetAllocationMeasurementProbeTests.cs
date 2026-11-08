using System;

using NUnit.Framework;

using Unity.Profiling;

using UnityEngine;

namespace CycloneGames.AssetManagement.Tests.Editor
{
    public sealed class AssetAllocationMeasurementProbeTests
    {
        private const int KnownAllocationBytes = 16 * 1024 * 1024;
        private const int NoiseSampleCount = 8;

        [Test]
        public void AllocationChannelProbe_ReportsLiveCountersAndHeapNoiseFloor()
        {
            // Unity.PerformanceTesting's .GC() sample is sourced from GC.GetAllocatedBytesForCurrentThread().
            // Its samples are written to TestResults.xml and cannot be read back at runtime, so this probe
            // determines .GC() liveness by observing that underlying counter against a known allocation.
            // The counter is observed only for channel liveness; it is never used as allocation evidence here.
            long emptyGcChannelDelta = MeasureGcChannelDelta(EmptyMethod);
            long controlGcChannelDelta = MeasureGcChannelDelta(AllocateKnownBytes);

            long heapNoiseFloorBytes = MeasureHeapNoiseFloor();
            long controlHeapGrowthBytes = MeasureHeapGrowth();

            long profilerControlBytes = MeasureProfilerControl();
            bool profilerMetricLive = profilerControlBytes > 0L;
            bool gcAllocatedBytesChannelLive = controlGcChannelDelta > 0L;

            string conclusion = gcAllocatedBytesChannelLive
                ? "GC.GetAllocatedBytesForCurrentThread is live; Unity.PerformanceTesting .GC() can report " +
                  "non-zero allocations on this host."
                : "GC.GetAllocatedBytesForCurrentThread returns 0 even for a known 16 MiB allocation; " +
                  "Unity.PerformanceTesting .GC() is not a usable allocation channel on this Editor Mono " +
                  "host. Use ProfilerRecorder (Memory, \"GC Allocated In Frame\") or the " +
                  "GC.GetTotalMemory heap-trend channel instead.";

            string report =
                $"[AllocationChannelProbe] knownAllocationBytes={KnownAllocationBytes} " +
                $"emptyGcChannelDelta={emptyGcChannelDelta} controlGcChannelDelta={controlGcChannelDelta} " +
                $"gcAllocatedBytesChannelLive={gcAllocatedBytesChannelLive} " +
                $"heapNoiseFloorBytes={heapNoiseFloorBytes} controlHeapGrowthBytes={controlHeapGrowthBytes} " +
                $"profilerControlBytes={profilerControlBytes} profilerMetricLive={profilerMetricLive} " +
                $"conclusion={conclusion}";
            TestContext.WriteLine(report);
            Debug.Log(report);

            // The heap-trend channel is the allocation evidence. It must observe the known live allocation.
            Assert.That(
                controlHeapGrowthBytes,
                Is.GreaterThanOrEqualTo(KnownAllocationBytes / 2),
                $"The heap-trend channel did not observe the known {KnownAllocationBytes}-byte allocation " +
                $"(observed growth {controlHeapGrowthBytes} bytes). The allocation measurement fallback is unusable.");

            // An empty method is reported as <= heap noise floor, never as absolute zero allocation.
            TestContext.WriteLine(
                $"[AllocationChannelProbe] emptyMethod observed <= {heapNoiseFloorBytes} bytes of heap noise; " +
                "a zero-allocation conclusion on this host is expressed as <= noise floor, not as absolute zero.");
        }

        private static void EmptyMethod()
        {
        }

        private static void AllocateKnownBytes()
        {
            byte[] garbage = new byte[KnownAllocationBytes];
            GC.KeepAlive(garbage);
        }

        private static long MeasureGcChannelDelta(Action action)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            action();
            long after = GC.GetAllocatedBytesForCurrentThread();
            return after - before;
        }

        private static long MeasureHeapNoiseFloor()
        {
            long peak = 0L;
            for (int i = 0; i < NoiseSampleCount; i++)
            {
                long before = GC.GetTotalMemory(false);
                EmptyMethod();
                long after = GC.GetTotalMemory(false);
                long growth = Math.Max(0L, after - before);
                if (growth > peak)
                {
                    peak = growth;
                }
            }

            return peak;
        }

        private static long MeasureHeapGrowth()
        {
            GC.Collect();
            GC.WaitForPendingFinalizers();
            GC.Collect();
            long baseline = GC.GetTotalMemory(true);
            // Hold a live reference across the measurement so a forced collection cannot reclaim it.
            byte[] held = new byte[KnownAllocationBytes];
            long peak = GC.GetTotalMemory(true);
            GC.KeepAlive(held);
            return Math.Max(0L, peak - baseline);
        }

        private static long MeasureProfilerControl()
        {
            using (var recorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
            {
                AllocateKnownBytes();
                return recorder.LastValue;
            }
        }
    }
}
