using System;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.Spatial;
using CycloneGames.Spatial.Core;
using NUnit.Framework;
using Unity.Profiling;
using UnityEngine;

namespace CycloneGames.BehaviorTree.Integrations.Spatial.Tests.Editor
{
    /// <summary>
    /// Multi-minute soak of the adapter and the managed tree inside the Editor. Marked explicit so it
    /// runs on demand rather than in the default gate; every threshold is loose enough to survive Mono
    /// and Editor background work, and only catches order-of-magnitude regressions.
    /// </summary>
    public sealed class SpatialAdapterSoakTests
    {
        private const int VisibleKey = 2001;
        private const int DistanceKey = 2002;

        [Test]
        [Explicit("Two-and-a-half minute soak; run on demand. The runner caps a single test at 180 s.")]
        public void Soak_ShallowTree()
        {
            RunSoak("shallow-256x8", agents: 256, depth: 8, duration: TimeSpan.FromSeconds(150));
        }

        [Test]
        [Explicit("Two-and-a-half minute soak; run on demand. The runner caps a single test at 180 s.")]
        public void Soak_DeepTree()
        {
            RunSoak("deep-128x128", agents: 128, depth: 128, duration: TimeSpan.FromSeconds(150));
        }

        /// <summary>
        /// Heap-retention gate for the adapter loop. The managed allocation counters are not
        /// trustworthy in this Editor host, so the assertion uses live-heap growth, and the
        /// ProfilerRecorder control at the end reports whether its metric is live at all.
        /// </summary>
        [Test]
        [Explicit("Five-second heap-retention gate.")]
        public void AllocationGate_AdapterLoopRetainsNoHeap()
        {
            const int Agents = 32;
            const int Depth = 8;

            using var grid = new SpatialHashGrid<int>(8f, Agents);
            var positions = new Vector3[Agents];
            var blackboards = new RuntimeBlackboard[Agents];
            var trees = new RuntimeBehaviorTree[Agents];
            var adapters = new SpatialBlackboardQuery<int>[Agents];
            var random = new System.Random(31337);

            for (int i = 0; i < Agents; i++)
            {
                positions[i] = new Vector3((float)random.NextDouble() * 20f, 0f, (float)random.NextDouble() * 20f);
                Assert.IsTrue(grid.TryInsert(i, positions[i].x, 0f, positions[i].z, SpatialLayers.Default, out _));
            }

            try
            {
                for (int i = 0; i < Agents; i++)
                {
                    blackboards[i] = new RuntimeBlackboard();
                    trees[i] = BuildTree(blackboards[i], Depth);
                    adapters[i] = new SpatialBlackboardQuery<int>(grid, 4);
                }

                var warmup = Stopwatch.StartNew();
                int frame = 0;
                while (warmup.Elapsed < TimeSpan.FromSeconds(2))
                {
                    Step(positions, adapters, blackboards, trees, frame++, grid);
                }

                long heapBefore = GC.GetTotalMemory(true);

                var measured = Stopwatch.StartNew();
                long frames = 0;
                while (measured.Elapsed < TimeSpan.FromSeconds(5))
                {
                    Step(positions, adapters, blackboards, trees, frame++, grid);
                    frames++;
                }

                long heapAfter = GC.GetTotalMemory(true);
                long retained = heapAfter - heapBefore;

                // Control: report whether the Profiler metric observes an obvious allocation.
                long controlBytes;
                using (var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame"))
                {
                    var garbage = new byte[16 * 1024 * 1024];
                    GC.KeepAlive(garbage);
                    controlBytes = gcRecorder.LastValue;
                }

                TestContext.WriteLine(
                    $"frames={frames} retainedBytes={retained} profilerControlBytes={controlBytes} " +
                    $"profilerMetricLive={controlBytes > 0}");

                Assert.Less(retained, 2L * 1024 * 1024, "adapter loop retained more than 2 MiB of live heap");
            }
            finally
            {
                for (int i = 0; i < Agents; i++)
                {
                    trees[i]?.Dispose();
                    blackboards[i]?.Dispose();
                    adapters[i]?.Dispose();
                }
            }
        }

        private static void RunSoak(string name, int agents, int depth, TimeSpan duration)
        {
            using var grid = new SpatialHashGrid<int>(8f, agents);
            var blackboards = new RuntimeBlackboard[agents];
            var trees = new RuntimeBehaviorTree[agents];
            var adapters = new SpatialBlackboardQuery<int>[agents];
            var positions = new Vector3[agents];
            var random = new System.Random(7777);
            float side = Mathf.Sqrt(agents) * 4f;

            for (int i = 0; i < agents; i++)
            {
                var position = new Vector3(
                    (float)random.NextDouble() * side,
                    0f,
                    (float)random.NextDouble() * side);
                positions[i] = position;
                Assert.IsTrue(grid.TryInsert(i, position.x, position.y, position.z, SpatialLayers.Default, out _));
            }

            try
            {
                for (int i = 0; i < agents; i++)
                {
                    blackboards[i] = new RuntimeBlackboard();
                    trees[i] = BuildTree(blackboards[i], depth);
                    adapters[i] = new SpatialBlackboardQuery<int>(grid, 4);
                }

                using var gcRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "GC Allocated In Frame");
                using var memoryRecorder = ProfilerRecorder.StartNew(ProfilerCategory.Memory, "Total Used Memory");

                var warmup = Stopwatch.StartNew();
                int warmupFrame = 0;
                while (warmup.Elapsed < TimeSpan.FromSeconds(3))
                {
                    Step(positions, adapters, blackboards, trees, warmupFrame++, grid);
                }

                var frames = new double[1 << 21];
                int frameCount = 0;
                long allocatedBytes = 0;
                long framesWithGc = 0;
                long heapMin = long.MaxValue;
                long heapMax = long.MinValue;
                var sampleWatch = Stopwatch.StartNew();
                var frameWatch = new Stopwatch();
                var totalWatch = Stopwatch.StartNew();

                while (totalWatch.Elapsed < duration)
                {
                    frameWatch.Restart();
                    Step(positions, adapters, blackboards, trees, frameCount, grid);
                    frameWatch.Stop();

                    frames[frameCount++] = frameWatch.Elapsed.TotalMilliseconds;

                    long gcBytes = gcRecorder.LastValue;
                    if (gcBytes > 0)
                    {
                        allocatedBytes += gcBytes;
                        framesWithGc++;
                    }

                    if (sampleWatch.Elapsed > TimeSpan.FromSeconds(15))
                    {
                        sampleWatch.Restart();
                        long heap = memoryRecorder.LastValue;
                        heapMin = Math.Min(heapMin, heap);
                        heapMax = Math.Max(heapMax, heap);
                    }
                }

                totalWatch.Stop();

                var sorted = new double[frameCount];
                Array.Copy(frames, sorted, frameCount);
                Array.Sort(sorted);

                double p50 = Percentile(sorted, 0.50);
                double p95 = Percentile(sorted, 0.95);
                double p99 = Percentile(sorted, 0.99);
                double max = sorted[frameCount - 1];
                double bytesPerFrame = allocatedBytes / (double)Math.Max(1, frameCount);
                long heapSpread = heapMax == long.MinValue ? 0 : heapMax - heapMin;

                AssertNearestMatchesBruteForce(grid, adapters, positions, agents);

                WriteReport(name, agents, depth, totalWatch.Elapsed.TotalSeconds, frameCount, p50, p95, p99, max,
                    bytesPerFrame, framesWithGc, heapMin, heapMax, heapSpread);

                Assert.Less(p99, 12.0, "p99 frame time regressed: " + p99.ToString("F3", CultureInfo.InvariantCulture));
                Assert.Less(bytesPerFrame, 128.0, "per-frame allocation regressed: " + bytesPerFrame.ToString("F2", CultureInfo.InvariantCulture));
                Assert.Less(heapSpread, 16L * 1024 * 1024, "heap drifted: " + heapSpread);
            }
            finally
            {
                for (int i = 0; i < agents; i++)
                {
                    trees[i]?.Dispose();
                    blackboards[i]?.Dispose();
                    adapters[i]?.Dispose();
                }
            }
        }

