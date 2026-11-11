using System;
using System.Collections.Generic;
using CycloneGames.InputSystem.Runtime;
using NUnit.Framework;

namespace CycloneGames.InputSystem.Tests.Editor
{
    /// <summary>
    /// Long-running stability measurement using frame-equivalent iterations.
    /// <para>
    /// Equivalent model: one iteration equals one rendered frame, so 108000 iterations equal 30 minutes at
    /// 60 frames per second. A batch-mode editor runs the pump far faster than real time, which compresses the
    /// wall clock while keeping the number of pump invocations, polling reads and hold evaluations identical to a
    /// real 30 minute session. The compression ratio actually achieved is reported with the results.
    /// </para>
    /// </summary>
    public sealed class InputPerfSoakTests
    {
        private const string File = "soak.csv";
        private const string Header = "runId,scenario,metric,value,unit,detail";

        private const int BlockFrames = 2_000;
        private const int DefaultTargetFrameCount = 108_000;

        /// <summary>
        /// Allows longer or shorter campaigns through <c>INPUTSYSTEM_PERF_SOAK_FRAMES</c> while keeping the
        /// thirty-minute equivalent as the default. Values below four blocks are ignored so that a block mean
        /// and a linear regression stay meaningful.
        /// </summary>
        private static readonly int TargetFrameCount = ReadTargetFrameCount();

        private static int ReadTargetFrameCount()
        {
            string configured = Environment.GetEnvironmentVariable("INPUTSYSTEM_PERF_SOAK_FRAMES");
            if (int.TryParse(configured, out int frames) && frames >= BlockFrames * 4) return frames;
            return DefaultTargetFrameCount;
        }

        [Test]
        public void Soak_FourPlayersThirtyMinuteEquivalent()
        {
            PerfOutput.WriteEnvironmentHeader(File);
            AllocationInstrument.Initialize();

            var scenario = new PerfScenario("Soak-P4", 4, 3, 64, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);

            double timestamp = 0d;
            for (int warm = 0; warm < 2_000; warm++)
            {
                timestamp += 1d / 60d;
                rig.PumpOnly(timestamp);
            }

            PerfMeter.ForceFullCollection();
            long heapStart = GC.GetTotalMemory(false);
            long liveStart = GC.GetTotalMemory(true);
            int collectionsStart = PerfMeter.TotalCollections();
            InputPlayerMemoryStats statsStart = rig.First.GetMemoryStats();

            var blockMeans = new List<double>(TargetFrameCount / BlockFrames);
            var blockHeap = new List<long>(TargetFrameCount / BlockFrames);
            long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();

            int blocks = TargetFrameCount / BlockFrames;
            for (int block = 0; block < blocks; block++)
            {
                long blockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int frame = 0; frame < BlockFrames; frame++)
                {
                    timestamp += 1d / 60d;
                    rig.PumpOnly(timestamp);
                }

                long blockElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - blockStart;
                blockMeans.Add(blockElapsed * PerfMeter.TicksToMicroseconds / BlockFrames);
                blockHeap.Add(GC.GetTotalMemory(false));
            }

            long wallElapsed = System.Diagnostics.Stopwatch.GetTimestamp() - wallStart;
            double wallMicroseconds = wallElapsed * PerfMeter.TicksToMicroseconds;
            long heapEnd = GC.GetTotalMemory(false);
            long liveEnd = GC.GetTotalMemory(true);
            int collectionsEnd = PerfMeter.TotalCollections();
            InputPlayerMemoryStats statsEnd = rig.First.GetMemoryStats();

            double firstMean = blockMeans[0];
            double lastMean = blockMeans[blockMeans.Count - 1];
            double driftPercent = (lastMean - firstMean) / Math.Max(1e-9, firstMean) * 100d;
            double heapSlope = SlopePerBlock(blockHeap);

            // The first block includes residual lazy initialization, so warm-start drift uses the second block.
            double warmFirst = blockMeans.Count > 2 ? blockMeans[1] : firstMean;
            double warmDriftPercent = (lastMean - warmFirst) / Math.Max(1e-9, warmFirst) * 100d;

