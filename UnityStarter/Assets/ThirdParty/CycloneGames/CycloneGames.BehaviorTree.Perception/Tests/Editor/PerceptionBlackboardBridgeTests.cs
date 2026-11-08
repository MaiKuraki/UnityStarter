using System;
using System.Threading.Tasks;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.Perception;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.BehaviorTree.Integrations.Perception.Tests.Editor
{
    public sealed class PerceptionBlackboardBridgeTests
    {
        private const int VisibleKey = 11;
        private const int DistanceKey = 12;
        private const int PositionKey = 13;
        private const int CountKey = 14;

        [Test]
        public void Constructor_RejectsInvalidArguments()
        {
            Assert.Throws<ArgumentNullException>(() => new PerceptionBlackboardBridge(null));

            using var sensor = new FakeSensor();
            Assert.Throws<ArgumentOutOfRangeException>(() => new PerceptionBlackboardBridge(sensor, capacity: 0));
        }

        [Test]
        public void Update_CopiesResultsNearestFirst()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 4);

            sensor.SetResults(Entry(1, 2f, 1f), Entry(2, 5f, 0.5f), Entry(3, 9f, 0.25f));

            Assert.IsTrue(bridge.Update());
            Assert.AreEqual(3, bridge.Count);
            Assert.IsFalse(bridge.IsTruncated);
            Assert.AreEqual(1, bridge.Nearest.Handle.Id);
            Assert.AreEqual(2f, bridge.Nearest.Distance, 1e-5f);

            ReadOnlySpan<PerceptionTarget> targets = bridge.Targets;
            Assert.AreEqual(3, targets.Length);
            Assert.IsTrue(targets[0].Distance <= targets[1].Distance);
            Assert.IsTrue(targets[1].Distance <= targets[2].Distance);
        }

        [Test]
        public void Update_TruncatesAtCapacityAndReportsIt()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 2);

            sensor.SetResults(Entry(1, 1f, 1f), Entry(2, 2f, 1f), Entry(3, 3f, 1f));

            bridge.Update();

            Assert.AreEqual(2, bridge.Count);
            Assert.IsTrue(bridge.IsTruncated);
            Assert.AreEqual(1, bridge.Nearest.Handle.Id);
        }

        [Test]
        public void Update_WithEmptySensor_ReportsNoTarget()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);

            Assert.IsFalse(bridge.Update());
            Assert.AreEqual(0, bridge.Count);
            Assert.IsFalse(bridge.Nearest.Handle.IsValid);
        }

        [Test]
        public void PublishNearest_WritesFlagAndDistance()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(7, 3.5f, 0.8f));
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);

            Assert.IsTrue(blackboard.GetBool(VisibleKey));
            Assert.AreEqual(3.5f, blackboard.GetFloat(DistanceKey), 1e-5f);

            sensor.SetResults();
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);

            Assert.IsFalse(blackboard.GetBool(VisibleKey));
            Assert.AreEqual(0f, blackboard.GetFloat(DistanceKey), 1e-5f);
        }

        [Test]
        public void PublishNearestPosition_WritesFloat3Slot()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(7, 3.5f, 0.8f, new float3(1f, 2f, 3f)));
            bridge.Update();
            bridge.PublishNearestPosition(blackboard, PositionKey);

            BTFloat3 stored = blackboard.GetFloat3(PositionKey, BTFloat3.Zero);
            Assert.AreEqual(1f, stored.X, 1e-5f);
            Assert.AreEqual(2f, stored.Y, 1e-5f);
            Assert.AreEqual(3f, stored.Z, 1e-5f);
        }

        [Test]
        public void PublishCount_WritesTargetCount()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 4);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(1, 1f, 1f), Entry(2, 2f, 1f));
            bridge.Update();
            bridge.PublishCount(blackboard, CountKey);

            Assert.AreEqual(2, blackboard.GetInt(CountKey));
        }

        [Test]
        public void RepeatedPublish_WithSameValues_DoesNotBumpStamps()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(7, 3.5f, 0.8f));
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);

            ulong visibleStamp = blackboard.GetStamp(VisibleKey);
            ulong distanceStamp = blackboard.GetStamp(DistanceKey);

            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);

            Assert.AreEqual(visibleStamp, blackboard.GetStamp(VisibleKey));
            Assert.AreEqual(distanceStamp, blackboard.GetStamp(DistanceKey));
        }

        [Test]
        public void AfterDispose_CallsThrow()
        {
            using var sensor = new FakeSensor();
            var bridge = new PerceptionBlackboardBridge(sensor);
            bridge.Dispose();

            Assert.Throws<ObjectDisposedException>(() => bridge.Update());
        }

        [Test]
        public void Update_FromAnotherThread_Throws()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);

            Exception captured = null;
            Task.Run(() =>
            {
                try
                {
                    bridge.Update();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            }).GetAwaiter().GetResult();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Fuzz_RandomResultSetsKeepTheSnapshotConsistent()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 6);
            var random = new System.Random(20261108);

            for (int round = 0; round < 5000; round++)
            {
                int count = random.Next(0, 10);
                var entries = new DetectionResult[count];
                float distance = 0f;
                for (int i = 0; i < count; i++)
                {
                    distance += random.Next(0, 5) * 0.5f;
                    entries[i] = Entry(random.Next(1, 1000), distance, (float)random.NextDouble());
                }

                sensor.SetResults(entries);
                bridge.Update();

                Assert.AreEqual(Math.Min(count, bridge.Capacity), bridge.Count, "count drifted at round " + round);
                Assert.AreEqual(count > bridge.Capacity, bridge.IsTruncated, "truncation flag drifted at round " + round);

                if (bridge.Count > 0)
                {
                    Assert.AreEqual(entries[0].Target, bridge.Nearest.Handle, "nearest handle drifted at round " + round);
                    Assert.AreEqual(entries[0].Distance, bridge.Nearest.Distance, 1e-5f, "nearest distance drifted at round " + round);
                }
            }
        }

        private static DetectionResult Entry(int id, float distance, float visibility, float3 position = default)
        {
            return new DetectionResult
            {
                Target = new PerceptibleHandle(1, id, 1),
                Distance = distance,
                LastKnownPosition = position,
                DetectionTime = 0d,
                Visibility = visibility,
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
