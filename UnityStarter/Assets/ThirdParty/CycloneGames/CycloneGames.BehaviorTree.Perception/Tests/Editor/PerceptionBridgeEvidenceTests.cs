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
    /// Evidence for the adapter itself: allocation behaviour across a long run and the cost of one
    /// update plus publish. Explicit because the soak needs a longer window than the default gate.
    /// </summary>
    public sealed class PerceptionBridgeEvidenceTests
    {
        private const int VisibleKey = 21;
        private const int DistanceKey = 22;
        private const int PositionKey = 23;
        private const int CountKey = 24;

        [Test]
        public void UpdateAndPublish_DoNotRetainHeap()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 8);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(1, 3f), Entry(2, 6f), Entry(3, 9f));

            for (int i = 0; i < 5000; i++)
            {
                Step(bridge, blackboard);
            }

            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 300000; i++)
            {
                Step(bridge, blackboard);
            }

            long after = GC.GetTotalMemory(true);
            Assert.Less(after - before, 1024 * 1024, "steady-state update and publish must not retain heap");
        }

        [Test]
        [Explicit("Sixty-second soak for the adapter; run on demand.")]
        public void Soak_SixtySeconds_HoldsCountAndHeap()
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

            // Time in batches: a per-call Stopwatch would dominate the cost it measures.
            const int Batch = 1024;
            var batchMicroseconds = new double[1 << 20];
            int batchCount = 0;
            long iterations = 0;
            long countMismatches = 0;
            double worstMicroseconds = 0d;

            long heapStart = GC.GetTotalMemory(true);
            long heapBaseline = heapStart;
            double worstHeapDelta = 0d;

            var total = Stopwatch.StartNew();
            var batchWatch = new Stopwatch();
            var heapWatch = Stopwatch.StartNew();

            while (total.Elapsed < TimeSpan.FromSeconds(60) && batchCount < batchMicroseconds.Length)
            {
                batchWatch.Restart();
                for (int i = 0; i < Batch; i++)
                {
                    Step(bridge, blackboard);
                }

                batchWatch.Stop();
                iterations += Batch;

                double perCallMicroseconds = batchWatch.Elapsed.TotalMilliseconds * 1000d / Batch;
                batchMicroseconds[batchCount++] = perCallMicroseconds;
                if (perCallMicroseconds > worstMicroseconds)
                {
                    worstMicroseconds = perCallMicroseconds;
                }

                if (bridge.Count != results.Length)
                {
                    countMismatches++;
                }

                if (heapWatch.Elapsed > TimeSpan.FromSeconds(5))
                {
                    heapWatch.Restart();
                    long now = GC.GetTotalMemory(false);
                    double delta = now - heapBaseline;
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
                $"iterations={iterations} batches={batchCount} p50={Percentile(sorted, 0.50):F4} " +
                $"p99={Percentile(sorted, 0.99):F4} max={worstMicroseconds:F4} us/call " +
                $"heapDelta={heapEnd - heapStart} worstSampleDelta={worstHeapDelta:F0}");

            Assert.Greater(iterations, 1_000_000L, "the soak did not run long enough to be evidence");
            Assert.AreEqual(0L, countMismatches, "target count drifted during the soak");
            Assert.Less(heapEnd - heapStart, 2 * 1024 * 1024, "adapter retained heap across the soak");
            Assert.Less(worstHeapDelta, 4d * 1024 * 1024, "heap sample drifted during the soak");
        }

        private static void Step(PerceptionBlackboardBridge bridge, RuntimeBlackboard blackboard)
        {
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
            bridge.PublishNearestPosition(blackboard, PositionKey);
            bridge.PublishCount(blackboard, CountKey);
        }

        private static double Percentile(double[] sorted, double q)
        {
            int index = (int)Math.Min(sorted.Length - 1, Math.Max(0, Math.Round(q * (sorted.Length - 1))));
            return sorted[index];
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