            Write("frames", TargetFrameCount, "count", "blockSize=" + BlockFrames);
            Write("wallSeconds", wallMicroseconds / 1_000_000d, "s", "equivalentSeconds=" + TargetFrameCount / 60d);
            Write("compressionRatio", TargetFrameCount / 60d / Math.Max(1e-9, wallMicroseconds / 1_000_000d), "ratio", "equivalentOverWall");
            Write("blockP50", PerfMeter.Percentile(ToSorted(blockMeans), 0.50d), "us", "perFramePump");
            Write("blockP95", PerfMeter.Percentile(ToSorted(blockMeans), 0.95d), "us", "perFramePump");
            Write("blockFirst", firstMean, "us", "perFramePump");
            Write("blockLast", lastMean, "us", "perFramePump");
            Write("cpuDriftTotalPercent", driftPercent, "percent", "firstBlockToLastBlock");
            Write("cpuDriftWarmPercent", warmDriftPercent, "percent", "secondBlockToLastBlock");
            Write("heapStart", heapStart, "byte", "afterFullCollection");
            Write("heapEnd", heapEnd, "byte", "afterRun");
            Write("heapSlopePerBlock", heapSlope, "byte", "linearRegression");
            Write("heapSlopePerFrame", heapSlope / BlockFrames, "byte", "linearRegression");
            Write("liveStart", liveStart, "byte", "forcedCollection");
            Write("liveEnd", liveEnd, "byte", "forcedCollection");
            Write("liveDelta", liveEnd - liveStart, "byte", "forcedCollection");
            Write("collections", collectionsEnd - collectionsStart, "count", "gen0AndGen1AndGen2Summed");
            Write("actionCountStart", statsStart.ActionCount, "count", "perPlayer");
            Write("actionCountEnd", statsEnd.ActionCount, "count", "perPlayer");
            Write("subjectCountStart", statsStart.SubjectCount, "count", "perPlayer");
            Write("subjectCountEnd", statsEnd.SubjectCount, "count", "perPlayer");
            Write("contextStackStart", statsStart.ContextStackCount, "count", "perPlayer");
            Write("contextStackEnd", statsEnd.ContextStackCount, "count", "perPlayer");

            PerfOutput.Log(
                "soak frames=" + TargetFrameCount +
                " wall=" + PerfOutput.Num(wallMicroseconds / 1_000_000d, 2) + "s" +
                " compression=" + PerfOutput.Num(TargetFrameCount / 60d / Math.Max(1e-9, wallMicroseconds / 1_000_000d), 1) + "x" +
                " first=" + PerfOutput.Num(firstMean, 2) + "us" +
                " last=" + PerfOutput.Num(lastMean, 2) + "us" +
                " drift=" + PerfOutput.Num(driftPercent, 2) + "%" +
                " warmDrift=" + PerfOutput.Num(warmDriftPercent, 2) + "%" +
                " heapStart=" + heapStart + "B heapEnd=" + heapEnd + "B slopePerFrame=" + PerfOutput.Num(heapSlope / BlockFrames, 3) + "B" +
                " liveDelta=" + (liveEnd - liveStart) + "B" +
                " collections=" + (collectionsEnd - collectionsStart) +
                " subjects=" + statsStart.SubjectCount + "->" + statsEnd.SubjectCount);

            Assert.That(statsEnd.SubjectCount, Is.EqualTo(statsStart.SubjectCount), "Subject count must not grow during steady state.");
            Assert.That(statsEnd.ContextStackCount, Is.EqualTo(statsStart.ContextStackCount), "Context stack must not grow during steady state.");
            Assert.That(Math.Abs(driftPercent), Is.LessThan(50d), "Per-frame cost drifted by more than 50 percent across the run.");
        }

