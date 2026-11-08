using System;
using System.Diagnostics;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.Perception;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.BehaviorTree.Integrations.Perception.Tests.Editor
{
    /// <summary>
    /// Per-call cost of the publish path, measured in batches because a per-call stopwatch would cost
    /// more than the work. Explicit: it runs for a fixed number of seconds.
    /// </summary>
    public sealed class BridgeBenchmarkTests
    {
        private const int VisibleKey = 61;
        private const int DistanceKey = 62;
        private const int PositionKey = 63;
        private const int CountKey = 64;
        private const int HandleKey = 65;
        private const int FromMemoryKey = 66;

        [Test]
        [Explicit("Ten-second benchmark of the legacy publish path.")]
        public void Benchmark_LegacyPublishPath()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 8);
            using var blackboard = new RuntimeBlackboard();

            var results = new DetectionResult[8];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = Entry(i + 1, 1f + i);
            }

            sensor.SetResults(results);

            Measure("legacy", () => Step(bridge, blackboard), TimeSpan.FromSeconds(10));
        }

        [Test]
        [Explicit("Ten-second benchmark of the identity and memory publish path.")]
        public void Benchmark_IdentityAndMemoryPublishPath()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 8);
            using var blackboard = new RuntimeBlackboard();

            var results = new DetectionResult[8];
            for (int i = 0; i < results.Length; i++)
            {
                results[i] = Entry(i + 1, 1f + i);
            }

            sensor.SetResults(results);

            Measure("identity", () => StepWithIdentity(bridge, blackboard), TimeSpan.FromSeconds(10));
        }

        [Test]
        [Explicit("Ten-second benchmark of the aggregator over two bridges.")]
        public void Benchmark_AggregatorPath()
        {
            using var sight = new FakeSensor();
            using var hearing = new FakeSensor();
            using var sightBridge = new PerceptionBlackboardBridge(sight, capacity: 8);
            using var hearingBridge = new PerceptionBlackboardBridge(hearing, capacity: 8);
            using var aggregator = new PerceptionAggregator(capacity: 8);
            using var blackboard = new RuntimeBlackboard();
            aggregator.Add(sightBridge);
            aggregator.Add(hearingBridge);

            sight.SetResults(Entry(1, 1f), Entry(2, 2f), Entry(3, 3f), Entry(4, 4f));
            hearing.SetResults(Entry(3, 3.5f), Entry(5, 5f), Entry(6, 6f), Entry(7, 7f));

            Measure("aggregator", () => StepAggregator(aggregator, blackboard), TimeSpan.FromSeconds(10));
        }

        [Test]
        [Explicit("Sixty-second soak of the aggregator with heap sampling.")]
        public void Soak_AggregatorSixtySeconds()
        {
            using var sight = new FakeSensor();
            using var hearing = new FakeSensor();
            using var sightBridge = new PerceptionBlackboardBridge(sight, capacity: 8);
            using var hearingBridge = new PerceptionBlackboardBridge(hearing, capacity: 8);
            using var aggregator = new PerceptionAggregator(capacity: 8);
            using var blackboard = new RuntimeBlackboard();
            aggregator.Add(sightBridge);
            aggregator.Add(hearingBridge);

            sight.SetResults(Entry(1, 1f), Entry(2, 2f), Entry(3, 3f), Entry(4, 4f));
            hearing.SetResults(Entry(3, 3.5f), Entry(5, 5f), Entry(6, 6f), Entry(7, 7f));

            const int Batch = 512;
            var batchMicroseconds = new double[1 << 18];
            int batchCount = 0;
            long iterations = 0;
            long countMismatches = 0;
            double worst = 0d;

            long heapStart = GC.GetTotalMemory(true);
            long heapBaseline = heapStart;
            double worstHeapDelta = 0d;

            var total = Stopwatch.StartNew();
            var watch = new Stopwatch();
            var heapWatch = Stopwatch.StartNew();

            while (total.Elapsed < TimeSpan.FromSeconds(60) && batchCount < batchMicroseconds.Length)
            {
                watch.Restart();
                for (int i = 0; i < Batch; i++)
                {
                    StepAggregator(aggregator, blackboard);
                }

                watch.Stop();
                iterations += Batch;

                double perCall = watch.Elapsed.TotalMilliseconds * 1000d / Batch;
                batchMicroseconds[batchCount++] = perCall;
                if (perCall > worst)
                {
                    worst = perCall;
                }

                if (aggregator.Count != 7)
                {
                    countMismatches++;
                }

                if (heapWatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    heapWatch.Restart();
                    double delta = GC.GetTotalMemory(false) - heapBaseline;
                    if (delta > worstHeapDelta)
                    {
                        worstHeapDelta = delta;
                    }
                }
            }

            total.Stop();

            var sorted = new double[batchCount];
            Array.Copy(batchMicroseconds, sorted, batchCount);
            Array.Sort(sorted);

            long heapEnd = GC.GetTotalMemory(true);
            TestContext.WriteLine(
                $"aggregator: iterations={iterations} p50={Percentile(sorted, 0.50):F4} " +
                $"p99={Percentile(sorted, 0.99):F4} max={worst:F4} us/call heapDelta={heapEnd - heapStart} " +
                $"worstSampleDelta={worstHeapDelta:F0}");

            Assert.Greater(iterations, 1_000_000L, "the soak did not run long enough to be evidence");
            Assert.AreEqual(0L, countMismatches, "merged count drifted during the soak");
            Assert.Less(heapEnd - heapStart, 2 * 1024 * 1024, "aggregator retained heap across the soak");
        }

        private static void Measure(string label, Action step, TimeSpan duration)
        {
            const int Batch = 512;
            var batchMicroseconds = new double[1 << 18];
            int batchCount = 0;
            long iterations = 0;
            double worst = 0d;

            for (int i = 0; i < 2000; i++)
            {
                step();
            }

            long heapStart = GC.GetTotalMemory(true);
            var total = Stopwatch.StartNew();
            var watch = new Stopwatch();

            while (total.Elapsed < duration && batchCount < batchMicroseconds.Length)
            {
                watch.Restart();
                for (int i = 0; i < Batch; i++)
                {
                    step();
                }

                watch.Stop();
                iterations += Batch;

                double perCall = watch.Elapsed.TotalMilliseconds * 1000d / Batch;
                batchMicroseconds[batchCount++] = perCall;
                if (perCall > worst)
                {
                    worst = perCall;
                }
            }

            total.Stop();
            long heapEnd = GC.GetTotalMemory(true);

            var sorted = new double[batchCount];
            Array.Copy(batchMicroseconds, sorted, batchCount);
            Array.Sort(sorted);

            TestContext.WriteLine(
                $"{label}: iterations={iterations} p50={Percentile(sorted, 0.50):F4} p99={Percentile(sorted, 0.99):F4} " +
                $"max={worst:F4} us/call heapDelta={heapEnd - heapStart}");
        }

        private static double Percentile(double[] sorted, double q)
        {
            int index = (int)Math.Min(sorted.Length - 1, Math.Max(0, Math.Round(q * (sorted.Length - 1))));
            return sorted[index];
        }

        private static void Step(PerceptionBlackboardBridge bridge, RuntimeBlackboard blackboard)
        {
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
            bridge.PublishNearestPosition(blackboard, PositionKey);
            bridge.PublishCount(blackboard, CountKey);
        }

        private static void StepWithIdentity(PerceptionBlackboardBridge bridge, RuntimeBlackboard blackboard)
        {
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
            bridge.PublishNearestPosition(blackboard, PositionKey);
            bridge.PublishCount(blackboard, CountKey);
            bridge.PublishNearestHandle(blackboard, HandleKey);
            bridge.PublishNearestFromMemory(blackboard, FromMemoryKey);
        }

        private static void StepAggregator(PerceptionAggregator aggregator, RuntimeBlackboard blackboard)
        {
            aggregator.Update();
            aggregator.PublishNearest(blackboard, VisibleKey, DistanceKey);
            aggregator.PublishNearestPosition(blackboard, PositionKey);
            aggregator.PublishNearestHandle(blackboard, HandleKey);
            aggregator.PublishNearestFromMemory(blackboard, FromMemoryKey);
            aggregator.PublishCount(blackboard, CountKey);
        }

        private static DetectionResult Entry(int id, float distance)
        {
            return new DetectionResult
            {
                Target = new PerceptibleHandle(1, id, 1),
                Distance = distance,
                LastKnownPosition = new float3(id, 0f, 0f),
                DetectionTime = 0d,
                Visibility = 1f,
                SensorType = SensorType.Sight,
                IsFromMemory = false
            };
        }

        private sealed class FakeSensor : ISensor
        {
            private DetectionResult[] _results = Array.Empty<DetectionResult>();

            public void SetResults(params DetectionResult[] results)
            {
                _results = results ?? Array.Empty<DetectionResult>();
            }

            public int SensorId => 1;
            public SensorType Type => SensorType.Sight;
            public bool IsEnabled { get; set; } = true;
            public float UpdateInterval => 0f;
            public double LastUpdateTime => 0d;
            public float3 Position => float3.zero;
            public SensorUpdateStatus LastUpdateStatus => SensorUpdateStatus.Ready;
            public bool HasDetection => _results.Length > 0;
            public int DetectedCount => _results.Length;

            public void Initialize() { }
            public void UpdateSensor(float deltaTime) { }
            public void ProcessJobResults() { }

            public bool TryGetResult(int index, out DetectionResult result)
            {
                if (index < 0 || index >= _results.Length)
                {
                    result = default;
                    return false;
                }

                result = _results[index];
                return true;
            }

            public void GetDetectionResults(ref NativeList<DetectionResult> results) { }
            public void GetDetectedHandles(ref NativeList<PerceptibleHandle> results) { }
            public void Dispose() { }
        }
    }
}