        private static void Step(
            Vector3[] positions,
            SpatialBlackboardQuery<int>[] adapters,
            RuntimeBlackboard[] blackboards,
            RuntimeBehaviorTree[] trees,
            int frameIndex,
            SpatialHashGrid<int> grid)
        {
            float phase = frameIndex * 0.05f;

            for (int i = 0; i < trees.Length; i++)
            {
                // The index stays static; only the query origin moves, so a brute-force comparison
                // against the same positions stays exact.
                Vector3 origin = Origin(positions[i], phase, i);
                adapters[i].QueryNearest(origin.x, origin.y, origin.z, 12f, SpatialLayers.All);
                adapters[i].PublishNearest(blackboards[i], VisibleKey, DistanceKey);
                trees[i].Tick();
            }
        }

        private static void AssertNearestMatchesBruteForce(
            SpatialHashGrid<int> grid,
            SpatialBlackboardQuery<int>[] adapters,
            Vector3[] positions,
            int agents)
        {
            for (int i = 0; i < agents; i += Math.Max(1, agents / 32))
            {
                Vector3 origin = Origin(positions[i], 0f, i);
                adapters[i].QueryNearest(origin.x, origin.y, origin.z, 12f, SpatialLayers.All);

                float best = float.MaxValue;
                for (int j = 0; j < agents; j++)
                {
                    Vector3 other = positions[j];
                    float dx = other.x - origin.x;
                    float dz = other.z - origin.z;
                    float squared = dx * dx + dz * dz;
                    if (squared < best)
                    {
                        best = squared;
                    }
                }

                if (adapters[i].Count > 0)
                {
                    Assert.AreEqual(best, adapters[i].NearestSquaredDistance, 1e-2f, "nearest mismatch for agent " + i);
                }
                else
                {
                    Assert.Greater(best, 144f, "agent " + i + " reported no neighbour but brute force found one in range");
                }
            }
        }

