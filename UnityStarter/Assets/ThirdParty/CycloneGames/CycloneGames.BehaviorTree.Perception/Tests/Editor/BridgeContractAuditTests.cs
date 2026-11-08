using System;
using System.Collections.Generic;
using System.Reflection;
using CycloneGames.AIPerception.Runtime;
using CycloneGames.BehaviorTree.Core;
using CycloneGames.BehaviorTree.Runtime.Integrations.Perception;
using NUnit.Framework;
using Unity.Collections;
using Unity.Mathematics;

namespace CycloneGames.BehaviorTree.Integrations.Perception.Tests.Editor
{
    /// <summary>
    /// Records what the bridge does and does not publish, and how it behaves on repeated calls.
    /// These tests exist to keep the review findings falsifiable: if the surface grows, they fail
    /// until someone updates the expectation on purpose.
    /// </summary>
    public sealed class BridgeContractAuditTests
    {
        private const int VisibleKey = 51;
        private const int DistanceKey = 52;
        private const int PositionKey = 53;
        private const int CountKey = 54;
        private const int HandleKey = 55;
        private const int FromMemoryKey = 56;

        [Test]
        public void PublishSurface_IsLimitedToNearestPositionAndCount()
        {
            var publishMethods = new List<string>();
            foreach (MethodInfo method in typeof(PerceptionBlackboardBridge)
                         .GetMethods(BindingFlags.Public | BindingFlags.Instance | BindingFlags.DeclaredOnly))
            {
                if (method.Name.StartsWith("Publish", StringComparison.Ordinal))
                {
                    publishMethods.Add(method.Name);
                }
            }

            publishMethods.Sort(StringComparer.Ordinal);

            Assert.AreEqual(
                "PublishCount,PublishNearest,PublishNearestFromMemory,PublishNearestHandle,PublishNearestPosition",
                string.Join(",", publishMethods),
                "the bridge gained or lost a publish method; update this expectation on purpose");
        }

        [Test]
        public void Publish_WritesIdentityAndMemoryOnlyWhenAsked()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(7, 5f, fromMemory: true));
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
            bridge.PublishNearestPosition(blackboard, PositionKey);
            bridge.PublishCount(blackboard, CountKey);

            Assert.IsFalse(blackboard.HasKey(HandleKey), "identity is opt-in, not written by the base publish");
            Assert.IsFalse(blackboard.HasKey(FromMemoryKey), "the memory flag is opt-in too");

            bridge.PublishNearestHandle(blackboard, HandleKey);
            bridge.PublishNearestFromMemory(blackboard, FromMemoryKey);

            Assert.IsTrue(blackboard.HasKey(HandleKey));
            Assert.IsTrue(blackboard.GetBool(FromMemoryKey), "the fake target is reported from memory");
        }

        [Test]
        public void Distance_IsLinearEvenWhenTheTargetIsRemembered()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            // A 3-4-5 triangle: linear distance is 5, squared would be 25.
            sensor.SetResults(Entry(7, 5f, fromMemory: true, position: new float3(3f, 4f, 0f)));
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
            bridge.PublishNearestPosition(blackboard, PositionKey);

            Assert.AreEqual(5f, blackboard.GetFloat(DistanceKey), 1e-5f, "the value is linear, not squared");

            BTFloat3 position = blackboard.GetFloat3(PositionKey, BTFloat3.Zero);
            Assert.AreEqual(3f, position.X, 1e-5f);
            Assert.AreEqual(4f, position.Y, 1e-5f);
        }

        [Test]
        public void Distance_StaysLinearAcrossMagnitudes([Values(0.001f, 1f, 5f, 1000f)] float distance)
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(7, distance, fromMemory: false));
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);

            Assert.AreEqual(distance, blackboard.GetFloat(DistanceKey), MathF.Max(1e-4f, distance * 1e-5f),
                "a squared value would differ at every magnitude above 1");
        }

        [Test]
        public void RepeatedUpdateAndPublish_AreIdempotent()
        {
            using var sensor = new FakeSensor();
            using var bridge = new PerceptionBlackboardBridge(sensor);
            using var blackboard = new RuntimeBlackboard();

            sensor.SetResults(Entry(7, 5f, fromMemory: false));
            bridge.Update();
            bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
            bridge.PublishNearestPosition(blackboard, PositionKey);
            bridge.PublishCount(blackboard, CountKey);

            ulong visibleStamp = blackboard.GetStamp(VisibleKey);
            ulong distanceStamp = blackboard.GetStamp(DistanceKey);
            ulong positionStamp = blackboard.GetStamp(PositionKey);
            ulong countStamp = blackboard.GetStamp(CountKey);

            for (int i = 0; i < 100; i++)
            {
                bridge.Update();
                bridge.PublishNearest(blackboard, VisibleKey, DistanceKey);
                bridge.PublishNearestPosition(blackboard, PositionKey);
                bridge.PublishCount(blackboard, CountKey);
            }

            Assert.AreEqual(visibleStamp, blackboard.GetStamp(VisibleKey));
            Assert.AreEqual(distanceStamp, blackboard.GetStamp(DistanceKey));
            Assert.AreEqual(positionStamp, blackboard.GetStamp(PositionKey));
            Assert.AreEqual(countStamp, blackboard.GetStamp(CountKey));
            Assert.AreEqual(1, bridge.Count);
        }

        [Test]
        public void TwoBridges_OnTheSameTarget_DoNotFuse()
        {
            using var sight = new FakeSensor();
            using var hearing = new FakeSensor();
            using var sightBridge = new PerceptionBlackboardBridge(sight);
            using var hearingBridge = new PerceptionBlackboardBridge(hearing);

            sight.SetResults(Entry(9, 4f, fromMemory: false));
            hearing.SetResults(Entry(9, 7f, fromMemory: false));

            sightBridge.Update();
            hearingBridge.Update();

            Assert.AreEqual(1, sightBridge.Count);
            Assert.AreEqual(1, hearingBridge.Count);
            Assert.AreEqual(
                sightBridge.Nearest.Handle,
                hearingBridge.Nearest.Handle,
                "both bridges report the same target");
            Assert.AreNotEqual(
                sightBridge.Nearest.Distance,
                hearingBridge.Nearest.Distance,
                "and each reports its own distance, so a shared view needs a layer above the bridges");
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
