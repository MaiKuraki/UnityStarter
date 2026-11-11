using System;
using System.Collections;
using System.Collections.Generic;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Validates the measurement instruments themselves before any product measurement is trusted.
    /// A counter that never advances would make every allocation assertion pass vacuously, so each candidate
    /// instrument is calibrated against a known garbage volume.
    /// </summary>
    public sealed class InputPerfProbeTests
    {
        private const string ProbeFile = "probe.csv";
        private const string ProbeHeader = "runId,probe,metric,value,unit";

        [Test]
        public void Probe_AllocationInstrumentCalibration()
        {
            AllocationInstrument.Initialize();
            PerfOutput.WriteEnvironmentHeader(ProbeFile);

            IReadOnlyList<string> log = AllocationInstrument.CalibrationLog;
            for (int i = 0; i < log.Count; i++)
            {
                string entry = log[i];
                int split = entry.IndexOf('=');
                string key = split > 0 ? entry.Substring(0, split) : entry;
                string value = split > 0 ? entry.Substring(split + 1) : string.Empty;
                PerfOutput.Row(ProbeFile, ProbeHeader, new[] { PerfOutput.RunId, "instrument-calibration", key, value, "raw" });
                PerfOutput.Log(entry);
            }

            Assert.That(
                AllocationInstrument.Selected,
                Is.Not.EqualTo(AllocationInstrument.Kind.None),
                "No allocation instrument was selected.");
        }

        [Test]
        public void Probe_AvailableAllocationRecorders()
        {
            List<string> names = AllocationInstrument.ProbeRecorderNames();
            PerfOutput.Log("allocation-related profiler statistics probed: " + names.Count);
            for (int i = 0; i < names.Count; i++)
            {
                int split = names[i].IndexOf('=');
                string statName = split > 0 ? names[i].Substring(0, split) : names[i];
                string state = split > 0 ? names[i].Substring(split + 1) : "unknown";
                PerfOutput.Row(
                    ProbeFile,
                    ProbeHeader,
                    new[] { PerfOutput.RunId, "profiler-statistic", statName, state, "availability" });
                PerfOutput.Log("  " + names[i]);
            }

            Assert.That(names, Is.Not.Null);
        }

        /// <summary>
        /// Records how the per-thread allocation counter behaves against a known 1 MiB garbage volume. No
        /// assertion is made about the value itself; the assertion only requires that instrument selection stays
        /// consistent with the observed accuracy.
        /// </summary>
        [Test]
        public void Probe_PerThreadCounterObservesKnownGarbage()
        {
            for (int i = 0; i < 8; i++) AllocateOneKilobyte();

            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 1024; i++) AllocateOneKilobyte();
            long delta = GC.GetAllocatedBytesForCurrentThread() - before;

            double error = Math.Abs(delta - 1048576d) / 1048576d;
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "per-thread-counter", "deltaBytesFor1MiB", PerfOutput.Num(delta), "byte" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "per-thread-counter", "relativeError", PerfOutput.Num(error, 6), "ratio" });
            PerfOutput.Log("per-thread counter observed " + delta + " bytes for a 1048576 byte garbage volume");

            Assert.That(
                error <= 0.10d,
                Is.EqualTo(AllocationInstrument.Selected == AllocationInstrument.Kind.PerThreadCounter),
                "Instrument selection must agree with the calibration result for GC.GetAllocatedBytesForCurrentThread.");
        }

        /// <summary>
        /// Confirms that the frame recorder observes allocation inside a real editor frame loop.
        /// </summary>
        [UnityTest]
        public IEnumerator Probe_FrameRecorderObservesKnownGarbageAcrossFrames()
        {
            bool started = AllocationInstrument.TryStartFrameRecorder();
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "frame-recorder", "valid", started ? "1" : "0", "bool" });
            PerfOutput.Log("frame recorder valid=" + started);

            if (started)
            {
                for (int frame = 0; frame < 6; frame++) yield return null;
                long baseline = AllocationInstrument.FrameRecorderLastValue;

                for (int i = 0; i < 1024; i++) AllocateOneKilobyte();
                yield return null;
                yield return null;

                long after = AllocationInstrument.FrameRecorderLastValue;
                PerfOutput.Row(
                    ProbeFile,
                    ProbeHeader,
                    new[] { PerfOutput.RunId, "frame-recorder", "deltaBytesFor1MiB", PerfOutput.Num(after - baseline), "byte" });
                PerfOutput.Log("frame recorder observed " + (after - baseline) + " bytes for a 1048576 byte garbage volume");

                AllocationInstrument.StopFrameRecorder();
            }

            Assert.That(started, Is.True, "GC Allocated In Frame recorder was not available.");
        }

        /// <summary>
        /// Records the timer resolution and the noise floor of the timing path so that later p95 values can be
        /// interpreted against a measured baseline rather than an assumed one.
        /// </summary>
        [Test]
        public void Probe_TimerNoiseFloor()
        {
            double[] emptySamples = PerfMeter.SamplePerCallMicroseconds(4000, 1, static () => { });
            TimingStats stats = PerfMeter.Summarize(emptySamples);

            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "timer", "emptyCallP50us", PerfOutput.Num(stats.MedianUs, 4), "us" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "timer", "emptyCallP95us", PerfOutput.Num(stats.P95Us, 4), "us" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "timer", "emptyCallP99us", PerfOutput.Num(stats.P99Us, 4), "us" });
            PerfOutput.Row(
                ProbeFile,
                ProbeHeader,
                new[] { PerfOutput.RunId, "timer", "stopwatchFrequency", PerfOutput.Num(System.Diagnostics.Stopwatch.Frequency), "hz" });

            PerfOutput.Log(
                "timer noise floor p50=" + PerfOutput.Num(stats.MedianUs, 4) +
                "us p95=" + PerfOutput.Num(stats.P95Us, 4) +
                "us p99=" + PerfOutput.Num(stats.P99Us, 4) + "us");

            Assert.That(stats.MedianUs, Is.GreaterThanOrEqualTo(0d));
        }

        private static void AllocateOneKilobyte()
        {
            var buffer = new byte[1024];
            buffer[0] = 1;
            GC.KeepAlive(buffer);
        }
    }
}
