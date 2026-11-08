using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.Perception;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.BehaviorTree.Integrations.Perception.Tests.Editor
{
    public sealed class PerceptionAggregatorTests
    {
        private const int VisibleKey = 71;
        private const int DistanceKey = 72;
        private const int PositionKey = 73;
        private const int CountKey = 74;
        private const int HandleKey = 75;
        private const int FromMemoryKey = 76;

        [Test]
        public void SameHandleFromTwoBridges_MergesToOne()
        {
            using var sight = new FakeSensor();
            using var hearing = new FakeSensor();
            using var sightBridge = new PerceptionBlackboardBridge(sight);
            using var hearingBridge = new PerceptionBlackboardBridge(hearing);
            using var aggregator = new PerceptionAggregator(capacity: 4);
            aggregator.Add(sightBridge);
            aggregator.Add(hearingBridge);

            sight.SetResults(Entry(9, 4f, fromMemory: false));
            hearing.SetResults(Entry(9, 7f, fromMemory: false));

            Assert.IsTrue(aggregator.Update());
            Assert.AreEqual(1, aggregator.Count, "one handle yields one entry");
            Assert.AreEqual(4f, aggregator.Nearest.Distance, 1e-5f, "between two live entries the nearer one wins");
        }

        [Test]
        public void LiveDetection_WinsOverRemembered()
        {
            using var remembered = new FakeSensor();
            using var live = new FakeSensor();
            using var rememberedBridge = new PerceptionBlackboardBridge(remembered);
            using var liveBridge = new PerceptionBlackboardBridge(live);
            using var aggregator = new PerceptionAggregator(capacity: 4);
            aggregator.Add(rememberedBridge);
            aggregator.Add(liveBridge);

            remembered.SetResults(Entry(9, 2f, fromMemory: true));
            live.SetResults(Entry(9, 5f, fromMemory: false));

            aggregator.Update();

            Assert.AreEqual(1, aggregator.Count);
            Assert.IsFalse(aggregator.Nearest.IsFromMemory, "a live detection outranks a remembered one");
            Assert.AreEqual(5f, aggregator.Nearest.Distance, 1e-5f);
        }

        [Test]
        public void TwoRememberedEntries_KeepTheNearer()
        {
            using var first = new FakeSensor();
            using var second = new FakeSensor();
            using var firstBridge = new PerceptionBlackboardBridge(first);
            using var secondBridge = new PerceptionBlackboardBridge(second);
            using var aggregator = new PerceptionAggregator(capacity: 4);
            aggregator.Add(firstBridge);
            aggregator.Add(secondBridge);

            first.SetResults(Entry(9, 6f, fromMemory: true));
            second.SetResults(Entry(9, 3f, fromMemory: true));

            aggregator.Update();

            Assert.AreEqual(1, aggregator.Count);
            Assert.AreEqual(3f, aggregator.Nearest.Distance, 1e-5f);
        }

        [Test]
        public void MergedTargets_AreOrderedByDistance()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 8);
            using var aggregator = new PerceptionAggregator(capacity: 8);
            aggregator.Add(bridge);

            sensor.SetResults(Entry(5, 5f, false), Entry(2, 2f, false), Entry(9, 9f, false));
            aggregator.Update();

            ReadOnlySpan<PerceptionTarget> targets = aggregator.Targets;
            Assert.AreEqual(3, targets.Length);
            for (int i = 1; i < targets.Length; i++)
            {
                Assert.LessOrEqual(targets[i - 1].Distance, targets[i].Distance, "merged output must be sorted");
            }
        }

        [Test]
        public void Truncation_KeepsTheNearestEntries()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor, capacity: 8);
            using var aggregator = new PerceptionAggregator(capacity: 2);
            aggregator.Add(bridge);

            sensor.SetResults(Entry(3, 3f, false), Entry(1, 1f, false), Entry(2, 2f, false));
            aggregator.Update();

            Assert.AreEqual(2, aggregator.Count);
            Assert.IsTrue(aggregator.IsTruncated);
            Assert.AreEqual(1f, aggregator.Targets[0].Distance, 1e-5f);
            Assert.AreEqual(2f, aggregator.Targets[1].Distance, 1e-5f);
        }

        [Test]
        public void Add_RejectsNullDuplicatesAndOverflow()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var otherBridge = new PerceptionBlackboardBridge(sensor);
            using var aggregator = new PerceptionAggregator(capacity: 4, bridgeCapacity: 1);

            Assert.IsFalse(aggregator.Add(null));
            Assert.IsTrue(aggregator.Add(bridge));
            Assert.IsFalse(aggregator.Add(bridge), "the same bridge must not be registered twice");
            Assert.IsFalse(aggregator.Add(otherBridge), "a full aggregator rejects new bridges");
        }

        [Test]
        public void Update_FromAnotherThread_Throws()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var aggregator = new PerceptionAggregator();
            aggregator.Add(bridge);

            Exception captured = null;
            Task.Run(() =>
            {
                try
                {
                    aggregator.Update();
                }
                catch (Exception exception)
                {
                    captured = exception;
                }
            }).GetAwaiter().GetResult();

            Assert.That(captured, Is.TypeOf<InvalidOperationException>());
        }

        [Test]
        public void Dispose_ClearsStateAndThrowsOnUse()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            var aggregator = new PerceptionAggregator();
            aggregator.Add(bridge);
            sensor.SetResults(Entry(1, 1f, false));
            aggregator.Update();
            Assert.AreEqual(1, aggregator.Count);

            aggregator.Dispose();

            Assert.AreEqual(0, aggregator.Count);
            Assert.Throws<ObjectDisposedException>(() => aggregator.Update());
        }

        [Test]
        public void Publish_WritesTheMergedNearest()
        {
            using var sight = new FakeSensor();
            using var hearing = new FakeSensor();
            using var sightBridge = new PerceptionBlackboardBridge(sight);
            using var hearingBridge = new PerceptionBlackboardBridge(hearing);
            using var aggregator = new PerceptionAggregator(capacity: 4);
            aggregator.Add(sightBridge);
            aggregator.Add(hearingBridge);
            using var blackboard = new RuntimeBlackboard();

            sight.SetResults(Entry(4, 2f, false, new float3(1f, 0f, 0f)));
            hearing.SetResults(Entry(8, 6f, false, new float3(9f, 0f, 0f)));

            aggregator.Update();
            aggregator.PublishNearest(blackboard, VisibleKey, DistanceKey);
            aggregator.PublishNearestPosition(blackboard, PositionKey);
            aggregator.PublishNearestHandle(blackboard, HandleKey);
            aggregator.PublishNearestFromMemory(blackboard, FromMemoryKey);
            aggregator.PublishCount(blackboard, CountKey);

            Assert.IsTrue(blackboard.GetBool(VisibleKey));
            Assert.AreEqual(2f, blackboard.GetFloat(DistanceKey), 1e-5f);
            Assert.AreEqual(2, blackboard.GetInt(CountKey));

            Assert.IsTrue(PerceptionBlackboardBridge.TryUnpackHandle(blackboard.GetLong3(HandleKey), out PerceptibleHandle handle));
            Assert.AreEqual(4, handle.Id);

            BTFloat3 position = blackboard.GetFloat3(PositionKey, BTFloat3.Zero);
            Assert.AreEqual(1f, position.X, 1e-5f);
            Assert.IsFalse(blackboard.GetBool(FromMemoryKey));
        }

        [Test]
        public void Fuzz_MergeRulesHold()
        {
            var random = new System.Random(20261108);
            using var aggregator = new PerceptionAggregator(capacity: 6, bridgeCapacity: 4);
            var bridges = new List<PerceptionBlackboardBridge>(4);
            var sensors = new List<FakeSensor>(4);

            try
            {
                for (int i = 0; i < 4; i++)
                {
                    var sensor = new FakeSensor();
                    var bridge = new PerceptionBlackboardBridge(sensor, capacity: 8);
                    sensors.Add(sensor);
                    bridges.Add(bridge);
                    aggregator.Add(bridge);
                }

                for (int round = 0; round < 2000; round++)
                {
                    var expected = new Dictionary<int, (float Distance, bool FromMemory)>();

                    for (int bridgeIndex = 0; bridgeIndex < bridges.Count; bridgeIndex++)
                    {
                        int count = random.Next(0, 6);
                        var entries = new DetectionResult[count];
                        for (int i = 0; i < count; i++)
                        {
                            int id = random.Next(1, 8);
                            float distance = (float)(random.NextDouble() * 20d);
                            bool fromMemory = random.Next(2) == 0;
                            entries[i] = Entry(id, distance, fromMemory);

                            if (!expected.TryGetValue(id, out (float Distance, bool FromMemory) current) ||
                                IsBetter(fromMemory, distance, current.FromMemory, current.Distance))
                            {
                                expected[id] = (distance, fromMemory);
                            }
                        }

                        sensors[bridgeIndex].SetResults(entries);
                    }

                    aggregator.Update();

                    Assert.AreEqual(Math.Min(expected.Count, aggregator.Capacity), aggregator.Count,
                        "merged count drifted at round " + round);
                    Assert.AreEqual(expected.Count > aggregator.Capacity, aggregator.IsTruncated,
                        "truncation flag drifted at round " + round);

                    ReadOnlySpan<PerceptionTarget> targets = aggregator.Targets;
                    var seen = new HashSet<int>();
                    for (int i = 0; i < targets.Length; i++)
                    {
                        Assert.IsTrue(seen.Add(targets[i].Handle.Id), "duplicate handle at round " + round);
                        Assert.IsTrue(expected.ContainsKey(targets[i].Handle.Id), "unexpected handle at round " + round);
                        (float Distance, bool FromMemory) expectedEntry = expected[targets[i].Handle.Id];
                        Assert.AreEqual(expectedEntry.Distance, targets[i].Distance, 1e-4f, "distance mismatch at round " + round);
                        Assert.AreEqual(expectedEntry.FromMemory, targets[i].IsFromMemory, "memory flag mismatch at round " + round);
                        if (i > 0)
                        {
                            Assert.LessOrEqual(targets[i - 1].Distance, targets[i].Distance, "sort broke at round " + round);
                        }
                    }
                }
            }
            finally
            {
                foreach (PerceptionBlackboardBridge bridge in bridges)
                {
                    bridge.Dispose();
                }
            }
        }

        [Test]
        public void UpdateAndPublish_DoNotRetainHeap()
        {
            using var sight = new FakeSensor();
            using var hearing = new FakeSensor();
            using var sightBridge = new PerceptionBlackboardBridge(sight);
            using var hearingBridge = new PerceptionBlackboardBridge(hearing);
            using var aggregator = new PerceptionAggregator(capacity: 6);
            aggregator.Add(sightBridge);
            aggregator.Add(hearingBridge);
            using var blackboard = new RuntimeBlackboard();

            sight.SetResults(Entry(1, 1f, false), Entry(2, 2f, true));
            hearing.SetResults(Entry(2, 5f, false), Entry(3, 3f, false));

            for (int i = 0; i < 5000; i++)
            {
                Step(aggregator, blackboard);
            }

            long before = GC.GetTotalMemory(true);
            for (int i = 0; i < 200000; i++)
            {
                Step(aggregator, blackboard);
            }

            long after = GC.GetTotalMemory(true);
            Assert.Less(after - before, 1024 * 1024, "steady-state merge and publish must not retain heap");
        }

        private static void Step(PerceptionAggregator aggregator, RuntimeBlackboard blackboard)
        {
            aggregator.Update();
            aggregator.PublishNearest(blackboard, VisibleKey, DistanceKey);
            aggregator.PublishNearestPosition(blackboard, PositionKey);
            aggregator.PublishNearestHandle(blackboard, HandleKey);
            aggregator.PublishNearestFromMemory(blackboard, FromMemoryKey);
            aggregator.PublishCount(blackboard, CountKey);
        }

        private static bool IsBetter(bool fromMemory, float distance, bool currentFromMemory, float currentDistance)
        {
            if (fromMemory != currentFromMemory)
            {
                return !fromMemory;
            }

            return distance < currentDistance;
        }

        private static DetectionResult Entry(int id, float distance, bool fromMemory, float3 position = default)
        {
            return new DetectionResult
            {
                Target = new PerceptibleHandle(1, id, 1),
                Distance = distance,
                LastKnownPosition = position,
                DetectionTime = 0d,
                Visibility = 1f,
                SensorType = SensorType.Sight,
                IsFromMemory = fromMemory
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