        private static Vector3 Origin(Vector3 position, float phase, int index)
        {
            return new Vector3(
                position.x + Mathf.Sin(phase + index) * 0.5f,
                position.y,
                position.z + Mathf.Cos(phase + index) * 0.5f);
        }

        private static RuntimeBehaviorTree BuildTree(RuntimeBlackboard blackboard, int depth)
        {
            var builder = new RuntimeBehaviorTreeBuilder(blackboard);
            builder.Selector();

            builder.Sequence();
            for (int i = 0; i < depth; i++)
            {
                builder.Sequence();
            }

            builder.Condition(bb => bb.GetBool(VisibleKey));
            builder.Action(bb => bb.GetFloat(DistanceKey) < 144f ? RuntimeState.Success : RuntimeState.Failure);

            for (int i = 0; i < depth; i++)
            {
                builder.End();
            }

            builder.End();
            builder.Action(_ => RuntimeState.Success);
            builder.End();

            return builder.Build();
        }

        private static double Percentile(double[] sorted, double q)
        {
            double index = q * (sorted.Length - 1);
            int low = (int)Math.Floor(index);
            int high = (int)Math.Ceiling(index);
            if (low == high)
            {
                return sorted[low];
            }

            double weight = index - low;
            return sorted[low] * (1 - weight) + sorted[high] * weight;
        }

        private static void WriteReport(
            string name,
            int agents,
            int depth,
            double seconds,
            int frames,
            double p50,
            double p95,
            double p99,
            double max,
            double bytesPerFrame,
            long framesWithGc,
            long heapMin,
            long heapMax,
            long heapSpread)
        {
            string projectRoot = Path.GetDirectoryName(Application.dataPath);
            string directory = Path.GetFullPath(Path.Combine(projectRoot, "..", "Temp~", "test-results"));
            Directory.CreateDirectory(directory);

            var builder = new StringBuilder();
            builder.Append("{\n");
            builder.Append("  \"name\": \"").Append(name).Append("\",\n");
            builder.Append("  \"agents\": ").Append(agents).Append(",\n");
            builder.Append("  \"depth\": ").Append(depth).Append(",\n");
            builder.Append("  \"seconds\": ").Append(seconds.ToString("F1", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"frames\": ").Append(frames).Append(",\n");
            builder.Append("  \"p50ms\": ").Append(p50.ToString("F4", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"p95ms\": ").Append(p95.ToString("F4", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"p99ms\": ").Append(p99.ToString("F4", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"maxms\": ").Append(max.ToString("F4", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"bytesPerFrame\": ").Append(bytesPerFrame.ToString("F2", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"framesWithGc\": ").Append(framesWithGc).Append(",\n");
            builder.Append("  \"heapMinMiB\": ").Append((heapMin / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"heapMaxMiB\": ").Append((heapMax / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)).Append(",\n");
            builder.Append("  \"heapSpreadMiB\": ").Append((heapSpread / 1048576.0).ToString("F2", CultureInfo.InvariantCulture)).Append("\n");
            builder.Append("}\n");

            File.WriteAllText(Path.Combine(directory, "spatial-soak-" + name + ".json"), builder.ToString());
        }
    }
}
