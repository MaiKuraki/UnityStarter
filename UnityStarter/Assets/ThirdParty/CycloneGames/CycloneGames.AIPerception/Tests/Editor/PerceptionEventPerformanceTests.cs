using System;
using CycloneGames.AIPerception.Runtime;
using NUnit.Framework;
using Unity.Mathematics;
using UnityEngine;

namespace CycloneGames.AIPerception.Tests.Editor
{
    /// <summary>
    /// Measures what the transition layer costs when it is used, and confirms it costs nothing when
    /// no listener is registered. Explicit because it needs a longer window than the default gate.
    /// </summary>
    public sealed class PerceptionEventPerformanceTests
    {
        private const int Targets = 256;
        private const int Sensors = 128;

        [Test]
        [Explicit("Perception event overhead measurement; run on demand.")]
        public void EventLayer_CostsNothingWithoutListeners_AndStaysBoundedWithThem()
        {
            PerceptibleRegistry.ResetInstance();
            var registry = new PerceptibleRegistry(initialCapacity: Targets, maximumCapacity: Targets, cellSize: 8f);
            var clock = new ManualPerceptionClock(0d);
            var manager = new SensorManager(registry, maximumSensorCount: Sensors, clock: clock);
            var sensorObjects = new GameObject[Sensors];
            var sensors = new ProximitySensor[Sensors];
            var targets = new SpeedPerceptible[Targets];

            try
            {
                var random = new System.Random(4242);
                for (int i = 0; i < Targets; i++)
                {
                    targets[i] = new SpeedPerceptible
                    {
                        PerceptibleId = i,
                        Position = new float3(random.Next(64), 0f, random.Next(64)),
                        LineOfSightPoint = float3.zero,
                        IsDetectable = true
                    };
                    registry.Register(targets[i]);
                }

                registry.RebuildData();

                ProximitySensorConfig config = ProximitySensorConfig.Default;
                config.Radius = 12f;
                config.UpdateInterval = 0f;
                config.MemoryDuration = 1f;

                for (int i = 0; i < Sensors; i++)
                {
                    sensorObjects[i] = new GameObject("event-perf-sensor-" + i);
                    sensorObjects[i].transform.position = new Vector3(random.Next(64), 0f, random.Next(64));
                    sensors[i] = new ProximitySensor(sensorObjects[i].transform, config, manager);
                    Assert.IsTrue(manager.TryRegister(sensors[i]));
                }

                for (int i = 0; i < 200; i++)
                {
                    clock.Advance(0.016d);
                    manager.Update(0.016f);
                }

                double withoutListener = Measure(manager, clock, 2000);

                var listener = new CountingListener();
                manager.AddListener(listener);

                for (int i = 0; i < 200; i++)
                {
                    clock.Advance(0.016d);
                    manager.Update(0.016f);
                }

                listener.Reset();
                long heapBefore = GC.GetTotalMemory(true);
                double withListener = Measure(manager, clock, 2000);
                long heapAfter = GC.GetTotalMemory(true);

                TestContext.WriteLine(
                    $"sensors={Sensors} targets={Targets} withoutListenerMs={withoutListener:F4} " +
                    $"withListenerMs={withListener:F4} events={listener.Count} retainedBytes={heapAfter - heapBefore}");

                Assert.Less(heapAfter - heapBefore, 2 * 1024 * 1024, "event layer retained heap");
                Assert.Less(withListener, withoutListener * 4d + 1d, "event dispatch dominated the update cost");
            }
            finally
            {
                for (int i = 0; i < Sensors; i++)
                {
                    sensors[i]?.Dispose();
                    if (sensorObjects[i] != null)
                    {
                        UnityEngine.Object.DestroyImmediate(sensorObjects[i]);
                    }
                }

                manager.Dispose();
                registry.Dispose();
                PerceptibleRegistry.ResetInstance();
            }
        }

        private static double Measure(SensorManager manager, ManualPerceptionClock clock, int updates)
        {
            var watch = System.Diagnostics.Stopwatch.StartNew();
            for (int i = 0; i < updates; i++)
            {
                clock.Advance(0.016d);
                manager.Update(0.016f);
            }

            watch.Stop();
            return watch.Elapsed.TotalMilliseconds / updates;
        }

        private sealed class CountingListener : IPerceptionEventListener
        {
            public int Count { get; private set; }

            public void Reset()
            {
                Count = 0;
            }

            public void OnPerceptionEvent(in PerceptionEvent perceptionEvent)
            {
                Count++;
            }
        }

        private sealed class SpeedPerceptible : IPerceptible
        {
            public int PerceptibleId { get; set; }
            public int PerceptibleTypeId { get; set; }
            public int TeamId { get; set; }
            public bool IsDetectable { get; set; }
            public float3 Position { get; set; }
            public float DetectionRadius { get; set; }
            public float Loudness { get; set; }
            public bool IsSoundSource { get; set; }
            public string Tag => string.Empty;
            public float3 LineOfSightPoint { get; set; }
            public float3 GetLOSPoint() => LineOfSightPoint;
        }
    }
}