        /// <summary>
        /// Thirty-minute-equivalent soak of the highest-load configuration in the matrix, to confirm that cost
        /// scaling does not change the stability conclusion.
        /// </summary>
        [Test]
        public void Soak_EightPlayersThirtyMinuteEquivalent()
        {
            PerfOutput.WriteEnvironmentHeader(File);

            var scenario = new PerfScenario("Soak-P8", 8, 3, 64, 50, DeviceScenario.Gamepad);
            using PerfRig rig = PerfRig.Create(scenario);

            double timestamp = 0d;
            for (int warm = 0; warm < 1_000; warm++)
            {
                timestamp += 1d / 60d;
                rig.PumpOnly(timestamp);
            }

            PerfMeter.ForceFullCollection();
            long liveStart = GC.GetTotalMemory(true);
            int collectionsStart = PerfMeter.TotalCollections();

            const int Blocks = 12;
            var blockMeans = new List<double>(Blocks);
            long wallStart = System.Diagnostics.Stopwatch.GetTimestamp();
            for (int block = 0; block < Blocks; block++)
            {
                long blockStart = System.Diagnostics.Stopwatch.GetTimestamp();
                for (int frame = 0; frame < BlockFrames; frame++)
                {
                    timestamp += 1d / 60d;
                    rig.PumpOnly(timestamp);
                }

                blockMeans.Add((System.Diagnostics.Stopwatch.GetTimestamp() - blockStart) * PerfMeter.TicksToMicroseconds / BlockFrames);
            }

            double wallSeconds = (System.Diagnostics.Stopwatch.GetTimestamp() - wallStart) * PerfMeter.TicksToMicroseconds / 1_000_000d;
            long liveEnd = GC.GetTotalMemory(true);
            int collectionsEnd = PerfMeter.TotalCollections();

            Write("P8.frames", Blocks * BlockFrames, "count", "blockSize=" + BlockFrames);
            Write("P8.wallSeconds", wallSeconds, "s", "eightPlayers");
            Write("P8.blockP50", PerfMeter.Percentile(ToSorted(blockMeans), 0.50d), "us", "perFramePump");
            Write("P8.blockFirst", blockMeans[0], "us", "perFramePump");
            Write("P8.blockLast", blockMeans[blockMeans.Count - 1], "us", "perFramePump");
            Write("P8.liveDelta", liveEnd - liveStart, "byte", "forcedCollection");
            Write("P8.collections", collectionsEnd - collectionsStart, "count", "summed");

            PerfOutput.Log(
                "soak P8 frames=" + (Blocks * BlockFrames) +
                " wall=" + PerfOutput.Num(wallSeconds, 2) + "s" +
                " first=" + PerfOutput.Num(blockMeans[0], 2) + "us" +
                " last=" + PerfOutput.Num(blockMeans[blockMeans.Count - 1], 2) + "us" +
                " liveDelta=" + (liveEnd - liveStart) + "B" +
                " collections=" + (collectionsEnd - collectionsStart));

            Assert.That(liveEnd - liveStart, Is.LessThan(256 * 1024), "Live heap grew by more than 256 KiB across the long run.");
        }

        private static double[] ToSorted(List<double> values)
        {
            var copy = values.ToArray();
            Array.Sort(copy);
            return copy;
        }

        /// <summary>
        /// Least-squares slope of heap bytes against block index, which is independent of block-to-block noise.
        /// </summary>
        private static double SlopePerBlock(List<long> series)
        {
            int n = series.Count;
            if (n < 2) return 0d;
            double meanX = (n - 1) / 2d;
            double meanY = 0d;
            for (int i = 0; i < n; i++) meanY += series[i];
            meanY /= n;

            double numerator = 0d;
            double denominator = 0d;
            for (int i = 0; i < n; i++)
            {
                double dx = i - meanX;
                numerator += dx * (series[i] - meanY);
                denominator += dx * dx;
            }

            return denominator <= 0d ? 0d : numerator / denominator;
        }

        private static void Write(string metric, double value, string unit, string detail)
        {
            PerfOutput.Row(
                File,
                Header,
                new[] { PerfOutput.RunId, "soak", metric, PerfOutput.Num(value, 4), unit, detail });
        }
    }
}
